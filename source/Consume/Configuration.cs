using System;

namespace TinyDancer.Consume
{
	public class Configuration
	{
		public int? MaxConcurrentMessages { get; set; }
		public TimeSpan? MaxAutoRenewDuration { get; set; }
	}
}