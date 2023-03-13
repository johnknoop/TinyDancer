using System;
using Microsoft.Extensions.DependencyInjection;

namespace TinyDancer.DependencyResolution
{
	internal static class ServiceProviderAccessor
	{
		private static IServiceCollection? _services;

		internal static Lazy<IServiceProvider?> ServiceProvider { get; private set; } =
			new Lazy<IServiceProvider?>(() => _services?.BuildServiceProvider());

		internal static void RegisterServiceCollection(IServiceCollection services)
		{
			_services = services;
		}
	}
}
