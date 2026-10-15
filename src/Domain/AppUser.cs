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

    // Whether the user held the Handler role when they last signed in. Roles live in the token, not the
    // database, so this snapshot is what lets us offer the list of people a claim can be assigned to.
    public bool IsHandler { get; set; }

    public DateTime LastSeenAt { get; set; }
}
