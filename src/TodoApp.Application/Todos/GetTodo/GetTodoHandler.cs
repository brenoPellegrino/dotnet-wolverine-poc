using Microsoft.EntityFrameworkCore;
using TodoApp.Application.Todos.Contracts;
using TodoApp.Infrastructure.Persistence;

namespace TodoApp.Application.Todos.GetTodo;

/// <summary>
/// Query handler: depends on <see cref="IQueryDbContextFactory"/> (not the DbContext) so Wolverine's
/// AutoApplyTransactions does not open a transaction / hold a connection for a read.
/// </summary>
public static class GetTodoHandler
{
    /// <summary>Returns null when the todo does not exist (mapped to 404 by the API).</summary>
    public static async Task<TodoDto?> Handle(GetTodoQuery query, IQueryDbContextFactory dbFactory, CancellationToken ct)
    {
        await using var db = dbFactory.CreateDbContext();

        var todo = await db.Todos.FirstOrDefaultAsync(x => x.Id == query.Id, ct);

        return todo?.ToDto();
    }
}
