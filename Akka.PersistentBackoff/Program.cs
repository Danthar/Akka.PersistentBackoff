using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sqlite;

namespace Akka.PersistentBackoff
{
    class Program
    {
        static void Main(string[] args) {

            var system = ActorSystem.Create("example");
            SqlitePersistence.Get(system);
            var childProps = Props.Create(() => new RemoteService());
            var backoff = new ExponentialBackoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), 0.2);
            var delivery = system.ActorOf(Props.Create(() => new PersistentBackoffSupervisor("test-remote-service", childProps,"delivery", backoff)));

            int i = 0;
            do {
                delivery.Tell($"test {i++}");
                var s = Console.ReadLine();
                if (s == "stop") 
                    break;
            } while (true);
                
            delivery.Tell(new PersistentBackoffProtocol.Shutdown());

            system.Terminate().Wait();
        }
    }
}
