using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate Task ExceptionHandler<in TException>(ServiceBusReceivedMessage message, TException exception);

	public static class ReceiverClientExtensions
	{
		public static MessageHandlerBuilder ConfigureTinyDancer(this ServiceBusProcessor processor) =>
			new MessageHandlerBuilder(processor);

		public static MessageHandlerBuilder ConfigureTinyDancer(this ServiceBusSessionProcessor processor) =>
			new MessageHandlerBuilder(processor);
	}
}
