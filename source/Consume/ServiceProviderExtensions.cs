using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TinyDancer.Consume
{
    public static class ServiceProviderExtensions
    {
        public static bool TryGetService<T>(this IServiceProvider serviceProvider, [NotNullWhen(true)] out T? service)
		{
            var resolvedService = serviceProvider.GetService<T>();

            service = resolvedService;

            return resolvedService != null;
		}
    }
}
