using Microsoft.Azure.ServiceBus;
using Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public static class SubscriptionClientExtensions
	{
		public static SessionMessageHandlerBuilder ConfigureSessions(this ISubscriptionClient client, SessionConfiguration config = null) =>
			new SessionMessageHandlerBuilder(client, config);
	}
}