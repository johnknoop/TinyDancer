using Azure.Messaging.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate string DeadletterReasonProvider<in TException>(ServiceBusReceivedMessage message, TException exception);
}
