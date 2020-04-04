TinyDancer is a high-level abstraction layer on top of the [Azure Service Bus client](https://www.nuget.org/packages/Microsoft.Azure.ServiceBus/) with some convenient features such as handling multiple types of messages, dependency injection, decoupled fault tolerance etc.

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
Unlike frameworks such as Rebus and MassTransit, TinyDancer will not create any queues or topics. We think you should do that yourself. Once you have a `QueueClient` or `SubscriptionClient`, TinyDancer provides a simple yet powerful interface to a number of important concerns:

- Serialization and deserialization (JSON and MessagePack are supported)
- Prevention of partial/unacknowledged message handling
- Decoupling of application logic from servicebus concepts when it comes to fault tolerance (see [exception handling](#exception-handling))
- Dependency resolution

# Documentation

#### Receiving messages
- [Consume by type](#consume-by-type)
- [Subscribe to all](#subscribe-to-all)
- [Exception handling](#exception-handling)
    - Retry (abandon) / Deadletter / Complete
    - [Callbacks](#callbacks)
- [Dependency injection](#dependency-injection)
- [Sessions](#sessions)
- [Handle malformed or unknown messages](#handle-malformed-or-unknown-messages)
- [Graceful shutdown](#graceful-shutdown)
- [Preventing unacknowledged message handling](#preventing-partial-message-handling)
- [Receive message in same culture as when sent](#culture)

#### [Sending messages](#sending-messages-1)
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

- **Abandon**(_maxTimes = null_)\
  Will return the message to the queue/subscription so that it may be retried again. If it has already been retried _maxTimes_ number of times, it will be deadlettered.
- **Deadletter**(_reason = null_)\
  Will move the message to the deadletter queue. Useful for poison messages, where you know it can not be handled by any running consumer.
- **Complete**()\
  Acknowledges the message.

#### Callbacks

Use this to carry out any side-effects, like logging etc.

#### Example:
  ```csharp
    .Catch<MyException>(x => x.Abandon(), msg => _logger.Error(...))
  ```

#### Unhandled exceptions

`.CatchUnhandledExceptions(action, callback)` will catch all other types of exceptions thrown from your handler.

Note that these exception handlers only will be triggered when an exception occurs in user code (or any library used below that). Exceptions thrown from the ServiceBus library will break execution, as this would indicate an unsafe state to operate in.

### Dependency injection

TinyDancer can be integrated with `Microsoft.Extensions.DependencyInjection`. Just pass an instance of `IServiceCollection` to the `RegisterDependencies` method, and then you can add parameters to your handler functions:

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        queueClient.Configure()
            .RegisterDependencies(services)
            // Inject dependencies like this:
            .HandleMessage<Animal, IRepository<Animal>>(async (message, animalRepo) =>
            {
                await animalRepo.InsertAsync(message);
            })
            // Or like this:
            .HandleMessage(async (Car message, IRepository<Car> carRepo, ILogger logger) =>
            {
                await carRepo.InsertAsync(message);
                logger.Info($"Saved car with id {message.CarId}")
            })
            .Subscribe();
    }
}
```

The first parameter must always be the message, and all subsequent parameters will be resolved.

A new dependency scope is created and disposed for each message that is handled, so any dependencies registered with `AddScoped` will be resolved and disposed correctly.

If you need to use information from your messages as part of your service resolution, a `Message` is added to your `IServiceCollection` before the handler is called, and can be used like this:

```csharp
services.AddScoped<IRepository<Animal>>(provider =>
{
    // In order to resolve IRepository<Animal>, we need the Tenant key from the incoming message:
    var userProperties = provider.GetRequiredService<Message>().UserProperties;
    return new Repository<Animal>(userProperties["TenantKey"]);
});
```

Any errors occuring during dependency resolution, for example if a required service isn't registered, can be caught using `OnDependencyResolutionException`.

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

### Graceful shutdown
If you want your message handling to be drained and terminated gracefully, just like HTTP requests in an ASP.NET application, then use the `SubscribeUntilShutdownAsync` method, which takes a `CancellationToken` representing application shutdown.

The simplest way is to write your code as a [hosted service](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-2.2&tabs=visual-studio), extending the [BackgroundService](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice?view=aspnetcore-2.2) class:

```csharp
public class MyMessageHandler : BackgroundService
{
    private readonly IReceiverClient _serviceBusClient;

    public MyMessageHandler(IReceiverClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient;
    }

    public override Task ExecuteAsync(CancellationToken applicationStopping)
    {
        return _serviceBusClient
            .Configure()
            // Set up your message handling etc
            .SubscribeUntilShutdownAsync(applicationStopping);
    }
}
```
This way, TinyDancer will be notified when application shutdown is initiated. It will then allow in-flight messages to be handled completely, but will not accept any new ones.

## Sending messages

TinyDancer provides a couple of extension methods to `ISenderClient` (`IQueueClient` and `ITopicClient` both implement this interface).

### Publish a single message

#### Signature:
```csharp
Task PublishAsync<TMessage>(
      this ISenderClient client,
      TMessage payload,
      string sessionId = null,
      string deduplicationIdentifier = null,
      string correlationId = null,
      IDictionary<string, object> userProperties = null)
```

#### Example:

```csharp
await client.PublishAsync(
    payload: myMessage,
    sessionId: sessionId, // Optional
    deduplicationIdentifier: deduplicationIdentifier, // Optional
    correlationId: correlationId, // Optional
    userProperties: userProps); // Optional
```

### Publish multiple messages

#### Signature:
```csharp
Task PublishAllAsync<TMessage>(
      this ISenderClient client,
      IList<TMessage> payloads,
      string sessionId = null,
      Func<TMessage, string> deduplicationIdentifier = null,
      Func<TMessage, string> correlationId = null,
      IDictionary<string, object> userProperties = null)
```

#### Example:

```csharp
await client.PublishAllAsync(
    payloads: messages,
    sessionId: sessionId, // Optional
    deduplicationIdentifier: deduplicationIdentifier,  // Optional
    correlationId:  correlationId, // Optional
    userProperties: userProps); // Optional
```