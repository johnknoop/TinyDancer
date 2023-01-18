using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text;
using System.Text.Json;

namespace TinyDancer.Publish
{
	public static class SerializationExtensions
	{
		public static byte[] Serialized<T>(this T message)
		{
			var settings = new JsonSerializerOptions();

			settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
			
			var json = JsonSerializer.Serialize<T>(message, settings);
			return Encoding.UTF8.GetBytes(json);
		}
	}
}
