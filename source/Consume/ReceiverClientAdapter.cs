using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TinyDancer.Consume
{
	internal class ReceiverClientAdapter
	{
		private readonly IReceiverClient _receiverClient;

		internal ReceiverClientAdapter(IReceiverClient receiverClient)
		{
			_receiverClient = receiverClient;
		}

		internal void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, MessageHandlerOptions messageHandlerOptions)
		{
			_receiverClient.RegisterMessageHandler(handler, messageHandlerOptions);
		}

		internal void RegisterSessionHandler(Func<IMessageSession, Message, CancellationToken, Task> handler, SessionHandlerOptions sessionHandlerOptions)
		{
			if (_receiverClient is IQueueClient queueClient)
			{
				queueClient.RegisterSessionHandler(handler, sessionHandlerOptions);
			}
			else if (_receiverClient is ISubscriptionClient subscriptionClient)
			{
				subscriptionClient.RegisterSessionHandler(handler, sessionHandlerOptions);
			}
		}

		public IReceiverClient GetClient()
		{
			return _receiverClient;
		}
	}
}