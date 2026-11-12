# TodoApp — POC .NET 10 + CQRS + Wolverine

API de Todo List demonstrando **Wolverine** (mensageria/CQRS) + **Wolverine.Http**
(endpoints HTTP) + **FluentValidation** (validação no command, via message bus) + **EF Core/Postgres**
com a **integração de durabilidade do Wolverine** (outbox transacional), numa arquitetura
em camadas `Domain → Application → Infrastructure → Api`.

Os endpoints seguem um estilo parecido com o do **FastEndpoints**: uma pasta por endpoint
com `Request` / `Response` na API; a orquestração e a **validação** vivem na camada
Application (handlers de caso de uso + validators de command), despachados via `IMessageBus`.

## Stack

| Item                     | Versão / escolha              |
|--------------------------|-------------------------------|
| .NET                     | 10.0                          |
| WolverineFx(.Http)       | 6.46.0                        |
| FluentValidation         | 12.0.0                        |
| Persistência             | EF Core 10 + Npgsql (Postgres)|
| Durabilidade (outbox)    | WolverineFx.Postgresql + EF   |
| OpenAPI UI               | Scalar (`/scalar`)            |
| Testes                   | xUnit + Alba + Testcontainers |

## Arquitetura e grafo de dependências

```
Domain          →  (nada)
Application     →  Domain, Infrastructure, WolverineFx   (handlers usam DbContext + IMessageContext)
Infrastructure  →  Domain                                (TodoDbContext / Npgsql / migrations)
Api             →  Application, Infrastructure            (composition root; endpoints finos)
```

> Ao adotar a integração de durabilidade do Wolverine, os handlers recebem o
> `TodoDbContext` **concreto** — por isso a camada Application conhece o EF (estilo
> *Vertical Slice*, não *Onion* estrito). Não há repositório: o `DbContext` já é o
> Repository + Unit of Work, e o Wolverine faz o `SaveChanges` + flush do outbox.

## Estrutura

```
TodoApp.sln
docker-compose.yml               # Postgres 17
src/
  TodoApp.Domain/                # Entidade + regras (sem dependências)
    Todos/
      TodoItem.cs                #   aggregate root com comportamento (Create/Complete/...)
      TodoStatus.cs
  TodoApp.Application/           # Casos de uso (CQRS) — handlers do Wolverine
    Todos/
      Contracts/TodoDto.cs       #   resultado do caso de uso + mapeamento do agregado
      CreateTodo/                #   Command + Handler
      UpdateTodo/
      GetTodo/ ListTodos/        #   Query + Handler
      CompleteTodo/              #   Command + Handler + evento TodoCompleted + handler do evento
      DeleteTodo/
  TodoApp.Infrastructure/        # Persistência (EF Core / Postgres)
    Persistence/
      TodoDbContext.cs
      TodoDbContextFactory.cs    #   design-time factory p/ `dotnet ef`
      Migrations/
    DependencyInjection.cs
  TodoApp.Api/                   # HTTP (Wolverine.Http) + composição
    Program.cs                   #   wiring Wolverine / EF durability / FluentValidation / OpenAPI
    Auth/AuthenticationSetup.cs  #   scaffolding OAuth (JWT Bearer) — DESLIGADO por padrão
    Features/Todos/
      Contracts/TodoResponse.cs  #   contrato HTTP de SAÍDA (normalizado na API)
      CreateTodo/                #   Endpoint + Request (COM body) — validação fica no command
      UpdateTodo/                #   Endpoint + Request (COM body)
      GetTodo/ ListTodos/ CompleteTodo/ DeleteTodo/   # só Endpoint (SEM body)
    ValidationExceptionHandler.cs #  ValidationException (do bus) -> 400 ProblemDetails
tests/
  TodoApp.Tests/                 # xUnit: domínio (puro) + integração (Alba + Testcontainers)
```

## Como rodar

```bash
docker compose up -d                      # sobe o Postgres (localhost:5432)
dotnet run --project src/TodoApp.Api      # migrations + tabelas do Wolverine no startup
```

- **Scalar UI** (explorar/testar): http://localhost:5153/scalar (a raiz `/` redireciona pra lá)
- **OpenAPI JSON:** http://localhost:5153/openapi/v1.json
- Requests prontos em `src/TodoApp.Api/TodoApp.Api.http`

Parar: `pkill -f TodoApp.Api` e `docker compose down` (use `-v` pra apagar os dados).

## Testes

```bash
dotnet test
```

- **Domínio** (puro, `FakeTimeProvider`): comportamento do `TodoItem`.
- **Integração** (Alba + Testcontainers): sobe a API real contra um Postgres descartável
  e exercita endpoint → validação → handler → EF → outbox (CRUD, 400 e 404).
- Um *module initializer* fixa `DOCKER_API_VERSION=1.43` para compatibilidade com o
  Docker Engine local (Testcontainers via Docker.DotNet).

## Endpoints

