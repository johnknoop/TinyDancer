using System.Threading.Tasks;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session
{
	public delegate Task AsyncSessionMessageHandler<in TMessage>(TMessage message, string sessionId);
}