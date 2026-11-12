using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Features.Todos.UpdateTodo;

/// <summary>
/// Normalized HTTP input contract for updating a todo (id comes from the route). DataAnnotations
/// are for OpenAPI docs; the enforced rules live in UpdateTodoCommandValidator (message bus).
/// </summary>
public record UpdateTodoRequest(
    [property: Required, StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(2000)]
    string? Description = null);
