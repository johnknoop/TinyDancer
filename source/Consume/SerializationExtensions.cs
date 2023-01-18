using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace TinyDancer.Consume
{
	public static class SerializationExtensions
	{
		public static async Task<object?> DeserializeAsync(this BinaryData data, Type type)
		{
			try
			{
				var settings = new JsonSerializerOptions();
				settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

				return await JsonSerializer.DeserializeAsync(data.ToStream(), type, settings);
			}
			catch (Exception ex)
			{
				throw new DeserializationFailedException(ex);
			}
		}
	}
}
