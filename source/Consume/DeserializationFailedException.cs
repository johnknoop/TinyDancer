using System;

namespace TinyDancer.Consume
{
	public class DeserializationFailedException : Exception {
		public  DeserializationFailedException(string message): base(message)
		{
			
		}

		public DeserializationFailedException(Exception innerException):base("Deserialization failed", innerException)
		{
			
		}
	}
}
