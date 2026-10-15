using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class ClaimServiceTests
{
    private readonly Mock<IClaimRepository> _repositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();

    private ClaimService CreateSut() => new(_repositoryMock.Object, new UserService(_userRepositoryMock.Object));

    private static CreateClaimRequest CreateRequest(
        string? policyNumber = "POL-123",
        string? claimantName = "Jane Doe",
        DateOnly? incidentDate = null,
        decimal amountClaimed = 1500.50m,
        Guid? assignedToUserId = null,
        bool isPrivate = false) =>
        new(policyNumber, claimantName, incidentDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), amountClaimed, assignedToUserId, isPrivate);

    [Fact]
    public async Task CreateClaimAsync_ValidRequest_PersistsOpenClaimWithGeneratedReference()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest());

        Assert.NotEqual(Guid.Empty, claim.Id);
        Assert.StartsWith("CLM-", claim.Reference);
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Equal("POL-123", claim.PolicyNumber);
        Assert.Equal("Jane Doe", claim.ClaimantName);
        Assert.Equal(1500.50m, claim.AmountClaimed);
        Assert.True(claim.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
        _repositoryMock.Verify(r => r.CreateClaimAsync(claim, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClaimAsync_TwoClaims_GetDifferentReferences()
    {
        var sut = CreateSut();

        var first = await sut.CreateClaimAsync(CreateRequest());
        var second = await sut.CreateClaimAsync(CreateRequest());

        Assert.NotEqual(first.Reference, second.Reference);
    }

    [Fact]
    public async Task CreateClaimAsync_TextFields_AreTrimmed()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(policyNumber: "  POL-1  ", claimantName: " Jane "));

        Assert.Equal("POL-1", claim.PolicyNumber);
        Assert.Equal("Jane", claim.ClaimantName);
    }

    [Fact]
    public async Task CreateClaimAsync_AmountWithExtraDecimals_IsRoundedToTwoPlaces()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(amountClaimed: 10.555m));

        Assert.Equal(10.56m, claim.AmountClaimed);
    }

    [Fact]
    public async Task CreateClaimAsync_IncidentToday_IsAccepted()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(incidentDate: DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.NotNull(claim);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateClaimAsync_MissingPolicyNumber_ThrowsArgumentException(string? policyNumber)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(policyNumber: policyNumber)));

        _repositoryMock.Verify(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateClaimAsync_MissingClaimantName_ThrowsArgumentException(string? claimantName)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(claimantName: claimantName)));

        _repositoryMock.Verify(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateClaimAsync_FieldsTooLong_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(policyNumber: new string('a', Claim.MaxPolicyNumberLength + 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(claimantName: new string('a', Claim.MaxNameLength + 1))));

        _repositoryMock.Verify(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateClaimAsync_IncidentDateInFuture_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(incidentDate: tomorrow)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    public async Task CreateClaimAsync_AmountNotPositive_ThrowsArgumentException(double amount)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(amountClaimed: (decimal)amount)));
    }

    [Fact]
    public async Task CreateClaimAsync_AmountTooLargeForColumn_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(amountClaimed: 10_000_000_000_000_000m)));

        Assert.Contains("cannot exceed", ex.Message);
        Assert.DoesNotContain("greater than zero", ex.Message);
    }

    [Fact]
    public async Task CreateClaimAsync_NullRequest_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.CreateClaimAsync(null!));
    }

    [Fact]
    public async Task CreateClaimAsync_RepositoryFails_PropagatesException()
    {
        _repositoryMock
            .Setup(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateClaimAsync(CreateRequest()));
    }

    [Fact]
    public async Task GetClaimsAsync_PassesPagingToRepository()
    {
        var claims = new List<Claim> { new() { Id = Guid.NewGuid() } };
        _repositoryMock.Setup(r => r.GetClaimsAsync(TestUsers.Handler, 2, 25, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(claims);
        var sut = CreateSut();

        var result = await sut.GetClaimsAsync(2, 25);

        Assert.Same(claims, result);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchAndStatus_AreForwardedTrimmed()
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, "  smith  ", ClaimStatus.InReview);

        _repositoryMock.Verify(r => r.GetClaimsAsync(TestUsers.Handler, 1, 10, "smith", ClaimStatus.InReview, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetClaimsAsync_BlankSearch_AppliesNoTextFilter(string? search)
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, search);

        _repositoryMock.Verify(r => r.GetClaimsAsync(TestUsers.Handler, 1, 10, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchAtMaxLength_IsAccepted()
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, new string('a', ClaimService.MaxSearchLength));

        _repositoryMock.Verify(r => r.GetClaimsAsync(TestUsers.Handler, 1, 10, It.IsAny<string?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchTooLong_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetClaimsAsync(1, 10, new string('a', ClaimService.MaxSearchLength + 1)));

        _repositoryMock.Verify(r => r.GetClaimsAsync(It.IsAny<CurrentUser>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<ClaimStatus?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetClaimWithDocumentsAsync_ClaimMissing_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.GetClaimWithDocumentsAsync(TestUsers.Handler, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateClaimAsync_ValidRequest_AssignsAndAttributesToCreator()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(TestUsers.Handler.Id, claim.AssignedToUserId);
        Assert.Equal(TestUsers.Handler.Id, claim.CreatedByUserId);
        Assert.False(claim.IsPrivate);
    }

    [Fact]
    public async Task CreateClaimAsync_ValidRequest_EnsuresCreatorExistsBeforeSavingClaim()
    {
        var calls = new List<string>();
        _userRepositoryMock
            .Setup(r => r.UpsertAsync(It.Is<AppUser>(u => u.Id == TestUsers.Handler.Id), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("user"))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("claim"))
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        await sut.CreateClaimAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(["user", "claim"], calls);
    }

    [Fact]
    public async Task CreateClaimAsync_InvalidRequest_DoesNotRecordTheUser()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(policyNumber: ""), TestUsers.Handler));

        _userRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthorizeActionAsync_ClaimNotVisibleOrMissing_ReturnsNotFound()
    {
        var sut = CreateSut();

        var outcome = await sut.AuthorizeActionAsync(TestUsers.Handler, Guid.NewGuid());

        Assert.Equal(AccessOutcome.NotFound, outcome);
    }

    [Theory]
    [InlineData("handler-own", AccessOutcome.Allowed)]
    [InlineData("admin", AccessOutcome.Allowed)]
    [InlineData("reader", AccessOutcome.Forbidden)]
    public async Task AuthorizeActionAsync_VisibleClaim_OnlyOwnerAndAdminMayAct(string who, AccessOutcome expected)
    {
        var user = who switch
        {
            "handler-own" => TestUsers.Handler,
            "admin" => TestUsers.Admin,
            _ => TestUsers.Reader
        };
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        _repositoryMock.Setup(r => r.GetClaimAsync(user, claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        var sut = CreateSut();

        Assert.Equal(expected, await sut.AuthorizeActionAsync(user, claim.Id));
    }

    private static readonly AppUser OtherHandlerUser = new() { Id = TestUsers.OtherHandler.Id, DisplayName = "Olive Other", IsHandler = true };

    [Fact]
    public async Task CreateClaimAsync_AssigneeIsAnotherHandler_AssignsToThemAndKeepsCreator()
    {
        _userRepositoryMock
            .Setup(r => r.FindHandlerAsync(TestUsers.OtherHandler.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherHandlerUser);
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(assignedToUserId: TestUsers.OtherHandler.Id, isPrivate: true), TestUsers.Handler);

        Assert.Equal(TestUsers.OtherHandler.Id, claim.AssignedToUserId);
        Assert.Equal(TestUsers.Handler.Id, claim.CreatedByUserId);
        Assert.True(claim.IsPrivate);
    }

    [Fact]
    public async Task CreateClaimAsync_AssigneeIsNotAKnownHandler_ThrowsAndSavesNothing()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateClaimAsync(CreateRequest(assignedToUserId: Guid.NewGuid()), TestUsers.Handler));

        _repositoryMock.Verify(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateClaimAsync_AssigneeIsTheCreator_DoesNotRequireAHandlerLookup()
    {
        var sut = CreateSut();

        // An Admin who is not flagged as a Handler may assign to themselves.
        var claim = await sut.CreateClaimAsync(CreateRequest(assignedToUserId: TestUsers.Admin.Id), TestUsers.Admin);

        Assert.Equal(TestUsers.Admin.Id, claim.AssignedToUserId);
        _userRepositoryMock.Verify(r => r.FindHandlerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GivenVisibleClaim(CurrentUser user, Claim claim) =>
        _repositoryMock.Setup(r => r.GetClaimAsync(user, claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claim);

    [Fact]
    public async Task AssignClaimAsync_OwnerAssignsToHandler_UpdatesAssignee()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        GivenVisibleClaim(TestUsers.Handler, claim);
        _userRepositoryMock
            .Setup(r => r.FindHandlerAsync(TestUsers.OtherHandler.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherHandlerUser);
        var sut = CreateSut();

        var outcome = await sut.AssignClaimAsync(TestUsers.Handler, claim.Id, TestUsers.OtherHandler.Id);

        Assert.Equal(AccessOutcome.Allowed, outcome);
        _repositoryMock.Verify(r => r.UpdateAssigneeAsync(claim.Id, TestUsers.OtherHandler.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignClaimAsync_AdminReassignsAnyVisibleClaim_UpdatesAssignee()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        GivenVisibleClaim(TestUsers.Admin, claim);
        _userRepositoryMock
            .Setup(r => r.FindHandlerAsync(TestUsers.OtherHandler.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherHandlerUser);
        var sut = CreateSut();

        var outcome = await sut.AssignClaimAsync(TestUsers.Admin, claim.Id, TestUsers.OtherHandler.Id);

        Assert.Equal(AccessOutcome.Allowed, outcome);
    }

    [Fact]
    public async Task AssignClaimAsync_ClaimNotVisible_ReturnsNotFoundAndChangesNothing()
    {
        var sut = CreateSut();

        var outcome = await sut.AssignClaimAsync(TestUsers.OtherHandler, Guid.NewGuid(), TestUsers.Handler.Id);

        Assert.Equal(AccessOutcome.NotFound, outcome);
        _repositoryMock.Verify(r => r.UpdateAssigneeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignClaimAsync_ReaderCanViewButNotAssign_ReturnsForbidden()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        GivenVisibleClaim(TestUsers.Reader, claim);
        var sut = CreateSut();

        var outcome = await sut.AssignClaimAsync(TestUsers.Reader, claim.Id, TestUsers.OtherHandler.Id);

        Assert.Equal(AccessOutcome.Forbidden, outcome);
        _repositoryMock.Verify(r => r.UpdateAssigneeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignClaimAsync_AssigneeNotAHandler_ThrowsAndChangesNothing()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        GivenVisibleClaim(TestUsers.Handler, claim);
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.AssignClaimAsync(TestUsers.Handler, claim.Id, Guid.NewGuid()));

        _repositoryMock.Verify(r => r.UpdateAssigneeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetPrivacyAsync_OwnerChangesPrivacy_UpdatesClaim(bool isPrivate)
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        GivenVisibleClaim(TestUsers.Handler, claim);
        var sut = CreateSut();

        var outcome = await sut.SetPrivacyAsync(TestUsers.Handler, claim.Id, isPrivate);

        Assert.Equal(AccessOutcome.Allowed, outcome);
        _repositoryMock.Verify(r => r.UpdatePrivacyAsync(claim.Id, isPrivate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPrivacyAsync_Reader_IsForbidden()
    {
        var claim = new Claim { Id = Guid.NewGuid() };
        GivenVisibleClaim(TestUsers.Reader, claim);
        var sut = CreateSut();

        var outcome = await sut.SetPrivacyAsync(TestUsers.Reader, claim.Id, true);

        Assert.Equal(AccessOutcome.Forbidden, outcome);
        _repositoryMock.Verify(r => r.UpdatePrivacyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

// Most of these tests are about validation and paging, not who is asking, so they act as an ordinary Handler.
internal static class ClaimServiceTestExtensions
{
    public static Task<Claim> CreateClaimAsync(this ClaimService sut, CreateClaimRequest request) =>
        sut.CreateClaimAsync(request, TestUsers.Handler);

    public static Task<List<Claim>> GetClaimsAsync(this ClaimService sut, int pageNumber, int pageSize, string? search = null, ClaimStatus? status = null) =>
        sut.GetClaimsAsync(TestUsers.Handler, pageNumber, pageSize, search, status);
}
