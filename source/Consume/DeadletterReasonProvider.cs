using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate string DeadletterReasonProvider<in TException>(Message message, TException exception);
}