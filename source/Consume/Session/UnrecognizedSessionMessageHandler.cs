using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session
{
	public delegate Task UnrecognizedSessionMessageHandler(IMessageSession session, Message message);
}