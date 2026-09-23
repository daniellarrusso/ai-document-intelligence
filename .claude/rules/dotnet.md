# .NET project rules

Use the project's documented .NET version and existing conventions. Prefer async I/O, dependency injection, cancellation tokens, thin controllers, meaningful exception handling and `ILogger<T>`. Avoid sync-over-async and unnecessary abstractions. For EF Core, consider query count, N+1 behaviour, projections, tracking and cancellation.
