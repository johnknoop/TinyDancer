using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TinyDancer.Publish
{
	public static class SenderClientExtensions
	{
        public static async Task PublishAsync<TMessage>(this ISenderClient client, TMessage payload, string sessionId = null, string deduplicationIdentifier = null, string correlationId = null, IDictionary<string, object> userProperties = null)
            => await PublishAsync(client, payload, sessionId, deduplicationIdentifier, correlationId: correlationId, userProperties: userProperties);

        [Obsolete("The 'compress' parameter is deprecated and will be removed in an upcoming version. Use something like https://github.com/SeanFeldman/ServiceBus.CompressionPlugin instead.")]
        public static async Task PublishAsync<TMessage>(this ISenderClient client, TMessage payload, string sessionId = null, string deduplicationIdentifier = null, bool compress = false, string correlationId = null, IDictionary<string, object> userProperties = null)
		{
			var serialized = compress
				? MessagePackSerializer.Serialize(payload, MessagePack.Resolvers.ContractlessStandardResolver.Instance)
				: payload.Serialized();

			var message = new Message(serialized)
			{
				SessionId = sessionId,
				CorrelationId = correlationId,
				UserProperties =
				{
					["MessageType"] = payload.GetType().FullName,
					["Culture"] = CultureInfo.CurrentCulture.Name
				}
			};

			if (userProperties != null)
			{
				foreach (var userPropertiesKey in userProperties.Keys)
				{
					message.UserProperties[userPropertiesKey] = userProperties[userPropertiesKey];
				}
			}

			if (compress)
			{
				message.UserProperties["compressed"] = true;
			}

			if (deduplicationIdentifier != null)
			{
				message.MessageId = deduplicationIdentifier;
			}

			await client.SendAsync(message);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TMessage"></typeparam>
		/// <param name="deduplicationIdentifier">Sets the MessageId property of each message, to allow for deduplication</param>
		/// <param name="compress">If true, will compress payload using MessagePack</param>
		/// <param name="correlationId">Sets the CorrelationId property of each message</param>
		/// <returns></returns>
		public static async Task PublishAllAsync<TMessage>(this ISenderClient client, IList<TMessage> payloads, string sessionId = null, Func<TMessage, string> deduplicationIdentifier = null, bool compress = false, Func<TMessage, string> correlationId = null, IDictionary<string, object> userProperties = null)
		{
			if (payloads.Count == 0)
			{
				throw new ArgumentException("No messages supplied for publishing");
			}

			var messages = payloads.Select(payload =>
			{
				var serialized = compress
					? MessagePackSerializer.Serialize(payload, MessagePack.Resolvers.ContractlessStandardResolver.Instance)
					: payload.Serialized();

				var message = new Message(serialized)
				{
					SessionId = sessionId,
					CorrelationId = correlationId?.Invoke(payload),
					UserProperties = { ["MessageType"] = payload.GetType().FullName }
				};

				if (userProperties != null)
				{
					foreach (var userPropertiesKey in userProperties.Keys)
					{
						message.UserProperties[userPropertiesKey] = userProperties[userPropertiesKey];
					}
				}

				if (compress)
				{
					message.UserProperties["compressed"] = true;
				}

				if (deduplicationIdentifier != null)
				{
					message.MessageId = deduplicationIdentifier(payload);
				}

				return message;
			}).ToList();

			await client.SendAsync(messages);
		}
	}
}