using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Persistence;

namespace Akka.PersistentBackoff {
    /// <summary>
    /// Actor used to supervise actors with ability to restart them after back-off timeout occurred. 
    /// </summary>
    public class PersistentBackoffSupervisor : ReceivePersistentActor
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

        [Serializable]
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
        private int _eventsSinceLastSnapshot = 0;

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

            Initialise();
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

        public void Initialise() {
            #region recovery
            Recover<PersistentBackoffProtocol.Sent>(s => UpdateState(s));
            Recover<PersistentBackoffProtocol.TrackedMsg>(s => UpdateState(s));
            Recover<SnapshotOffer>(s => {
                BufferState state;
                if ((state = s.Snapshot as BufferState) != null)
                    State = state;
            });
            Recover<RecoveryCompleted>(_ => FlushBuffer());
            #endregion

            #region management

            Command<Tick>(s => FlushBuffer());
            Command<Terminated>(terminated => {
                if (_child != null && _child.Equals(terminated.ActorRef)) {
                    //restart and schedule a retry according to the backoff algorithm
                    _child = Context.Watch(Context.ActorOf(_childProps, _childName));
                    ScheduleRetry();
                }
            });
            Command<GetCurrentChild>(_ => Sender.Tell(new CurrentChild(_child)));

            Command<SaveSnapshotSuccess>(saved => {
                DeleteMessages(saved.Metadata.SequenceNr);
                DeleteSnapshots(new SnapshotSelectionCriteria(saved.Metadata.SequenceNr-1,saved.Metadata.Timestamp));
                Context.GetLogger().Debug("Snapshot saved");
            });

            #endregion

            Command<PersistentBackoffProtocol.Sent>(sent => {
                Persist(sent, s => {
                    UpdateState(sent);
                    backoff.Reset();
                    if (++_eventsSinceLastSnapshot % 10 == 0)
                    {
                        SaveSnapshot(State);
                    }
                });
            });

            Command<PersistentBackoffProtocol.Shutdown>(s => Context.Stop(Self));
            
            CommandAny(message => {
                //this here is a big sign that you should always explicitly model messages you send to your persistenct actor.
                //CommandAny should be avoided because it means you need to handle every PersistenceMessage explicitly
                if (message is IPersistenceMessage)
                    return;

                var trackedMsg = new PersistentBackoffProtocol.TrackedMsg(message, Sender);

                Persist(trackedMsg, (t) => {
                    UpdateState(trackedMsg);
                    if (!backoff.IsStarted()) _child.Forward(trackedMsg);
                    if (backoff.IsStarted() && retryScheduled == false) ScheduleRetry();
                });
            });
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

        private void UpdateState(PersistentBackoffProtocol.Sent sent) {
            State = State.Remove(sent.Id);
        }

        private void UpdateState(PersistentBackoffProtocol.TrackedMsg msg) {
            State = State.Add(msg);
        }
    }
}