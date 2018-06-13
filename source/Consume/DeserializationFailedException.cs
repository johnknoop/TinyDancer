using System;

namespace TinyDancer.Consume
{
	public class DeserializationFailedException : Exception {
		public DeserializationFailedException(Exception innerException):base("Deserialization failed", innerException)
		{
			
		}
	}
}