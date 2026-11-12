using System.Runtime.CompilerServices;
using Alba;
using Microsoft.AspNetCore.Hosting;
using JasperFx.CommandLine;
using Testcontainers.PostgreSql;
using Xunit;

namespace TodoApp.Tests.Integration;

internal static class DockerEnv
{
    /// <summary>
    /// Docker.DotNet (used by Testcontainers) negotiates API v1.44, which a local Docker
    /// Engine 24.x (max v1.43) rejects. Pin it so `dotnet test` works without extra env setup.
    /// </summary>
    [ModuleInitializer]
    public static void Init()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_API_VERSION")))
        {
            Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.43");
        }
    }
}

/// <summary>
/// Boots the REAL API (via Alba) against a throwaway Postgres container (Testcontainers).
/// Migrations + Wolverine message storage are created on startup, so the whole stack —
/// endpoint, validation, handler, EF Core, outbox — is exercised exactly as in production.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17")
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    /// <summary>Direct (unpooled-by-the-app) access to the test database, e.g. for pg_stat_activity.</summary>
    public string DatabaseConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Let JasperFx/Wolverine just start the host instead of entering command-line mode.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:Todo", _db.GetConnectionString());
            ConfigureHost(builder);
        });
    }

    /// <summary>Hook for fixtures that need extra settings (e.g. tighter pool limits).</summary>
    protected virtual void ConfigureHost(IWebHostBuilder builder) { }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFixture>;
