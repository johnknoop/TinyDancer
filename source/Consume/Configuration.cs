using System;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public class Configuration
	{
		public int? MaxConcurrentMessages { get; set; }
		public TimeSpan? MaxAutoRenewDuration { get; set; }
	}
}