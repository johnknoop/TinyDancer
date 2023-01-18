using Azure.Messaging.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate void ExceptionCallback<in TException>(ServiceBusReceivedMessage message, TException exception);
}
