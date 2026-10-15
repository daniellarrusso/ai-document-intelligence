using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public class UserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    // Records that this user has signed in so they can own claims and documents.
    public Task RecordSignInAsync(CurrentUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _userRepository.UpsertAsync(new AppUser
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            LastSeenAt = DateTime.UtcNow
        }, cancellationToken);
    }
}
