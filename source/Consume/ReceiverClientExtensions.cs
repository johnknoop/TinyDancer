using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace TinyDancer.Consume
{
	public delegate Task ExceptionHandler<in TException>(ServiceBusReceivedMessage message, TException exception);

	public static class ServiceBusProcessorExtensions
	{
		public static MessageHandlerBuilder ConfigureTinyDancer(this ServiceBusProcessor processor) =>
			new MessageHandlerBuilder(processor);

		public static MessageHandlerBuilder ConfigureTinyDancer(this ServiceBusSessionProcessor processor) =>
			new MessageHandlerBuilder(processor);
	}

	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddTinyDancer(this IServiceCollection services)
		{
			services.AddScoped<ServiceBusReceivedMessage>(x => x.GetRequiredService<MessageHolder>().Message);
			services.AddScoped<MessageHolder>();
			services.AddScoped<MessageSettler>();

			ServiceProviderAccessor.RegisterServiceCollection(services);
			return services;
		}
	}

	internal static class ServiceProviderAccessor
	{
		private static IServiceCollection? _services;

		internal static Lazy<IServiceProvider?> ServiceProvider { get; private set; } =
			new Lazy<IServiceProvider?>(() => _services.BuildServiceProvider());

		internal static void RegisterServiceCollection(IServiceCollection services)
		{
			_services = services;
		}
	}
}
