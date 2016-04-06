using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.PersistentBackoff {
    /// <summary>
    /// Actor used to supervise actors with ability to restart them after back-off timeout occurred. 
    /// </summary>
    public class PersistentBackoffSupervisor : PersistentActor
    {
        #region Messages

        /// <summary>
        /// Request <see cref="PersistentBackoffSupervisor"/> with this message to receive <see cref="CurrentChild"/> response with current child.
        /// </summary>
        [Serializable]
        public sealed class GetCurrentChild
        {
            public static readonly GetCurrentChild Instance = new GetCurrentChild();
            private GetCurrentChild() { }
        }

        [Serializable]
        public sealed class CurrentChild
        {
            public readonly IActorRef Ref;

            public CurrentChild(IActorRef @ref)
            {
                Ref = @ref;
            }
        }

        [Serializable]
        private sealed class Tick
        {
            public static readonly Tick Instance = new Tick();
            private Tick() { }
        }

        #endregion

        private class BufferState {
            public BufferState(List<PersistentBackoffProtocol.TrackedMsg> buffer = null)
            {
                Buffer = buffer ?? new List<PersistentBackoffProtocol.TrackedMsg>();
            }

            public IEnumerable<PersistentBackoffProtocol.TrackedMsg> Buffer { get; private set; }

            public BufferState Add(PersistentBackoffProtocol.TrackedMsg evt)
            {
                var list = new List<PersistentBackoffProtocol.TrackedMsg>();
                list.AddRange(Buffer);
                list.Add(evt);
                return new BufferState(list);
            }

            public BufferState Remove(Guid sentId) {
                var buffer = Buffer.Where(tracked => tracked.Id != sentId).ToList();
                return new BufferState(buffer);
            }
        }

        private readonly Props _childProps;
        private readonly string _childName;

        private IActorRef _child = null;
        private IBackoffStrategy backoff;

        private bool retryScheduled;

        private string persistenceId;

        public override string PersistenceId { get { return persistenceId; } }

        //buffer for messages which we are currently handling
        private BufferState State;

        public PersistentBackoffSupervisor(string persistenceId, Props childProps, string childName, IBackoffStrategy backoff)
        {
         
            this.persistenceId = persistenceId;
            _childProps = childProps;
            _childName = childName;
            this.backoff = backoff;
            State = new BufferState();
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            //we only work with the stoppingstrategy. Which is in line with the let-it-crash paradigm
            //by overriding it here we eliminate the flexibility of providing your own.
            //you can remove this ofcourse, so you can provide your own, for more flexibility
            return Akka.Actor.SupervisorStrategy.StoppingStrategy;
        }

        protected override void PreStart()
        {
            StartChildActor();
            base.PreStart();
        }
        
        protected override bool ReceiveRecover(object message) {
            BufferState state;
            if (message is SnapshotOffer &&
                (state = ((SnapshotOffer) message).Snapshot as BufferState) != null) {
                State = state;
            }
            else if (message is RecoveryCompleted) {
                FlushBuffer();
            }
            else return false;
            return true;
        }

        protected override bool ReceiveCommand(object message) {
            if (message is SaveSnapshotSuccess)
                return false;

            if (message is SaveSnapshotFailure) {
                var failure = (SaveSnapshotFailure) message;
                Context.GetLogger().Error("Error storing snapshot: {0}", failure.Cause.GetBaseException().Message);
                return false;
            }
            if (message is Tick) {
                FlushBuffer();
                return true;
            } else if (message is Terminated)
            {
                var terminated = (Terminated)message;
                if (_child != null && _child.Equals(terminated.ActorRef))
                {
                    //restart and schedule a retry according to the backoff algorithm
                    _child = Context.Watch(Context.ActorOf(_childProps, _childName));
                    ScheduleRetry();
                }
                return true;
            } else if (message is PersistentBackoffProtocol.Sent)
            {
                //ack of monitored actor. Remove succesfull message and reset the backoff
                UnMonitorMessage((PersistentBackoffProtocol.Sent)message);
                backoff.Reset();
                return true;
            } else if (message is GetCurrentChild)
            {
                Sender.Tell(new CurrentChild(_child));
                return true;
            }

            var trackedMsg = MonitorMessage(message, Sender);
            if (!backoff.IsStarted()) _child.Forward(trackedMsg);
            if (backoff.IsStarted() && retryScheduled == false) ScheduleRetry();
            return true;
        }

        private void ScheduleRetry()
        {
            Context.System.Scheduler.ScheduleTellOnce(backoff.NextDelay(), Self, Tick.Instance, Self);
            retryScheduled = true;
        }

        private void StartChildActor()
        {
            if (_child == null) _child = Context.Watch(Context.ActorOf(_childProps, _childName));
        }

        private void FlushBuffer() {
            //we could extend this code, to have the trackedMsg include a retry counter
            //which we can monitor, and implement 'give-up' mechanics after X amount of retry's
            foreach (var msg in State.Buffer)
            {
                _child.Tell(msg, msg.Sender);
            }
            retryScheduled = false;
        }

        private PersistentBackoffProtocol.TrackedMsg MonitorMessage(object message, IActorRef sender) {
            var trackedMsg = new PersistentBackoffProtocol.TrackedMsg(message, sender);
            State = State.Add(trackedMsg);
            SaveSnapshot(State);
            return trackedMsg;
        }

        private void UnMonitorMessage(PersistentBackoffProtocol.Sent sent) {
            State = State.Remove(sent.Id);
            DeleteSnapshots(SnapshotSelectionCriteria.Latest);
            if(State.Buffer.Any())
                SaveSnapshot(State);
        }
    }
}