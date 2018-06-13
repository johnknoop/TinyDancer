using System;
using System.Text;
using Newtonsoft.Json;
using NodaTime;
using NodaTime.Serialization.JsonNet;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public static class SerializationExtensions
	{
		public static object Deserialize(this byte[] arr, Type type)
		{
			try
			{
				var json = Encoding.UTF8.GetString(arr);
				var settings = new JsonSerializerSettings();

				settings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
				return JsonConvert.DeserializeObject(json, type, settings);
			}
			catch (Exception ex)
			{
				throw new DeserializationFailedException(ex);
			}
		}
	}
}