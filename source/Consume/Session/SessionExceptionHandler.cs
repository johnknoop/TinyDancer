using System;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public delegate Task SessionExceptionHandler(IMessageSession session, Message message, Exception exception);
}