using System;
using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public class UnrecognizedSessionMessageHandlingBuilder
	{
		public UnrecognizedSessionMessageHandler Deadletter(Func<Message, string> reason)
		{
			return async (session, message) =>
			{
				await session.DeadLetterAsync(message.SystemProperties.LockToken, reason(message));
			};
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public UnrecognizedSessionMessageHandler Abandon(int maxTimes = 0)
		{
			return async (session, message) =>
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

		public UnrecognizedSessionMessageHandler Complete()
		{
			return async (session, message) =>
			{
				await session.CompleteAsync(message.SystemProperties.LockToken);
			};
		}
	}
}