using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using TinyDancer.Consume;
using TinyDancer.DependencyResolution;

namespace TinyDancer;

public record DiagnosticsWriter(TextWriter Writer);

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddTinyDancer(this IServiceCollection services, Func<IServiceProvider, TextWriter>? diagnosticsFactory = null)
	{
		services.AddScoped(x => x.GetRequiredService<MessageHolder>().Message);
		services.AddScoped<MessageHolder>();
		services.AddScoped<MessageSettler>();

		if (diagnosticsFactory != null)
		{
			services.AddSingleton(x => new DiagnosticsWriter(diagnosticsFactory(x)));
		}

		ServiceProviderAccessor.RegisterServiceCollection(services);
		return services;
	}
}
