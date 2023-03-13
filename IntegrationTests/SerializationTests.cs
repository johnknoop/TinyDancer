using FluentAssertions;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using TinyDancer.Consume;
using TinyDancer.Publish;
using Xunit;

namespace IntegrationTests
{
	public interface ITenantMessage
	{
		string CorrelationId { get; }
	}

	public record GoodsShipped_Item(string ItemId, int QuantityShipped);
	public record GoodsShipped(Instant ShippedTime, string WarehouseId, IList<GoodsShipped_Item> Items, string CorrelationId, string OrderNumber) : ITenantMessage;

	public class SerializationTests
	{
		[Fact]
		public void ShouldBeAbleToSerializeAndDeserializeRecordsWithPrimaryConstructors()
		{
			var messages = new List<ITenantMessage>
			{
				new GoodsShipped(SystemClock.Instance.GetCurrentInstant(), "MyWarehouse", new List<GoodsShipped_Item> { new ("item1", 2) }, "corrid", "35")
			};

			var json = messages[0].Serialized();

			json.Should().Match("*WarehouseId*");
		}
	}
}
