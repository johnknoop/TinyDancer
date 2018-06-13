using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public delegate Task UnrecognizedSessionMessageHandler(IMessageSession session, Message message);
}