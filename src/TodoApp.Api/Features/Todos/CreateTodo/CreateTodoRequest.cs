using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Features.Todos.CreateTodo;

/// <summary>
/// Normalized HTTP input contract for creating a todo. The DataAnnotations here are for
/// OpenAPI documentation (maxLength/minLength/required); the enforced rules live in
/// CreateTodoCommandValidator and run on the message bus.
/// </summary>
public record CreateTodoRequest(
    [property: Required, StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(2000)]
    string? Description = null);
