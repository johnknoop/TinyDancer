using System;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session
{
	public class SessionExceptionHandlingBuilder<TException> where TException : Exception
	{
		public SessionExceptionHandler<TException> Deadletter()
		{
			return Deadletter(null);
		}

		public SessionExceptionHandler<TException> Deadletter(DeadletterReasonProvider<TException> reason)
		{
			return async (session, message, exception) =>
			{
				await session.DeadLetterAsync(message.SystemProperties.LockToken, reason?.Invoke(message, exception) ?? string.Empty);
			};
		}

		/// <summary>
		/// Note that <c>maxTimes</c> is enforced at best effort. The DeliveryCount property of the message will be examined,
		/// but if there is another consumer of this queue or subscription that does not implement ..............
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public SessionExceptionHandler<TException> Abandon(int maxTimes = 0)
		{
			return async (session, message, exception) =>
			{
				if (message.SystemProperties.DeliveryCount <= maxTimes)
				{
					await session.AbandonAsync(message.SystemProperties.LockToken);
				}
				else
				{
					await session.DeadLetterAsync(message.SystemProperties.LockToken);
				}
			};
		}

		public SessionExceptionHandler<TException> Complete()
		{
			return async (session, message, exception) =>
			{
				await session.CompleteAsync(message.SystemProperties.LockToken);
			};
		}
	}
}