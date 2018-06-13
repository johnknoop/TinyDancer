using System;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public class SessionConfiguration
	{
		public int? MaxConcurrentSessions { get; set; }
		public TimeSpan? MaxAutoRenewDuration { get; set; }
	}
}