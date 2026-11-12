using Alba;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using TodoApp.Api.Features.Todos.Contracts;
using TodoApp.Api.Features.Todos.CreateTodo;
using TodoApp.Api.Features.Todos.UpdateTodo;
using Wolverine.Http;
using Xunit;

namespace TodoApp.Tests.Integration;

/// <summary>
/// Boots the API with an EF Core connection pool of exactly ONE connection. If any request
/// path needs a second connection while it still holds the first (a transactional endpoint or
/// middleware that calls a transactional handler, a DB-backed permission middleware holding a
/// connection across the request, ...), the second checkout cannot be served and Npgsql fails
/// fast with "The connection pool has been exhausted" instead of hanging.
///
/// That is the exact shape of the production pool-starvation deadlock, made deterministic:
/// with N pooled connections and N concurrent requests each holding one and waiting for another,
/// nobody progresses. Pool size 1 + one request reproduces it without load or timing.
///
/// Wolverine's message store uses its own bounded pool (see DatabasePools), so its background
/// agents do not compete for this connection.
/// </summary>
public class SingleConnectionApiFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        // Only the request-path pool is squeezed to 1; Wolverine keeps its own pool.
        builder.UseSetting("ConnectionPool:AppMaxSize", "1");
        // Seconds to wait for a free pooled connection. Short, so a regression fails fast.
        builder.UseSetting("ConnectionPool:TimeoutSeconds", "3");
    }
}

[CollectionDefinition(nameof(SingleConnectionCollection))]
public class SingleConnectionCollection : ICollectionFixture<SingleConnectionApiFixture>;

[Collection(nameof(SingleConnectionCollection))]
public class SingleConnectionTests(SingleConnectionApiFixture fixture)
{
    private readonly IAlbaHost _host = fixture.Host;

    [Fact]
    public async Task every_endpoint_works_with_a_single_pooled_connection()
    {
        var created = (await _host.Scenario(x =>
        {
            x.Post.Json(new CreateTodoRequest("one connection", null)).ToUrl("/api/todos");
            x.StatusCodeShouldBe(201);
        })).ReadAsJson<CreationResponse<TodoResponse>>()!.Value;

        await _host.Scenario(x =>
        {
            x.Get.Url($"/api/todos/{created.Id}");
            x.StatusCodeShouldBe(200);
        });

        await _host.Scenario(x =>
        {
            x.Get.Url("/api/todos");
            x.StatusCodeShouldBe(200);
        });

        await _host.Scenario(x =>
        {
            x.Put.Json(new UpdateTodoRequest("still one connection", null)).ToUrl($"/api/todos/{created.Id}");
            x.StatusCodeShouldBe(200);
        });

        var completed = await _host.Scenario(x =>
        {
            x.Post.Url($"/api/todos/{created.Id}/complete");
            x.StatusCodeShouldBe(200);
        });
        completed.ReadAsJson<TodoResponse>().Status.ShouldBe("Completed");

        await _host.Scenario(x =>
        {
            x.Delete.Url($"/api/todos/{created.Id}");
            x.StatusCodeShouldBe(204);
        });
    }
}
