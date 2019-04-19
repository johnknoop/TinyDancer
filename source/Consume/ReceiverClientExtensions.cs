using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TinyDancer.Consume
{
	public delegate Task ExceptionHandler<in TException>(IReceiverClient client, Message message, TException exception);

	public static class ReceiverClientExtensions
	{
		public static MessageHandlerBuilder Configure(this IReceiverClient client, Configuration config = null) =>
			new MessageHandlerBuilder(new ReceiverClientAdapter(client), config);
		
		public static MessageHandlerBuilder ConfigureSessions(this IReceiverClient client, SessionConfiguration config = null) =>
			new MessageHandlerBuilder(new ReceiverClientAdapter(client), config);
	}
}