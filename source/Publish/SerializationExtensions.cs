using System.Text;
using Newtonsoft.Json;
using NodaTime;
using NodaTime.Serialization.JsonNet;

namespace TinyDancer.Publish
{
	public static class SerializationExtensions
	{
		public static byte[] Serialized<T>(this T message)
		{
			var settings = new JsonSerializerSettings();

			settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
			
			var json = JsonConvert.SerializeObject(message, settings);
			return Encoding.UTF8.GetBytes(json);
		}
	}
}