| Método | Rota                        | Descrição                       | Respostas       |
|--------|-----------------------------|---------------------------------|-----------------|
| POST   | `/api/todos`                | Cria um todo                    | 201 / 400       |
| GET    | `/api/todos?status=`        | Lista (filtro opcional)         | 200             |
| GET    | `/api/todos/{id}`           | Busca por id                    | 200 / 404       |
| PUT    | `/api/todos/{id}`           | Atualiza título/descrição       | 200 / 400 / 404 |
| POST   | `/api/todos/{id}/complete`  | Marca como concluído (idempot.) | 200 / 404       |
| DELETE | `/api/todos/{id}`           | Remove                          | 204 / 404       |

## Fluxo de uma request (ex.: criar)

```
POST /api/todos  (body = CreateTodoRequest)
   ▼  Endpoint mapeia Request → CreateTodoCommand e chama bus.InvokeAsync
   │  Wolverine: UseFluentValidation → CreateTodoCommandValidator
   │     inválido → ValidationException → ValidationExceptionHandler → 400 ProblemDetails
   ▼  CreateTodoHandler (Application): db.Todos.Add(...)  — NÃO chama SaveChanges
   ▼  Wolverine: SaveChangesAsync + flush do outbox (AutoApplyTransactions)
   ▼  Endpoint mapeia TodoDto → TodoResponse  → 201
```

## Padrões demonstrados

- **Endpoint estilo FastEndpoints**: cada endpoint tem sua pasta em `Features/Todos/<Feature>/`.
  Com body ⇒ `Endpoint` + `Request`; sem body ⇒ só o `Endpoint`. Entrada e saída são
  normalizadas na API (`Request`/`Response`), distintas do `TodoDto` do caso de uso.
- **Validação de fonte única (no command)**: os validators vivem na Application
  (`CreateTodoCommandValidator`, ...) e rodam como middleware do Wolverine (`opts.UseFluentValidation()`)
  em **todo** dispatch pelo `IMessageBus` — HTTP, fila, cron, testes. No HTTP, o
  `ValidationExceptionHandler` traduz o `ValidationException` em **400 ProblemDetails**
  (`application/problem+json` + `errors` por campo), e os endpoints de escrita declaram esse
  400 no OpenAPI via `[ProducesResponseType<HttpValidationProblemDetails>(400)]`.
- **CQRS via IMessageBus**: endpoints finos mapeiam `Request → Command/Query` e despacham
  pro Wolverine; a orquestração vive nos handlers da Application.
- **Durabilidade EF Core (outbox transacional)**: `AddDbContextWithWolverineIntegration`
  + `UseEntityFrameworkCoreTransactions` + `Policies.AutoApplyTransactions`
  + `PersistMessagesWithPostgresql` + `UseDurableLocalQueues`. Os handlers **não** chamam
  `SaveChanges`; o Wolverine salva e despacha as mensagens na **mesma transação**.
  `CompleteTodoHandler` publica `TodoCompleted`, persistido junto da mudança de status e
  despachado só após o commit (sem janela "salvou mas perdeu o evento").
- **Queries sem transação (`IQueryDbContextFactory`)**: o `AutoApplyTransactions` envolve em
  transação **todo** handler que recebe o `TodoDbContext` — inclusive leituras —, segurando uma
  conexão do pool até o fim do handler. Somado a outro acesso ao banco na mesma request (ex.: um
  middleware HTTP que carrega os escopos/permissões do usuário), cada request passa a precisar de
  2 conexões e, sob carga, o pool esgota e as requests ficam esperando umas pelas outras
  (*pool starvation*). Por isso: **commands** recebem `TodoDbContext` (transação + outbox);
  **queries** (e middlewares de leitura) recebem `IQueryDbContextFactory`, criam um contexto
  `NoTracking` com `await using` e devolvem a conexão ao pool logo após a consulta.
  `TransactionPolicyTests` garante que as queries continuem não-transacionais.

## Autenticação OAuth (scaffolding pronto, desligado)

`Auth/AuthenticationSetup.cs` traz **JWT Bearer (OAuth2/OIDC)** desativado. Para ligar
contra um IdP real (Keycloak, Auth0, Entra ID, ...):

```json
"Authentication": {
  "Enabled": true,
  "Authority": "https://seu-idp/realms/todo",
  "Audience": "todo-api"
}
```

Com `Enabled: true`, `UseAuthentication/UseAuthorization` são ativados e
`opts.RequireAuthorizeOnAll()` exige usuário autenticado em todas as rotas.

## Migrations (EF Core)

```bash
dotnet ef migrations add <Nome> \
  --project src/TodoApp.Infrastructure \
  --startup-project src/TodoApp.Api \
  --output-dir Persistence/Migrations
```

Em dev, o `Program.cs` aplica as migrations no startup (`Database.MigrateAsync()`) e o
`AddResourceSetupOnStartup()` cria as tabelas de mensageria do Wolverine (schema `wolverine`).

## Geração de código do Wolverine

Esta POC usa **compilação em runtime** (`WolverineFx.RuntimeCompilation`), o caminho mais
simples para dev. Para produção, prefira **codegen estático**:

```bash
dotnet run --project src/TodoApp.Api -- codegen write
```

e defina `opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static`. Comandos de diagnóstico
ficam disponíveis via `-- ?` (ex.: `-- describe`), graças ao `RunJasperFxCommands`.

## Notas

- A connection string de dev (`Username=todo;Password=todo`) está em `appsettings.json`
  por conveniência da POC. Para um banco real, use **user-secrets** / variáveis de ambiente.
