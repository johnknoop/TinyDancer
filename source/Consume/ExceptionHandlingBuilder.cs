using Azure.Messaging.ServiceBus;
using System;
using System.Threading.Tasks;

namespace TinyDancer.Consume
{
	public class ExceptionHandlingBuilder<TException> where TException : Exception
	{
		private readonly DeadLetterMessageAsync _deadletterDelegate;
		private readonly Func<ServiceBusReceivedMessage, Task> _completeMessageAsync;
		private readonly Func<ServiceBusReceivedMessage, Task> _abandonMessageAsync;

		internal ExceptionHandlingBuilder(DeadLetterMessageAsync deadletterDelegate, Func<ServiceBusReceivedMessage, Task> completeMessageAsync, Func<ServiceBusReceivedMessage, Task> abandonMessageAsync)
		{
			_deadletterDelegate = deadletterDelegate;
			_completeMessageAsync = completeMessageAsync;
			_abandonMessageAsync = abandonMessageAsync;
		}

		public ExceptionHandler<TException> Deadletter()
		{
			return Deadletter(null);
		}

		public ExceptionHandler<TException> Deadletter(DeadletterReasonProvider<TException> reason)
		{
			return async (message, exception) =>
			{
				await _deadletterDelegate(message, reason?.Invoke(message, exception) ?? string.Empty);
			};
		}

		/// <summary>
		/// Note that <c>maxTimes</c> is enforced at best effort. The DeliveryCount property of the message will be examined,
		/// but there might be another consumer of this queue or subscription that does not honor this behavior.
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public ExceptionHandler<TException> Abandon(int maxTimes = 0)
		{
			return async (message, exception) =>
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

		public ExceptionHandler<TException> Complete()
		{
			return async (message, exception) =>
			{
				await _completeMessageAsync(message);
			};
		}
	}
}
