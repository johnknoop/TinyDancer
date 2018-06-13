using System;

namespace TinyDancer.Consume
{
	public class ExceptionHandlingBuilder<TException> where TException : Exception
	{
		public ExceptionHandler<TException> Deadletter()
		{
			return Deadletter(null);
		}

		public ExceptionHandler<TException> Deadletter(DeadletterReasonProvider<TException> reason)
		{
			return async (client, message, exception) =>
			{
				await client.DeadLetterAsync(message.SystemProperties.LockToken, reason?.Invoke(message, exception) ?? string.Empty);
			};
		}

		/// <summary>
		/// Note that <c>maxTimes</c> is enforced at best effort. The DeliveryCount property of the message will be examined,
		/// but if there is another consumer of this queue or subscription that does not implement ..............
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public ExceptionHandler<TException> Abandon(int maxTimes = 0)
		{
			return async (client, message, exception) =>
			{
				if (message.SystemProperties.DeliveryCount <= maxTimes)
				{
					await client.AbandonAsync(message.SystemProperties.LockToken);
				}
				else
				{
					await client.DeadLetterAsync(message.SystemProperties.LockToken);
				}
			};
		}

		public ExceptionHandler<TException> Complete()
		{
			return async (client, message, exception) =>
			{
				await client.CompleteAsync(message.SystemProperties.LockToken);
			};
		}
	}
}