using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinyDancer.DependencyResolution;

namespace TinyDancer.Publish
{
	public static class SenderClientExtensions
	{
        public static async Task PublishAsync<TMessage>(this ServiceBusSender sender, TMessage payload, string? sessionId = null, string? deduplicationIdentifier = null, string? correlationId = null, IDictionary<string, object>? userProperties = null)
		{
			var json = payload.Serialized();

			if (ServiceProviderAccessor.ServiceProvider.Value?.TryGetService<DiagnosticsWriter>(out var logger) ?? false)
			{
				await logger.Writer.WriteLineAsync($"Serialized message of type {typeof(TMessage)}: {json}");
			}

			var serialized = Encoding.UTF8.GetBytes(json);

			var message = new ServiceBusMessage(serialized)
			{
				SessionId = sessionId,
				CorrelationId = correlationId,
				ApplicationProperties =
				{
					["MessageType"] = payload.GetType().FullName,
					["Culture"] = CultureInfo.CurrentCulture.Name
				}
			};

			if (userProperties != null)
			{
				foreach (var userPropertiesKey in userProperties.Keys)
				{
					message.ApplicationProperties[userPropertiesKey] = userProperties[userPropertiesKey];
				}
			}

			if (deduplicationIdentifier != null)
			{
				message.MessageId = deduplicationIdentifier;
			}

			await sender.SendMessageAsync(message);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TMessage"></typeparam>
		/// <param name="deduplicationIdentifier">Sets the MessageId property of each message, to allow for deduplication</param>
		/// <param name="correlationId">Sets the CorrelationId property of each message</param>
		/// <returns></returns>
		public static async Task PublishAllAsync<TMessage>(this ServiceBusSender sender, IList<TMessage> payloads, string? sessionId = null, Func<TMessage, string>? deduplicationIdentifier = null, Func<TMessage, string>? correlationId = null, IDictionary<string, object>? userProperties = null)
		{
			if (payloads.Count == 0)
			{
				throw new ArgumentException("No messages supplied for publishing");
			}

			var messages = payloads.Select(payload =>
			{
				var serialized = payload.Serialized();

				var message = new ServiceBusMessage(serialized)
				{
					SessionId = sessionId,
					CorrelationId = correlationId?.Invoke(payload),
					ApplicationProperties = { ["MessageType"] = payload.GetType().FullName }
				};

				if (userProperties != null)
				{
					foreach (var userPropertiesKey in userProperties.Keys)
					{
						message.ApplicationProperties[userPropertiesKey] = userProperties[userPropertiesKey];
					}
				}

				if (deduplicationIdentifier != null)
				{
					message.MessageId = deduplicationIdentifier(payload);
				}

				return message;
			}).ToList();

			await sender.SendMessagesAsync(messages);
		}
	}
}
