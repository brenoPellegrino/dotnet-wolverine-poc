namespace TodoApp.Infrastructure.Persistence;

/// <summary>
/// Read-side DbContext factory for query handlers and request middleware (e.g. permission lookups).
///
/// Why it exists: with <c>Policies.AutoApplyTransactions()</c>, Wolverine wraps EVERY handler that
/// takes a <see cref="TodoDbContext"/> in EF transaction middleware (Eager mode = BEGIN on entry,
/// connection held until the handler finishes) — even pure reads. A request that also touches the
/// database elsewhere (an HTTP middleware loading the user's scopes, for example) then holds two
/// pooled connections at once; under load the pool is exhausted and requests wait on each other
/// forever (pool starvation / "deadlock").
///
/// Handlers that depend on this factory instead of the DbContext are NOT transactional: Wolverine's
/// policy only sees a non-DbContext dependency. The context created here opens its connection
/// lazily per query and returns it to the pool as soon as the query completes, and
/// disposing it (<c>await using</c>) releases everything immediately.
///
/// Rule of thumb: commands take <see cref="TodoDbContext"/> (transaction + outbox);
/// queries take <see cref="IQueryDbContextFactory"/>.
/// </summary>
public interface IQueryDbContextFactory
{
    /// <summary>
    /// Creates a short-lived, no-tracking context. The caller owns it and must dispose it.
    /// Never call SaveChanges on it — writes belong in a command handler.
    /// </summary>
    TodoDbContext CreateDbContext();
}
