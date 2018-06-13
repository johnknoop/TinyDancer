using System;
using Microsoft.Azure.ServiceBus;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public class UnrecognizedMessageHandlingBuilder
	{
		public UnrecognizedMessageHandler Deadletter(Func<Message, string> reason)
		{
			return async (client, message) =>
			{
				await client.DeadLetterAsync(message.SystemProperties.LockToken, reason(message));
			};
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="maxTimes">If defined, the message will only be abandoned <c>maxTimes</c> times, and then deadlettered</param>
		/// <returns></returns>
		public UnrecognizedMessageHandler Abandon(int maxTimes = 0)
		{
			return async (client, message) =>
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

		public UnrecognizedMessageHandler Complete()
		{
			return async (client, message) =>
			{
				await client.CompleteAsync(message.SystemProperties.LockToken);
			};
		}
	}
}