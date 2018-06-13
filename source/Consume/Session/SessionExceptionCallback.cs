using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public delegate void SessionExceptionCallback<in TException>(Message message, TException exception, string sessionId);
}