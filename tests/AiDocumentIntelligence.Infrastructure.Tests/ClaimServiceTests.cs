using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class ClaimServiceTests
{
    private readonly Mock<IClaimRepository> _repositoryMock = new();

    private ClaimService CreateSut() => new(_repositoryMock.Object);

    private static CreateClaimRequest CreateRequest(
        string? policyNumber = "POL-123",
        string? claimantName = "Jane Doe",
        DateOnly? incidentDate = null,
        decimal amountClaimed = 1500.50m,
        string? assignedTo = null) =>
        new(policyNumber, claimantName, incidentDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), amountClaimed, assignedTo);

    [Fact]
    public async Task CreateClaimAsync_ValidRequest_PersistsOpenClaimWithGeneratedReference()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(assignedTo: "adjuster@example.com"));

        Assert.NotEqual(Guid.Empty, claim.Id);
        Assert.StartsWith("CLM-", claim.Reference);
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Equal("POL-123", claim.PolicyNumber);
        Assert.Equal("Jane Doe", claim.ClaimantName);
        Assert.Equal(1500.50m, claim.AmountClaimed);
        Assert.Equal("adjuster@example.com", claim.AssignedTo);
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
    public async Task CreateClaimAsync_TextFields_AreTrimmedAndBlankAssigneeBecomesNull()
    {
        var sut = CreateSut();

        var claim = await sut.CreateClaimAsync(CreateRequest(policyNumber: "  POL-1  ", claimantName: " Jane ", assignedTo: "   "));

        Assert.Equal("POL-1", claim.PolicyNumber);
        Assert.Equal("Jane", claim.ClaimantName);
        Assert.Null(claim.AssignedTo);
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
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateClaimAsync(CreateRequest(assignedTo: new string('a', Claim.MaxNameLength + 1))));

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
        _repositoryMock.Setup(r => r.GetClaimsAsync(2, 25, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(claims);
        var sut = CreateSut();

        var result = await sut.GetClaimsAsync(2, 25);

        Assert.Same(claims, result);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchAndStatus_AreForwardedTrimmed()
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, "  smith  ", ClaimStatus.InReview);

        _repositoryMock.Verify(r => r.GetClaimsAsync(1, 10, "smith", ClaimStatus.InReview, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetClaimsAsync_BlankSearch_AppliesNoTextFilter(string? search)
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, search);

        _repositoryMock.Verify(r => r.GetClaimsAsync(1, 10, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchAtMaxLength_IsAccepted()
    {
        var sut = CreateSut();

        await sut.GetClaimsAsync(1, 10, new string('a', ClaimService.MaxSearchLength));

        _repositoryMock.Verify(r => r.GetClaimsAsync(1, 10, It.IsAny<string?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClaimsAsync_SearchTooLong_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetClaimsAsync(1, 10, new string('a', ClaimService.MaxSearchLength + 1)));

        _repositoryMock.Verify(r => r.GetClaimsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<ClaimStatus?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetClaimWithDocumentsAsync_ClaimMissing_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.GetClaimWithDocumentsAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
