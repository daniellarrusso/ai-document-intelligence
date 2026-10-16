using System.Linq.Expressions;

namespace AiDocumentIntelligence.Domain;

// The single definition of who may see and act on a claim. The same rule is exposed as an expression (so
// the database does the filtering, which keeps paging and search correct) and as a plain check for a claim
// already in memory; both must stay in step, which the tests assert.
//
// The user's highest role decides: Admin > Handler > Reader.
//   Admin   - sees and acts on every claim.
//   Handler - sees and acts only on claims assigned to them.
//   Reader  - sees every claim that is not private; acts on none.
public static class ClaimAccess
{
    public static Expression<Func<Claim, bool>> CanView(CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var userId = user.Id;
        if (user.IsAdmin) return claim => true;
        if (user.IsHandler) return claim => claim.AssignedToUserId == userId;
        if (user.IsReader) return claim => !claim.IsPrivate;

        return claim => false;
    }

    public static bool CanView(CurrentUser user, Claim claim) => CanView(user).Compile()(claim);

    public static bool CanAct(CurrentUser user, Claim claim)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claim);

        if (user.IsAdmin) return true;
        if (user.IsHandler) return claim.AssignedToUserId == user.Id;

        return false;
    }

    // Whether the user may delete a document: an unmatched document follows the standalone rule, a matched one
    // needs the right to act on every claim it is linked to (deleting it removes it from all of them).
    public static bool CanActOnDocument(CurrentUser user, Document document, IReadOnlyCollection<Claim> linkedClaims)
    {
        ArgumentNullException.ThrowIfNull(linkedClaims);

        return linkedClaims.Count == 0
            ? CanActOnStandaloneDocument(user, document)
            : linkedClaims.All(claim => CanAct(user, claim));
    }

    // An unmatched document (linked to no claim): Admins and Readers see it; a Handler sees only what they uploaded.
    public static bool CanViewStandaloneDocument(CurrentUser user, Document document)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(document);

        if (user.IsAdmin) return true;
        if (user.IsHandler) return document.UploadedByUserId == user.Id;

        return user.IsReader;
    }

    public static bool CanActOnStandaloneDocument(CurrentUser user, Document document)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(document);

        if (user.IsAdmin) return true;

        return user.IsHandler && document.UploadedByUserId == user.Id;
    }
}
