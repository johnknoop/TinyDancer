using System.Threading.Tasks;

namespace TinyDancer.Consume.Session
{
	public delegate Task AsyncSessionMessageHandler<in TMessage>(TMessage message, string sessionId);
}