using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class ClaimAccessTests
{
    private static Claim ClaimFor(CurrentUser? assignee, bool isPrivate = false) => new()
    {
        Id = Guid.NewGuid(),
        AssignedToUserId = assignee?.Id,
        IsPrivate = isPrivate
    };

    public static TheoryData<string, bool, bool> ViewMatrix => new()
    {
        // who, claim is private, expected: can view a claim assigned to the Handler
        { "admin", false, true },
        { "admin", true, true },
        { "handler", false, true },
        { "handler", true, true },
        { "other-handler", false, false },
        { "other-handler", true, false },
        { "reader", false, true },
        { "reader", true, false },
        { "no-role", false, false },
        { "no-role", true, false },
    };

    private static CurrentUser Resolve(string who) => who switch
    {
        "admin" => TestUsers.Admin,
        "handler" => TestUsers.Handler,
        "other-handler" => TestUsers.OtherHandler,
        "reader" => TestUsers.Reader,
        _ => TestUsers.NoRole
    };

    [Theory]
    [MemberData(nameof(ViewMatrix))]
    public void CanView_Matrix_FollowsRoleAndPrivacy(string who, bool isPrivate, bool expected)
    {
        var claim = ClaimFor(TestUsers.Handler, isPrivate);

        Assert.Equal(expected, ClaimAccess.CanView(Resolve(who), claim));
    }

    [Theory]
    [MemberData(nameof(ViewMatrix))]
    public void CanViewExpression_AgreesWithInMemoryCheck_WhenFilteringACollection(string who, bool isPrivate, bool expected)
    {
        // The expression is what the database runs, so it must select exactly what the in-memory check allows.
        var claims = new[] { ClaimFor(TestUsers.Handler, isPrivate), ClaimFor(TestUsers.OtherHandler, isPrivate), ClaimFor(null, isPrivate) };
        var user = Resolve(who);

        var viaExpression = claims.AsQueryable().Where(ClaimAccess.CanView(user)).ToList();
        var viaCheck = claims.Where(c => ClaimAccess.CanView(user, c)).ToList();

        Assert.Equal(viaCheck, viaExpression);
        Assert.Equal(expected, viaExpression.Contains(claims[0]));
    }

    [Fact]
    public void CanView_UnassignedClaim_IsVisibleOnlyToAdminAndToReadersWhenPublic()
    {
        var publicClaim = ClaimFor(null);
        var privateClaim = ClaimFor(null, isPrivate: true);

        Assert.True(ClaimAccess.CanView(TestUsers.Admin, publicClaim));
        Assert.True(ClaimAccess.CanView(TestUsers.Reader, publicClaim));
        Assert.False(ClaimAccess.CanView(TestUsers.Reader, privateClaim));
        Assert.False(ClaimAccess.CanView(TestUsers.Handler, publicClaim));
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("handler", true)]
    [InlineData("other-handler", false)]
    [InlineData("reader", false)]
    [InlineData("no-role", false)]
    public void CanAct_OnlyAdminAndAssignedHandler(string who, bool expected)
    {
        Assert.Equal(expected, ClaimAccess.CanAct(Resolve(who), ClaimFor(TestUsers.Handler)));
    }

    [Fact]
    public void CanView_AdminAndReaderBoth_HighestRoleWins()
    {
        var both = new CurrentUser(Guid.NewGuid(), "Both", null, [AppRoles.Reader, AppRoles.Handler]);

        // A user holding Reader and Handler is a Handler: they do not also get every public claim.
        Assert.False(ClaimAccess.CanView(both, ClaimFor(TestUsers.OtherHandler)));
        Assert.True(ClaimAccess.CanView(both, ClaimFor(both)));
    }

    [Theory]
    [InlineData("admin", true, true)]
    [InlineData("reader", true, false)]
    [InlineData("handler", true, true)]
    [InlineData("other-handler", false, false)]
    [InlineData("no-role", false, false)]
    public void StandaloneDocument_VisibilityAndActions(string who, bool canView, bool canAct)
    {
        var document = new Document { Id = Guid.NewGuid(), UploadedByUserId = TestUsers.Handler.Id };
        var user = Resolve(who);

        Assert.Equal(canView, ClaimAccess.CanViewStandaloneDocument(user, document));
        Assert.Equal(canAct, ClaimAccess.CanActOnStandaloneDocument(user, document));
    }

    [Fact]
    public void CanActOnDocument_Unmatched_FollowsTheStandaloneRule()
    {
        var document = new Document { Id = Guid.NewGuid(), UploadedByUserId = TestUsers.Handler.Id };

        Assert.True(ClaimAccess.CanActOnDocument(TestUsers.Handler, document, []));
        Assert.False(ClaimAccess.CanActOnDocument(TestUsers.OtherHandler, document, []));
        Assert.True(ClaimAccess.CanActOnDocument(TestUsers.Admin, document, []));
    }

    [Fact]
    public void CanActOnDocument_LinkedToSeveralClaims_NeedsTheRightToActOnAllOfThem()
    {
        var document = new Document { Id = Guid.NewGuid() };
        var own = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var others = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.OtherHandler.Id };

        Assert.True(ClaimAccess.CanActOnDocument(TestUsers.Handler, document, [own]));
        Assert.False(ClaimAccess.CanActOnDocument(TestUsers.Handler, document, [own, others]));
        Assert.True(ClaimAccess.CanActOnDocument(TestUsers.Admin, document, [own, others]));
        Assert.False(ClaimAccess.CanActOnDocument(TestUsers.Reader, document, [own]));
    }
}
