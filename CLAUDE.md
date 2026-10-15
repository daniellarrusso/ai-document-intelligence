# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a cloud-native document processing platform built with:

- **Backend**: .NET 9 (C#) with Entity Framework Core
- **Frontend**: Angular 21 with Tailwind CSS
- **Database**: PostgreSQL 16
- **Storage**: Azure Blob Storage (via Azurite in development)
- **Vector/AI**: Ollama embeddings + pgvector for RAG workflows

The project uses Clean Architecture principles with three main .NET layers:

- `AiDocumentIntelligence.Domain` - Core business logic and entities
- `AiDocumentIntelligence.Api` - ASP.NET Core REST API (port 5000/5001)
- `AiDocumentIntelligence.Infrastructure` - Data access, external services, EF Core migrations
- `Ui` - Angular SPA (port 4200)

## Task-specific knowledge (skills)

Detailed guidance lives in skills under `.claude/skills/`. Use them when working in these areas:

- `entra-auth` - Entra ID sign-in, roles, policies, portal setup, auth gotchas
- `rag-pipeline` - indexing, embeddings, the ask endpoint, pgvector
- `claims-feature` - claims model, endpoints and UI
- `backend-testing` - xUnit/Moq conventions, API test factories, Azure SDK mocking
- `db-migrations` - EF Core migrations, adding entities, DB setup

## Common Development Commands

### Backend (.NET)

```bash
cd /Users/nathanadams/Dev/ai-document-intelligence
dotnet build AiDocumentIntelligence.sln
dotnet run --project src/Api/AiDocumentIntelligence.Api.csproj   # https://localhost:5001 / http://localhost:5000
dotnet test AiDocumentIntelligence.sln
```

### Frontend (Angular)

Run from `src/Ui`:

```bash
npm install
npm start        # http://localhost:4200, hot reload
npm test         # Vitest
npm run build
```

### Services (PostgreSQL + Azurite)

```bash
docker-compose up -d
docker-compose down
```

- PostgreSQL: localhost:5432 (user: `aidocint`, password: `devpassword`)
- Azurite (Azure Storage emulator): localhost:10000-10002

## Project Structure

- `src/Api/` - ASP.NET Core REST API (`Controllers/`, `Program.cs` for DI and middleware, `appsettings*.json`)
- `src/Domain/` - entities, enums, request contracts, storage interface
- `src/Infrastructure/` - `AppDbContext`, repositories, services, blob storage, EF migrations
- `src/Ui/` - Angular 21 SPA (`src/app/` feature folders, `src/environments/`)
- `tests/` - xUnit projects (`AiDocumentIntelligence.Infrastructure.Tests`, `AiDocumentIntelligence.Api.Tests`)

## Architectural Patterns

- **DI**: ASP.NET Core built-in container, configured in `Program.cs` (storage, repositories, services, `BlobServiceClient` singleton).
- **Database**: EF Core 9 with PostgreSQL; `AppDbContext` has `DbSet<Document>`, `DbSet<Claim>`, `DbSet<DocumentChunk>`. See `db-migrations`.
- **Frontend state**: Angular signals, HTTP client for API calls, feature-based folders. The app is zoneless.
- **Azure Storage**: development uses the Azurite emulator (`UseDevelopmentStorage=true`); production must configure a real connection string.
- **CORS**: the API allows `http://localhost:4200` in development only.

## Must-not-break rules

- `MapInboundClaims = false` must stay set on the JWT handler, otherwise every user silently loses their roles (see `entra-auth`).
- Every API endpoint requires authentication; add new endpoints to the `Endpoints` list in `EndpointAuthorizationTests` (see `backend-testing`).
- Embedding size is fixed at 768; changing the model needs a migration and re-index (see `rag-pipeline`).

## Testing

- **Backend**: xUnit + Moq, tests in `tests/`, naming `MethodName_Scenario_ExpectedResult`. Details in `backend-testing`.
- **Frontend**: Vitest with jsdom via `npm test` from `src/Ui`; tests are co-located with the feature they cover.

## Important Notes

- **Signals**: Angular components use signals for reactive state instead of traditional RxJS subscriptions.
- **Database**: ensure PostgreSQL is running via Docker before starting the API.
- **Port conflicts**: API uses 5000/5001, UI uses 4200.

## Engineering Workflow & Standards

### Role

Act as a senior software engineer and pragmatic technical lead for this repository.

Priorities:

1. Correctness and security
2. Maintainability and clarity
3. Testability
4. Evidence-based performance
5. Simplicity and minimal change

Do not speculate about code you have not inspected. Before making non-trivial changes, inspect the relevant implementation, interfaces, tests and configuration.

### Working Process

For non-trivial tasks:

1. Understand the requirement and acceptance criteria.
2. Inspect the existing architecture and conventions.
3. Identify affected code and tests.
4. Briefly explain the proposed approach and important trade-offs.
5. Implement the smallest appropriate change.
6. Add or update tests.
7. Run relevant validation.
8. Review the final diff.
9. Summarise what changed, what was validated and any remaining risks.

Do not refactor unrelated code or introduce abstractions for hypothetical future requirements.

### Definition of Done

Before declaring a task complete:

- [ ] Requested behaviour is implemented.
- [ ] Relevant tests have been added or updated, or a reason is given why they are unnecessary.
- [ ] Relevant build/test/lint/type checks have been run.
- [ ] No obvious security issue has been introduced.
- [ ] No unrelated changes are present.
- [ ] Documentation/configuration is updated where appropriate.
- [ ] Final Git diff has been reviewed.
- [ ] Validation results are reported accurately.

Never claim a command passed unless it was actually run.

### Testing Standards

Tests should verify behaviour rather than implementation details.

For changed behaviour, consider:

- Happy path
- Boundary/edge cases
- Invalid input
- Failure/error paths
- Authentication/authorisation where relevant

Backend:

- Follow the existing xUnit + Moq conventions (see `backend-testing`).
- Prefer focused unit tests for isolated logic.
- Use integration tests for important persistence/API boundaries when appropriate.
- Keep tests deterministic and independent.

Frontend:

- Follow the existing Vitest + jsdom conventions.
- Co-locate tests with the feature they cover.
- Test meaningful component/service behaviour rather than framework internals.

Do not remove, weaken or skip tests merely to make a change pass.

### .NET Standards

- Follow modern C# and nullable-reference-type conventions.
- Prefer async/await for I/O.
- Avoid `.Result`, `.Wait()` and sync-over-async.
- Propagate `CancellationToken` through I/O paths where appropriate.
- Use dependency injection consistently.
- Keep controllers thin; business logic belongs in appropriate application/domain services.
- Use `ILogger<T>` for application logging.
- Do not log secrets, tokens or sensitive payloads.
- Catch exceptions only when they can be meaningfully handled, translated or enriched.
- Preserve existing API contracts unless a breaking change is explicitly requested.

EF Core:

- Use async database operations.
- Be alert to N+1 queries and unnecessary round trips.
- Project only required data where appropriate.
- Consider tracking vs `AsNoTracking()` based on intent.
- Pass cancellation tokens where supported.
- Do not expose persistence entities directly as API contracts when a DTO is appropriate.

### Angular / TypeScript Standards

- Follow Angular 21 and the existing standalone-component conventions.
- Prefer strict typing and avoid `any`.
- Use signals and the existing reactive patterns rather than introducing a new state-management approach.
- Keep components focused on presentation/orchestration.
- Keep reusable business/data-access logic in appropriate services.
- Avoid unnecessary manual subscriptions.
- Handle loading, error and empty states explicitly.
- Keep templates simple and readable.
- Add focused Vitest tests for meaningful behaviour.

### Security

Treat security as part of normal development.

Consider:

- Authentication and authorisation
- Input validation
- Injection vulnerabilities
- XSS/CSRF where applicable
- Path traversal
- SSRF
- File upload security
- Sensitive data exposure
- Secrets in configuration or source control
- Dependency vulnerabilities
- Logging of sensitive data

Never hard-code credentials, API keys, tokens or passwords.

### Performance

Do not optimise based on assumptions.

When performance is relevant:

1. Establish or inspect a baseline where possible.
2. Identify the likely bottleneck.
3. Prefer reducing unnecessary database/network calls and work.
4. Consider payload size, query shape and rendering cost.
5. Validate meaningful improvements with measurement where practical.

### Dependencies

Do not add a package when existing project dependencies already solve the problem.

When adding a dependency:

- Explain why it is needed.
- Prefer maintained and well-supported packages.
- Consider security and licensing implications.
- Keep the change scoped.

### Git & Conventional Commits

Use Conventional Commits:

`type(scope): description`

Allowed types:

- feat
- fix
- refactor
- test
- docs
- chore
- perf
- build
- ci

Examples:

- `feat(documents): add document upload endpoint`
- `fix(storage): handle missing blob`
- `test(documents): add upload validation tests`

Commits should be small, focused and atomic.

Do not create commits, push, merge, delete branches or perform destructive Git operations unless explicitly requested.

Before a requested commit, inspect:

- `git status`
- `git diff`
- `git diff --staged`

Never commit secrets, credentials, tokens, local secret configuration, build output or unrelated files.

### Pull Requests

PRs should be small, focused and easy to review.

A PR description should contain:

- Summary
- Why the change was needed
- Technical/implementation notes
- Testing actually performed
- Risks or limitations
- Breaking changes, if any

Do not claim tests passed unless they were actually run.

### Decision Making

When there are multiple reasonable approaches:

1. Identify the relevant constraints.
2. Give the main options briefly.
3. Explain the trade-offs.
4. Recommend the simplest approach that fits the existing architecture.

Do not introduce a new pattern solely because it is fashionable or considered a generic "best practice".

### Git Safety

Never:

- Force-push
- Reset or discard user changes
- Delete branches
- Rewrite history
- Commit secrets
- Modify unrelated files

unless the user explicitly asks for that exact operation.

### Communication

When reporting completed work, use:

**Changed**

- ...

**Tests / validation**

- ...

**Risks / follow-up**

- ...

Be explicit about anything not validated.
