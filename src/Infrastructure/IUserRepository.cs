using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public interface IUserRepository
{
    // Creates the user or refreshes their name, email and last-seen time.
    Task UpsertAsync(AppUser user, CancellationToken cancellationToken = default);
}
