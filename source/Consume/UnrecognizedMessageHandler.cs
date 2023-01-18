using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate Task UnrecognizedMessageHandler(ServiceBusReceivedMessage message);
}
