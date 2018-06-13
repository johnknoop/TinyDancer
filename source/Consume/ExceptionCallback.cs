using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public delegate void ExceptionCallback<in TException>(Message message, TException exception);
}