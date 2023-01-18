using System;
using Azure.Messaging.ServiceBus;
using System.Threading.Tasks;

namespace TinyDancer.Consume
{
	public class UnrecognizedMessageHandlingBuilder
	{
		private readonly DeadLetterMessageAsync _deadletterDelegate;
		private readonly Func<ServiceBusReceivedMessage, Task> _completeMessageAsync;
		private readonly Func<ServiceBusReceivedMessage, Task> _abandonMessageAsync;

		internal UnrecognizedMessageHandlingBuilder(DeadLetterMessageAsync deadletterDelegate, Func<ServiceBusReceivedMessage, Task> completeMessageAsync, Func<ServiceBusReceivedMessage, Task> abandonMessageAsync)
		{
			_deadletterDelegate = deadletterDelegate;
			_completeMessageAsync = completeMessageAsync;
			_abandonMessageAsync = abandonMessageAsync;
		}

		public UnrecognizedMessageHandler Deadletter(Func<ServiceBusReceivedMessage, string> reason)
		{
			return async (message) =>
			{
				await _deadletterDelegate(message, reason(message));
			};
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public UnrecognizedMessageHandler Abandon(int maxTimes = 0)
		{
			return async (message) =>
			{
				if (message.DeliveryCount <= maxTimes)
				{
					await _abandonMessageAsync(message);
				}
				else
				{
					await _deadletterDelegate(message, "Maximum number of retries reached");
				}
			};
		}

		public UnrecognizedMessageHandler Complete()
		{
			return async (message) =>
			{
				await _completeMessageAsync(message);
			};
		}
	}
}
