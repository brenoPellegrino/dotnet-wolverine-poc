using Microsoft.Extensions.DependencyInjection;
using TodoApp.Infrastructure.Persistence;

namespace TodoApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services other than the write-side DbContext — that one is
    /// registered by the API composition root with Wolverine's EF Core integration
    /// (AddDbContextWithWolverineIntegration), which enables the transactional outbox.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, DatabasePools pools)
    {
        services.AddSingleton(TimeProvider.System);

        // Registered through a factory so the container disposes the pools on shutdown.
        services.AddSingleton(_ => pools);

        // Read side: non-transactional DbContext factory for queries (see IQueryDbContextFactory).
        // Same bounded pool as the command DbContext.
        services.AddSingleton<IQueryDbContextFactory>(
            new QueryDbContextFactory(QueryDbContextFactory.BuildOptions(pools.App)));

        return services;
    }
}
