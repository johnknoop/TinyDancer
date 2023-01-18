using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace IntegrationTests
{
    public class GracefulShutdownTests
    {
		[Fact]
		public void ShouldLetOngoingMessageHandlersDrainBeforeAllowingShutdown()
		{
			throw new NotImplementedException();

			// Verifiera att inflight-messages som inte kompletteras flyttas tillbaka till kön
		}
    }
}
