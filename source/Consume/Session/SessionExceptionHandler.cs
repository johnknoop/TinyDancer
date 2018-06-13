using System;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session
{
	public delegate Task SessionExceptionHandler(IMessageSession session, Message message, Exception exception);
}