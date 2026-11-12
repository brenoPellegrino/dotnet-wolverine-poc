using Npgsql;

namespace TodoApp.Infrastructure.Persistence;

/// <summary>Connection-pool ceilings, bound from the "ConnectionPool" configuration section.</summary>
public sealed class ConnectionPoolOptions
{
    public const string SectionName = "ConnectionPool";

    /// <summary>Max connections for the request path (EF Core: command DbContext + query factory).</summary>
    public int AppMaxSize { get; set; } = 5;

    /// <summary>Max connections for Wolverine's message store (inbox/outbox, durability agents).</summary>
    public int WolverineMaxSize { get; set; } = 3;

    /// <summary>Seconds to wait for a free pooled connection before failing (Npgsql default is 15).</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Upper bound of connections ONE app instance can open against Postgres.</summary>
    public int TotalPerInstance => AppMaxSize + WolverineMaxSize;
}

/// <summary>
/// The two explicitly bounded Postgres pools of the app. Kept separate on purpose: Wolverine's
/// background agents (which keep at least one connection open) can never starve HTTP requests,
/// and the request path gets a predictable budget.
///
/// Budget rule: TotalPerInstance x instances must stay below Postgres max_connections
/// (default 100, 3 reserved for superusers) minus headroom for migrations/admin/monitoring.
/// Each pool tags its connections with an Application Name, visible in pg_stat_activity.
/// </summary>
public sealed class DatabasePools(NpgsqlDataSource app, NpgsqlDataSource wolverine) : IAsyncDisposable
{
    /// <summary>Request path: EF Core command DbContext and <see cref="IQueryDbContextFactory"/>.</summary>
    public NpgsqlDataSource App { get; } = app;

    /// <summary>Wolverine message store.</summary>
    public NpgsqlDataSource Wolverine { get; } = wolverine;

    public static DatabasePools Create(string connectionString, ConnectionPoolOptions pool)
        => new(
            Build(connectionString, pool.AppMaxSize, pool.TimeoutSeconds, "todo-api"),
            Build(connectionString, pool.WolverineMaxSize, pool.TimeoutSeconds, "todo-wolverine"));

    private static NpgsqlDataSource Build(string connectionString, int maxPoolSize, int timeoutSeconds, string applicationName)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        builder.ConnectionStringBuilder.Timeout = timeoutSeconds;
        builder.ConnectionStringBuilder.ApplicationName = applicationName;
        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Wolverine.DisposeAsync();
    }
}
