using System;
using Akka.Actor;

namespace Akka.PersistentBackoff {
    public class RemoteService : ReceiveActor {
        private int received = 0;
        public RemoteService() {

            Receive<PersistentBackoffProtocol.TrackedMsg>(tracked => tracked.Msg is string, tracked => {
                var command = tracked.Msg as string;

                //simulate a failure after 5 messages
//                if(received == 5)
//                    throw new Exception("Boom");

                Console.WriteLine($"Received message : {command}");
                received++;
                Context.Parent.Tell(new PersistentBackoffProtocol.Sent(tracked.Id));
            });
        }
    }
}