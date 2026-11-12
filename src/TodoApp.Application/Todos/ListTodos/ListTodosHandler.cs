using Microsoft.EntityFrameworkCore;
using TodoApp.Application.Todos.Contracts;
using TodoApp.Domain.Todos;
using TodoApp.Infrastructure.Persistence;

namespace TodoApp.Application.Todos.ListTodos;

/// <summary>
/// Query handler: depends on <see cref="IQueryDbContextFactory"/> (not the DbContext) so Wolverine's
/// AutoApplyTransactions does not open a transaction / hold a connection for a read.
/// </summary>
public static class ListTodosHandler
{
    public static async Task<IReadOnlyList<TodoDto>> Handle(ListTodosQuery query, IQueryDbContextFactory dbFactory, CancellationToken ct)
    {
        TodoStatus? status = Enum.TryParse<TodoStatus>(query.Status, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        await using var db = dbFactory.CreateDbContext();

        IQueryable<TodoItem> q = db.Todos;
        if (status is not null)
        {
            q = q.Where(x => x.Status == status);
        }

        // Postgres orders by timestamptz server-side (unlike SQLite).
        var items = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return items.Select(x => x.ToDto()).ToList();
    }
}
