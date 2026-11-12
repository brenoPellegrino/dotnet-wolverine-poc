using Alba;
using Npgsql;
using Shouldly;
using Xunit;

namespace TodoApp.Tests.Integration;

/// <summary>
/// Proves the connection ceiling is real: a burst of concurrent requests is served by queuing on
/// the bounded app pool (default 5, see appsettings "ConnectionPool") instead of opening one
/// Postgres connection per request.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ConnectionPoolTests(ApiFixture fixture)
{
    private const int AppMaxSize = 5;   // appsettings.json → ConnectionPool:AppMaxSize
    private const int Concurrency = 50;

    [Fact]
    public async Task concurrent_requests_never_exceed_the_app_pool_ceiling()
    {
        var peak = 0;
        using var stop = new CancellationTokenSource();

        // Sample how many server connections the request pool holds while the burst runs.
        var sampler = Task.Run(async () =>
        {
            await using var monitor = new NpgsqlConnection(fixture.DatabaseConnectionString);
            await monitor.OpenAsync();
            while (!stop.IsCancellationRequested)
            {
                await using var cmd = new NpgsqlCommand(
                    "select count(*) from pg_stat_activity where application_name = 'todo-api'", monitor);
                var current = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                peak = Math.Max(peak, current);
                await Task.Delay(5);
            }
        });

        var burst = Enumerable.Range(0, Concurrency).Select(_ => fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/todos");
            x.StatusCodeShouldBe(200);
        }));
        await Task.WhenAll(burst);

        await stop.CancelAsync();
        await sampler;

        peak.ShouldBeGreaterThan(0, "sampler never saw the app pool — is Application Name set?");
        peak.ShouldBeLessThanOrEqualTo(AppMaxSize);
    }
}
