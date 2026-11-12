using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace TodoApp.Infrastructure.Persistence;

/// <summary>
/// Singleton: the options are built once, so Wolverine's code generation can inline the
/// dependency (no service location) and every CreateDbContext call is cheap.
/// </summary>
public sealed class QueryDbContextFactory(DbContextOptions<TodoDbContext> options) : IQueryDbContextFactory
{
    public TodoDbContext CreateDbContext() => new(options);

    public static DbContextOptions<TodoDbContext> BuildOptions(DbDataSource dataSource)
        => new DbContextOptionsBuilder<TodoDbContext>()
            .UseNpgsql(dataSource)
            // Read side: no change tracking, so AsNoTracking() is the default.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
}
