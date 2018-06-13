TinyDancer is a high-level abstraction layer on top of the [Azure Service Bus client](https://www.nuget.org/packages/Microsoft.Azure.ServiceBus/).

## Install
	PM> Install-Package TinyDancer
	
### Consume different types of messages from the same queue/topic
```csharp
var client = new QueueClient(...); // or SubscriptionClient

client.Configure()
	.HandleMessage<SeatsReserved>(seatsReserved => { 
		// A user reserved one or more seats
		SaveReservation(...);
		LockSeats(...);
	})
	.HandleMessage<SeatsDiscarded>(async seatsDiscarded => { 
		// A user has discarded a reservation
		await RemoveReservation(...);
		await FreeUpSeats(...);
	})
	.Catch<DbConnectionFailedException>(x => x.Abandon(maxTimes: 2)) // Probably network instability. Try one more time.
	.OnUnrecognizedMessageType(x => x.Abandon()) // Let a different consumer handle this one
	.CatchUnhandledExceptions(x => x.Deadletter(),
		(msg, ex) => _logger.Error($"Error while processing message {msg.Id}", ex))
	.Subscribe();
```

### Publish a message
```csharp
// Simple:
await client.PublishAsync(myMessageObject);

// ...or with all options:
await client.PublishAsync(
	payload: myMessageObject,
	sessionId: "", // For queues/subscriptions with sessions enabled.
	deduplicationIdentifier: "", // For queues/topics that support deduplication:
	compress: true, // Serialize using MessagePack for smaller byte-size
	correlationId: x => x.AnyString
	);
```

### Why?
Frameworks like Rebus and MassTransit are great, but were not written specifically with Azure Service Bus in mind, and adds a lot of features you might not need. In contrast, this tiny library relies on the Azure Service Bus client, but provides a simplified interface to a number of important concerns:

- Serialization and deserialization (JSON and MessagePack are supported)
- Prevention of partial/unacknowledged message handling
- Decoupling of application logic from servicebus concepts when it comes to fault tolerance (see [exception handling](#exception-handling))

# Documentation

#### Receiving messages
- [Consume by type](#consume-by-type)
- [Subscribe to all](#subscribe-to-all)
- [Exception handling](#exception-handling)
	- Retry (abandon) / Deadletter / Complete
	- [Callbacks](#callbacks)
- [Sessions](#sessions)
- [Handle malformed or unknown messages](#handle-malformed-or-unknown-messages)
- [Preventing unacknowledged message handling](#preventing-partial-message-handling)

#### Sending messages
- PublishMany
- Deduplication
- Compression
- Correlation id

## Receiving Messages

### Consume by type

When you publish a message using TinyDancer, the message type is added to the metadata of the message. Thus, on the receiving end, handling messages of different types is as easy as:

```csharp
client.HandleMessage<TMessage>((TMessage msg) => { /* ... */})
```

#### A note about messages types...

In theory, you could maintain a copy of this object model in both assemblies, but a better idea is to distribute message types as a shared library.

### Subscribe to all

For cases when your topic/queue only contains messages of the same type, you can use the `ConsumeAllAs<T>` method.

### Exception handling

Any exception that your handler cannot recover from gracefully can be allowed to bubble up from your code. This lets you deal with the concern of what to do with the message without allowing concepts like *deadletter* or *abandon* to leak into your application logic.

`.Catch<MyException>(action, callback)`

You have three options for what to do with the message, when `MyException` occurs:

- **Abandon**(_maxTimes = null_)
  -- Will return the message to the queue/subscription so that it may be retried again. If it has already been retried _maxTimes_ number of times, it will be deadlettered.
- **Deadletter**(_reason = null_)
  -- Will move the message to the deadletter queue. Useful for poison messages, where you know it can not be handled by any running consumer.
- **Complete**()
  -- Acknowledges the message.

#### Callbacks

Use this to carry out any side-effects, like logging etc.

#### Unexpected exceptions

`.CatchUnhandledExceptions(action, callback)` will catch all other types of exceptions thrown from your handler.

Note that these exception handlers only will be triggered when an exception occurs in user code (or any library used below that). Exceptions thrown from the ServiceBus library will break execution, as this would indicate an unsafe state to operate in.

### Sessions
Sessions are the way Azure Service Bus guarantees order of delivery.

For a consuming application, use `client.ConfigureSessions` instead of `client.Configure`. Everything else is the same.

### Handle malformed or unknown messages
If a message arrives with an unknown message type, you might want to release the message back onto the queue/subscription, to give another consumer the chance to process it. However, if no consumer can handle the message, it's best to set a maximum number of "retries" to prevent it from bouncing around until it expires:
```csharp
.OnUnrecognizedMessageType(x => x.Abandon(maxTimes: 10))
```
If a received message cannot be deserialized, it might mean that the schema has changed and that the sending application is newer than the (currently curring) consuming application. Abandoning it might be the best solution for this scenario as well:
```csharp
.OnDeserializationFailed(x => x.Abandon(maxTimes: 10))
```
There's also an overload of this method that takes a callback, if you want to do some logging.

Both `OnUnrecognizedMessageType` and `OnDeserializationFailed` offer the choice to `Abandon`, `Deadletter` or `Complete` the message.

### Preventing partial message handling
It's always a good idea to make your message handling idempotent, given the at-least-once delivery nature of the Service Bus. However, Azure Service Bus supports deduplication, and some operations might be hard to carry out idempotently, like sending an e-mail. Therefore you want to avoid sending that e-mail without marking the message as complete. Otherwise, it will be consumed again.

There are two overloads of the `Subscribe` method. One of them takes a `CancellationToken` and a `Func<IDisposable>` as arguments. You can use these to prevent the program from terminating in the middle of message processing. TinyDancer will check the cancellation token whenever a message is received. If cancellation has been requested, the message is returned to the queue. The `Func<Disposable>` is a way to signal back to the program that work is being done and interruption should be avoided.

This integrates nicely with [this package](https://github.com/johnknoop/GracefulConsoleRunner).

## Sending messages
(documentation coming)