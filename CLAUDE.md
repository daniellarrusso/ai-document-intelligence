# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a cloud-native document processing platform built with:

- **Backend**: .NET 9 (C#) with Entity Framework Core
- **Frontend**: Angular 21 with Tailwind CSS
- **Database**: PostgreSQL 16
- **Storage**: Azure Blob Storage (via Azurite in development)
- **Vector/AI**: Azure OpenAI integration for RAG workflows

The project uses Clean Architecture principles with three main .NET layers:

- `AiDocumentIntelligence.Domain` - Core business logic and entities
- `AiDocumentIntelligence.Api` - ASP.NET Core REST API (port 5000/5001)
- `AiDocumentIntelligence.Infrastructure` - Data access, external services, EF Core migrations
- `Ui` - Angular SPA (port 4200)

## Common Development Commands

### Backend (.NET)

**Build the solution:**

```bash
cd /Users/nathanadams/Dev/ai-document-intelligence
dotnet build AiDocumentIntelligence.sln
```

**Run the API:**

```bash
dotnet run --project src/Api/AiDocumentIntelligence.Api.csproj
```

The API will start on `https://localhost:5001` (or `http://localhost:5000` for HTTP).

**Run tests:**

```bash
dotnet test AiDocumentIntelligence.sln
```

**Entity Framework Core migrations:**

```bash
# Create a new migration (run from repo root or src/Infrastructure directory)
dotnet ef migrations add MigrationName --project src/Infrastructure --startup-project src/Api

# Apply pending migrations to the database
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

### Frontend (Angular)

**Navigate to UI directory:**

```bash
cd src/Ui
```

**Install dependencies:**

```bash
npm install
```

**Start development server:**

```bash
npm start
```

The UI will be available at `http://localhost:4200` and hot-reloads on file changes.

**Run tests:**

```bash
npm test
```

**Build for production:**

```bash
npm run build
```

### Full-Stack Development

**Start services (PostgreSQL + Azurite):**

```bash
docker-compose up -d
```

- PostgreSQL: localhost:5432 (user: `aidocint`, password: `devpassword`)
- Azurite (Azure Storage emulator): localhost:10000-10002

**Stop services:**

```bash
docker-compose down
```

## Project Structure

```
src/
├── Api/                          # ASP.NET Core REST API
│   ├── Controllers/              # API endpoints (DocumentsController, WeatherForecastController)
│   ├── Program.cs                # DI configuration, middleware setup
│   ├── appsettings.json          # Production configuration
│   └── appsettings.Development.json  # Development overrides (Azure Storage, logging)
├── Domain/                       # Core business logic and entities
│   ├── Document.cs               # Main document entity
│   ├── DocumentStatus.cs         # Document status enum
│   ├── UploadDocumentRequest.cs  # API request contract
│   └── IDcoumentStorage.cs       # Storage interface
├── Infrastructure/               # Data access and external services
│   ├── AppDbContext.cs           # EF Core DbContext
│   ├── BlobDocumentStorage.cs    # Azure Blob Storage implementation
│   ├── DocumentService.cs        # Business logic orchestration
│   ├── DocumentRepository.cs     # Data access layer
│   ├── AzureStorageOptions.cs    # Azure configuration
│   └── Migrations/               # EF Core migrations (PostgreSQL)
└── Ui/                           # Angular 21 SPA
    ├── src/
    │   ├── app/                  # Components and services
    │   │   ├── app.ts            # Root component
    │   │   ├── app.routes.ts      # Route definitions
    │   │   └── upload-document/   # Feature module for document upload
    │   ├── main.ts               # Angular bootstrap
    │   └── styles.css            # Global Tailwind styles
    ├── angular.json              # Angular build configuration
    ├── package.json              # Dependencies (Vitest for unit tests)
    └── tsconfig.json             # TypeScript configuration
```

## Architectural Patterns

### Dependency Injection

The API uses ASP.NET Core's built-in DI container (configured in `Program.cs`):

- `IDocumentStorage` - Blob storage abstraction (implemented by `BlobDocumentStorage`)
- `IDocumentRepository` - Data repository pattern for PostgreSQL
- `DocumentService` - Orchestrates storage + database operations
- `BlobServiceClient` - Singleton for Azure Blob Storage

### Database Access

- **ORM**: Entity Framework Core 9.0 with PostgreSQL
- **Migrations**: Located in `src/Infrastructure/Migrations/`
- **Connection string**: Configured in `appsettings.json` (development uses localhost:5432)
- **DbContext**: `AppDbContext` with `DbSet<Document>`

### Frontend State Management

- Angular signals for reactive state (used in components)
- HTTP client for API communication
- Feature-based folder structure (e.g., `upload-document/`)

### Azure Integration

- **Blob Storage**: Configured via `AzureStorageOptions` in `appsettings.Development.json`
- **Development mode**: Uses Azurite emulator (`UseDevelopmentStorage=true`)
- **Production mode**: Uses real Azure Storage connection string (via configuration)
- **OpenAI**: Azure OpenAI integration for document processing and RAG

### CORS Configuration

API allows requests from Angular dev server (`http://localhost:4200`) in development mode only.

## Database Setup

### Initial Setup

1. Start PostgreSQL: `docker-compose up -d postgres`
2. Apply migrations: `dotnet ef database update --project src/Infrastructure --startup-project src/Api`

### Existing Migrations

- `20260829125915_InitialCreate.cs` - Initial schema with Documents table

### Adding New Entities

1. Create entity class in `src/Domain/`
2. Add `DbSet<Entity>` to `AppDbContext`
3. Create migration: `dotnet ef migrations add EntityName`
4. Review and apply: `dotnet ef database update`

## Testing Approach

### Backend (.NET)

- **Framework**: xUnit with Moq for mocking
- **Location**: `tests/AiDocumentIntelligence.Infrastructure.Tests/` (mirrors the project under test, e.g. `BlobDocumentStorageTests.cs` tests `src/Infrastructure/BlobDocumentStorage.cs`)
- **Naming convention**: `MethodName_Scenario_ExpectedResult` (e.g. `UploadAsync_ContentExceedsMaxSize_ThrowsArgumentException`)
- **Structure**: A private `CreateSut()` helper builds the class under test from mocked dependencies set up in the test class constructor (e.g. `Mock<BlobServiceClient>` wired to return a `Mock<BlobContainerClient>`)
- **Run a single test file**: `dotnet test tests/AiDocumentIntelligence.Infrastructure.Tests --filter "FullyQualifiedName~BlobDocumentStorageTests"`
- **Mocking Azure SDK clients**: `BlobClient`/`BlobContainerClient`/`BlobServiceClient` methods are virtual and mockable, but many Azure SDK response types (e.g. `BlobDownloadStreamingResult`) have **internal constructors and internal property setters** and cannot be instantiated from test code. Prefer client methods that return plain types instead (e.g. use `OpenReadAsync` which returns `Task<Stream>` rather than `DownloadStreamingAsync` which returns `Task<Response<BlobDownloadStreamingResult>>`). For methods returning `Task<Response<T>>` with a simple `T` (e.g. `bool` from `ExistsAsync`), mock with `Response.FromValue(value, Mock.Of<Response>())`.
- Cover: input validation (null/empty/invalid args), the happy path, and the wrapped-exception path (external client throws → verify it's caught and re-thrown as the appropriate exception type, e.g. `InvalidOperationException` with the original exception as `InnerException`).

### Frontend (Angular)

- **Framework**: Vitest with jsdom, run via `npm test` from `src/Ui`
- **Structure**: Tests are co-located with the feature they cover (e.g. under `upload-document/`)

## Angular/TypeScript Specifics

- **Version**: Angular 21.2.0 with latest TypeScript 5.9
- **Build tool**: Angular CLI with modern standalone components
- **Styling**: Tailwind CSS 4.1 with PostCSS
- **Testing**: Vitest with jsdom
- **Package manager**: npm 11.6.2
- **Server**: Angular SSR (Server-Side Rendering) configured via Express

## Configuration Files

### Backend

- `AiDocumentIntelligence.sln` - Solution file
- `.csproj` files - Project definitions with NuGet package references
- `appsettings.json` - Default configuration (production database connection)
- `appsettings.Development.json` - Dev overrides (Azurite storage, logging levels)

### Frontend

- `angular.json` - Build and serve configuration
- `package.json` - npm dependencies and scripts
- `tsconfig.json` - TypeScript compiler options
- `tailwind.config.js` - Tailwind CSS configuration (if present)

## Key Dependencies

### Backend

- **AspNetCore.OpenApi** - OpenAPI (Swagger) support
- **EntityFrameworkCore** - ORM
- **Npgsql.EntityFrameworkCore.PostgreSQL** - PostgreSQL provider
- **Azure.Storage.Blobs** - Azure Blob Storage SDK

### Frontend

- **@angular/core, @angular/common, @angular/router** - Core Angular
- **@angular/forms** - Reactive and template-driven forms
- **rxjs** - Reactive programming
- **tailwindcss** - Utility-first CSS

## Debugging

### Backend Debugging

VS Code launch configuration is set up in `.vscode/launch.json` for Chrome debugging of the Angular app.

To debug the .NET API:

1. Run with debug configuration in Visual Studio or use `dotnet run` with debugger attached
2. Set breakpoints in C# code
3. API will restart and hit your breakpoints

### Frontend Debugging

1. Use Chrome DevTools (F12) when running `npm start`
2. Set breakpoints in TypeScript source files (visible in DevTools Sources)
3. Angular DevTools browser extension is helpful

## Important Notes

- **Signals**: Angular components use signals for reactive state instead of traditional RxJS subscriptions
- **Zoneless Angular**: The application is configured to work with Angular's zoneless mode for better performance
- **Azure Storage**: Development uses local Azurite emulator; production must configure real Azure Storage connection string
- **Database**: Ensure PostgreSQL is running via Docker before starting the API
- **Port conflicts**: API uses 5000/5001 (HTTPS), UI uses 4200 - ensure these are available

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

- Follow the existing xUnit + Moq conventions documented above.
- Prefer focused unit tests for isolated logic.
- Use integration tests for important persistence/API boundaries when appropriate.
- Keep tests deterministic and independent.

Frontend:

- Follow the existing Vitest + jsdom conventions documented above.
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
