---
name: backend-testing
description: Conventions and gotchas for the .NET xUnit/Moq tests, including API tests with WebApplicationFactory and mocking Azure SDK clients. Use when writing or changing backend tests, adding an API endpoint, or mocking BlobClient/BlobContainerClient/BlobServiceClient.
---

# Backend testing (.NET)

- **Framework**: xUnit with Moq.
- **Location**: `tests/AiDocumentIntelligence.Infrastructure.Tests/` (mirrors the project under test, e.g. `BlobDocumentStorageTests.cs` tests `src/Infrastructure/BlobDocumentStorage.cs`).
- **Naming**: `MethodName_Scenario_ExpectedResult` (e.g. `UploadAsync_ContentExceedsMaxSize_ThrowsArgumentException`).
- **Structure**: a private `CreateSut()` helper builds the class under test from mocked dependencies set up in the test class constructor (e.g. `Mock<BlobServiceClient>` wired to return a `Mock<BlobContainerClient>`).
- **Run a single test file**: `dotnet test tests/AiDocumentIntelligence.Infrastructure.Tests --filter "FullyQualifiedName~BlobDocumentStorageTests"`

## API tests
`tests/AiDocumentIntelligence.Api.Tests/` boot the real API with `WebApplicationFactory` and mocked data/AI services (no Postgres, Azurite or Ollama).

- `EndpointAuthorizationTests` runs every endpoint against anonymous/role-less/wrong-scope/Reader/Handler/Admin callers using a header-driven test auth handler (`ApiFactory`). **Add new endpoints to its `Endpoints` list.**
- `JwtAuthenticationTests` (`JwtApiFactory`) sends real signed JWTs through the real bearer pipeline and covers role/scope mapping and token rejection.

## Mocking Azure SDK clients
- `BlobClient`/`BlobContainerClient`/`BlobServiceClient` methods are virtual and mockable, but many Azure SDK response types (e.g. `BlobDownloadStreamingResult`) have **internal constructors and internal property setters** and cannot be instantiated from test code.
- Prefer client methods that return plain types (e.g. `OpenReadAsync` returning `Task<Stream>` rather than `DownloadStreamingAsync` returning `Task<Response<BlobDownloadStreamingResult>>`).
- For methods returning `Task<Response<T>>` with a simple `T` (e.g. `bool` from `ExistsAsync`), mock with `Response.FromValue(value, Mock.Of<Response>())`.

## What to cover
Input validation (null/empty/invalid args), the happy path, and the wrapped-exception path (external client throws -> verify it's caught and re-thrown as the appropriate exception type, e.g. `InvalidOperationException` with the original exception as `InnerException`).
