# Akka.PersistentBackoff

Targets Akka 1.0.6

This is a small project that integrates a PersistentActor with a backoff supervisor.

It employs an AtLeastOnce Delivery mechanism. 
Where the default AtLeastOnceDeliveryActor is especially suited for communication across the network with other actors. This actor is very suited 
for wrapping actors that communicate with non-actor based endpoints, like remote 3th party webservices, a database connection, or other network 
based endpoints. 

The exponential backoff supervisor ensures that when communication with a remote endpoint fails, it wont hammer it with requests.
The persistent part ensures that messages buffered will survive process restarts.