using System.Threading.Tasks;

namespace TinyDancer.Consume
{
	public delegate Task AsyncMessageHandler<in TMessage>(TMessage message);
}