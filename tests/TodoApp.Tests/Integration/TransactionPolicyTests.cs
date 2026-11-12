using Alba;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TodoApp.Api.Features.Todos.Contracts;
using TodoApp.Api.Features.Todos.CreateTodo;
using TodoApp.Application.Todos.CreateTodo;
using TodoApp.Application.Todos.GetTodo;
using TodoApp.Application.Todos.ListTodos;
using Wolverine;
using Wolverine.Http;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TodoApp.Tests.Integration;

/// <summary>
/// Guards against the connection-pool starvation regression: with AutoApplyTransactions, any
/// handler that takes the DbContext gets a transaction (and holds a connection) — even reads.
/// Queries must use IQueryDbContextFactory so they stay non-transactional; commands must stay
/// transactional so SaveChanges + outbox still happen.
/// </summary>
[Collection(nameof(ApiCollection))]
public class TransactionPolicyTests(ApiFixture fixture)
{
    private readonly IAlbaHost _host = fixture.Host;

    [Fact]
    public async Task queries_are_not_transactional_but_commands_are()
    {
        // Exercise every chain first: IsTransactional is decided during code generation.
        var created = (await _host.Scenario(x =>
        {
            x.Post.Json(new CreateTodoRequest("tx policy", null)).ToUrl("/api/todos");
            x.StatusCodeShouldBe(201);
        })).ReadAsJson<CreationResponse<TodoResponse>>()!.Value;

        await _host.Scenario(x => x.Get.Url($"/api/todos/{created.Id}"));
        await _host.Scenario(x => x.Get.Url("/api/todos"));

        var handlers = _host.Services.GetRequiredService<HandlerGraph>();

        handlers.ChainFor(typeof(GetTodoQuery))!.IsTransactional.ShouldBeFalse();
        handlers.ChainFor(typeof(ListTodosQuery))!.IsTransactional.ShouldBeFalse();
        handlers.ChainFor(typeof(CreateTodoCommand))!.IsTransactional.ShouldBeTrue();
    }
}
