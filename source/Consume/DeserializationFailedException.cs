using System;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public class DeserializationFailedException : Exception {
		public DeserializationFailedException(Exception innerException):base("Deserialization failed", innerException)
		{
			
		}
	}
}