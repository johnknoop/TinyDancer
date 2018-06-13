using System.Threading.Tasks;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public delegate Task AsyncMessageHandler<in TMessage>(TMessage message);
}