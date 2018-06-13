using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public delegate Task UnrecognizedMessageHandler(IReceiverClient client, Message message);
}