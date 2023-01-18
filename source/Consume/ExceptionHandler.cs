using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace TinyDancer.Consume
{
	public delegate Task ExceptionHandler(ServiceBusReceivedMessage message, Exception exception);
}
