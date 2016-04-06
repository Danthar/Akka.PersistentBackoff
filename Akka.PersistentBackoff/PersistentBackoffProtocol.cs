using System;
using Akka.Actor;

namespace Akka.PersistentBackoff {
    public abstract class PersistentBackoffProtocol
    {
        [Serializable]
        public sealed class TrackedMsg
        {
            public Guid Id { get; private set; }
            public dynamic Msg { get; private set; }
            public IActorRef Sender { get; private set; }

            public TrackedMsg(dynamic msg, IActorRef sender)
            {
                Id = Guid.NewGuid();
                Msg = msg;
                Sender = sender;
            }
        }

        [Serializable]
        public sealed class Sent
        {
            public Guid Id { get; private set; }

            public Sent(Guid id)
            {
                Id = id;
            }
        }
    }
}