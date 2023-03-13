using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text;
using System.Text.Json;

namespace TinyDancer.Publish
{
	public static class SerializationExtensions
	{
		public static string Serialized<T>(this T message) where T : notnull
		{
			var settings = new JsonSerializerOptions();

			settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
			
			return JsonSerializer.Serialize((object)message, settings);
		}
	}
}
