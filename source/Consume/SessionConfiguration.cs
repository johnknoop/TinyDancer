using System;

namespace TinyDancer.Consume
{
	public class SessionConfiguration
	{
		public int? MaxConcurrentSessions { get; set; }
		public TimeSpan? MaxAutoRenewDuration { get; set; }
	}
}