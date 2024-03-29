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
		public static object Deserialize(this BinaryData data, Type type)
		{
			try
			{
				var settings = new JsonSerializerOptions();
				settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

				return JsonSerializer.Deserialize(data.ToStream(), type, settings) ?? throw new DeserializationFailedException("Deserialization returned null");
			}
			catch (Exception ex)
			{
				throw new DeserializationFailedException(ex);
			}
		}
	}
}
