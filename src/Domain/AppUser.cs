namespace AiDocumentIntelligence.Domain;

// A person who has signed in. Id is the user's Entra object id (the "oid" claim), so ownership can be
// recorded and checked straight from the token without a lookup.
public class AppUser
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 320;

    public Guid Id { get; set; }

    public string DisplayName { get; set; } = null!;

    public string? Email { get; set; }

    public DateTime LastSeenAt { get; set; }
}
