using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Stoplight.OpenApiBundler.Abstract;
using Soenneker.Utils.HttpClientCache.Registrar;

namespace Soenneker.Stoplight.OpenApiBundler.Registrars;

/// <summary>
/// A utility library to download and bundle OpenApi specs from Stoplight
/// </summary>
public static class StoplightOpenApiBundlerRegistrar
{
    /// <summary>
    /// Adds <see cref="IStoplightOpenApiBundler"/> as a singleton service. <para/>
    /// </summary>
    /// <param name="services">Service collection that receives the registration.</param>
    /// <returns>The same service collection, so additional registrations can be chained.</returns>
    public static IServiceCollection AddStoplightOpenApiBundlerAsSingleton(this IServiceCollection services)
    {
        services.AddHttpClientCacheAsSingleton()
                .TryAddSingleton<IStoplightOpenApiBundler, StoplightOpenApiBundler>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IStoplightOpenApiBundler"/> as a scoped service. <para/>
    /// </summary>
    /// <param name="services">Service collection that receives the registration.</param>
    /// <returns>The same service collection, so additional registrations can be chained.</returns>
    public static IServiceCollection AddStoplightOpenApiBundlerAsScoped(this IServiceCollection services)
    {
        services.AddHttpClientCacheAsSingleton()
                .TryAddScoped<IStoplightOpenApiBundler, StoplightOpenApiBundler>();

        return services;
    }
}
