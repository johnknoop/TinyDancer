using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public class SessionClientWrapper
	{
		private readonly IQueueClient _queueClient;
		private readonly ISubscriptionClient _subscriptionClient;

		public SessionClientWrapper(ISubscriptionClient subscriptionClient)
		{
			_subscriptionClient = subscriptionClient;
		}

		public SessionClientWrapper(IQueueClient queueClient)
		{
			_queueClient = queueClient;
		}

		public void RegisterSessionHandler(Func<IMessageSession, Message, CancellationToken, Task> handler, SessionHandlerOptions sessionHandlerOptions)
		{
			if (_queueClient != null)
			{
				_queueClient.RegisterSessionHandler(handler, sessionHandlerOptions);
			}
			else
			{
				_subscriptionClient.RegisterSessionHandler(handler, sessionHandlerOptions);
			}
		}
	}
}