using Microsoft.Azure.ServiceBus;
using Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public static class QueueClientExtensions
	{
		public static SessionMessageHandlerBuilder ConfigureSessions(this IQueueClient client, SessionConfiguration config = null) => new SessionMessageHandlerBuilder(client, config);
	}
}