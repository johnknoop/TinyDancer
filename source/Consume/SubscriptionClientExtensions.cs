using Microsoft.Azure.ServiceBus;
using TinyDancer.Consume.Session;

namespace TinyDancer.Consume
{
	public static class SubscriptionClientExtensions
	{
		public static SessionMessageHandlerBuilder ConfigureSessions(this ISubscriptionClient client, SessionConfiguration config = null) =>
			new SessionMessageHandlerBuilder(client, config);
	}
}