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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(TimeProvider.System);

        // Read side: non-transactional DbContext factory for queries (see IQueryDbContextFactory).
        services.AddSingleton<IQueryDbContextFactory>(
            new QueryDbContextFactory(QueryDbContextFactory.BuildOptions(connectionString)));

        return services;
    }
}
