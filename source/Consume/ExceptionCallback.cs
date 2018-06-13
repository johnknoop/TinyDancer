using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate void ExceptionCallback<in TException>(Message message, TException exception);
}