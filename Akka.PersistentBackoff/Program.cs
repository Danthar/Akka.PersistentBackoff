using System;
using Akka.Actor;

namespace Akka.PersistentBackoff
{
    class Program
    {
        static void Main(string[] args)
        {

            using (var system = ActorSystem.Create("example")) {

                var childProps = Props.Create(() => new RemoteService());
                var backoff = new ExponentialBackoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), 0.2);
                var delivery = system.ActorOf(Props.Create(() => new PersistentBackoffSupervisor("test-remote-service", childProps,"delivery", backoff)));

                int i = 0;
                do {
                    delivery.Tell($"test {i++}");
                    Console.ReadLine();
                } while (true);
                
            }
        }
    }
}
