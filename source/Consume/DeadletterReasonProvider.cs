using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public delegate string DeadletterReasonProvider<in TException>(Message message, TException exception);
}