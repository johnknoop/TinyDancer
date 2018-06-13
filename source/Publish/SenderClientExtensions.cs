using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TinyDancer.Publish
{
	public static class SenderClientExtensions
	{
		public static async Task PublishAsync<TMessage>(this ISenderClient client, TMessage payload, string sessionId = null, string deduplicationIdentifier = null, bool compress = false, Func<TMessage, string> correlationId = null)
		{
			var serialized = compress
				? MessagePackSerializer.Serialize(payload, MessagePack.Resolvers.ContractlessStandardResolver.Instance)
				: payload.Serialized();

			var message = new Message(serialized)
			{
				SessionId = sessionId,
				CorrelationId = correlationId?.Invoke(payload),
				UserProperties = { ["MessageType"] = payload.GetType().Name }
			};

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

		public static async Task PublishAllAsync<TMessage>(this ISenderClient client, IList<TMessage> payloads, string sessionId = null, string deduplicationIdentifier = null, bool compress = false, Func<TMessage, string> correlationId = null)
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
					UserProperties = { ["MessageType"] = payload.GetType().Name }
				};

				if (compress)
				{
					message.UserProperties["compressed"] = true;
				}

				return message;
			}).ToList();

			if (deduplicationIdentifier != null)
			{
				messages.ForEach(x => x.MessageId = deduplicationIdentifier);
			}

			await client.SendAsync(messages);
		}
	}
}