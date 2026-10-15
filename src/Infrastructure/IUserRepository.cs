using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public interface IUserRepository
{
    // Creates the user or refreshes their name, email and last-seen time.
    Task UpsertAsync(AppUser user, CancellationToken cancellationToken = default);

    // Users who last signed in as Handlers, by display name.
    Task<List<AppUser>> GetHandlersAsync(CancellationToken cancellationToken = default);

    // The user if they are a known Handler, otherwise null.
    Task<AppUser?> FindHandlerAsync(Guid id, CancellationToken cancellationToken = default);
}
