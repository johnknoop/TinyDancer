using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session
{
	public delegate void SessionExceptionCallback<in TException>(Message message, TException exception, string sessionId);
}