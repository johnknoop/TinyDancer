TinyDancer is a high-level abstraction layer on top of the [Azure Service Bus client](https://www.nuget.org/packages/Azure.Messaging.ServiceBus/) with some convenient features such as handling multiple types of messages, dependency injection, decoupled fault tolerance etc.

### Major features
TinyDancer provides a simple yet powerful interface to a number of important concerns:

- Prevention of partial/unacknowledged message handling through graceful shutdown
- Decoupling of application logic from servicebus concepts when it comes to fault tolerance (see [exception handling](#exception-handling))
- Dependency resolution

## Install
    PM> Install-Package TinyDancer

### Consume different types of messages from the same queue/subscription
```csharp
var messageProcessor = serviceBusClient.CreateProcessor(...)

messageProcessor
    .ConfigureTinyDancer()
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
    .SubscribeAsync();
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

## Dependencies

| Major version | Framework requirement | Dependencies              |
|---------------|-----------------------|---------------------------|
| 4.x           | .NET 6                | Azure.Messaging.ServiceBus, NodaTime                  |
| 3.x           | .NET Standard 2.1     | Microsoft.Azure.ServiceBus, Newtonsoft.Json, NodaTime |

## Documentation

- [Receiving messages](#receiving-messages)
    - [Consume by type](#multiplexing)
    - [Subscribe to all](#subscribe-to-all)
    - [Exception handling](#exception-handling)
        - Retry (abandon) / Deadletter / Complete
        - [Callbacks](#callbacks)
    - [Dependency injection](#dependency-injection)
    - [Sessions](#sessions)
    - [Handle malformed or unknown messages](#handle-malformed-or-unknown-messages)
    - [Graceful shutdown](#graceful-shutdown)
    - [Receive message in same culture as when sent](#receive-message-in-same-culture-as-when-sent)
    - [Release message early](#release-message-early)
- [Sending messages](#sending-messages-1)


## Receiving Messages

### Multiplexing

When you publish a message using TinyDancer, the message type is added to the metadata of the message. Thus, on the receiving end, handling messages of different types is as easy as:

```csharp
client.HandleMessage<TMessage>(async (TMessage msg) => { /* ... */})
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

TinyDancer can be integrated with `Microsoft.Extensions.DependencyInjection`. Just call `AddTinyDancer()` on your service collection:

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTinyDancer();
    }
}
```

A new dependency scope is created and disposed for each message that is handled, so any dependencies registered with `AddScoped` will be resolved and disposed correctly.

If you need to use information from your messages as part of your service resolution, a `ServiceBusReceivedMessage` is added to your `IServiceCollection` before the handler is called, and can be used like this:

```csharp
services.AddScoped<IRepository<Animal>>(provider =>
{
    // In order to resolve IRepository<Animal>, we need the Tenant key from the incoming message:
    var appProperties = provider.GetRequiredService<ServiceBusReceivedMessage>().ApplicationProperties;
    return new Repository<Animal>(appProperties["TenantKey"]);
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
Passing a `CancellationToken` representing application shutdown as argument to `SubscribeAsync` will ensure that no more messages are received once application termination has begun.

If you also want to ensure that all ongoing message handlers are allowed to finish before exiting, then pass `true` as argument for the `blockInterruption` parameter of the same method. This can be useful if you're code doesn't handle cancellation, or if you have multiple side-effects which all need to complete atomically, and there is no support for transactions (such as writing to the file system).

The simplest way is to write your code as a [hosted service](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-2.2&tabs=visual-studio), extending the [BackgroundService](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice?view=aspnetcore-2.2) class:

```csharp
public class MyMessageHandler : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;

    public MyMessageHandler(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient;
    }

    public override Task ExecuteAsync(CancellationToken applicationStopping)
    {
        return _serviceBusClient
            .CreateMessageProcessor(...)
            .ConfigureTinyDancer()
            // Set up your message handling etc here
            .SubscribeAsync(
                blockInterruption: true,
                cancellationToken: applicationStopping
            );
    }
}
```
This way, TinyDancer will be notified when application shutdown is initiated. It will then allow in-flight messages to be handled completely, but will not accept any new ones.

### Receive message in same culture as sent in

TinyDancer can set the thread culture of the thread that handles a message to the same culture as that of the thread that published the message, impacting things like number and date formatting. This is useful in when sending message between services in a multi-tenant system where the tenants may have different cultural preferences.

Use `.ConsumeMessagesInSameCultureAsSentIn()` to enable this feature.

### Release message early

If your message handling results in a really time-consuming operation, and you want to settle the message (meaning complete, abandon or deadletter it) before the operation has completed, you can use the `MessageSettler` helper. Just declare it as a dependency in your handler and call it whenever you feel like it:

```cs
messageReceiver.Configure()
    //...
    .HandleMessage<MyMessage, MessageSettler>(async (msg, settler) =>
    {
        await settler.CompleteAsync();

        // Do more work...
    })
```

Please note that settling a message early does not mean the next message in the queue will get consumed right away. The `MaxConcurrentSessions`/`MaxConcurrentMessages` settings limit the number of messages in process concurrently, and a message is still considered in process until the handler completes, regardless of whether or not you settle it early.

## Sending messages

TinyDancer provides a couple of extension methods to `ServiceBusSender`.

### Publish a single message

#### Signature:
```csharp
Task PublishAsync<TMessage>(
      this ServiceBusSender sender,
      TMessage payload,
      string sessionId = null,
      string deduplicationIdentifier = null,
      string correlationId = null,
      IDictionary<string, object> userProperties = null)
```

#### Example:

```csharp
await sender.PublishAsync(
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
      this ServiceBusSender sender,
      IList<TMessage> payloads,
      string sessionId = null,
      Func<TMessage, string> deduplicationIdentifier = null,
      Func<TMessage, string> correlationId = null,
      IDictionary<string, object> userProperties = null)
```

#### Example:

```csharp
await sender.PublishAllAsync(
    payloads: messages,
    sessionId: sessionId, // Optional
    deduplicationIdentifier: deduplicationIdentifier,  // Optional
    correlationId:  correlationId, // Optional
    userProperties: userProps); // Optional
```
