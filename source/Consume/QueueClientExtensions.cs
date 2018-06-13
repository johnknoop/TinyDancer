using Microsoft.Azure.ServiceBus;
using TinyDancer.Consume.Session;

namespace TinyDancer.Consume
{
	public static class QueueClientExtensions
	{
		public static SessionMessageHandlerBuilder ConfigureSessions(this IQueueClient client, SessionConfiguration config = null) => new SessionMessageHandlerBuilder(client, config);
	}
}