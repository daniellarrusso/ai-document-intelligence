using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repositoryMock = new();

    [Fact]
    public async Task RecordSignInAsync_ValidUser_UpsertsUserWithCurrentTimestamp()
    {
        var sut = new UserService(_repositoryMock.Object);
        var id = Guid.NewGuid();
        AppUser? saved = null;
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
            .Callback<AppUser, CancellationToken>((u, _) => saved = u)
            .Returns(Task.CompletedTask);

        await sut.RecordSignInAsync(new CurrentUser(id, "Jane Doe", "jane@example.com", [AppRoles.Handler]));

        Assert.NotNull(saved);
        Assert.Equal(id, saved.Id);
        Assert.Equal("Jane Doe", saved.DisplayName);
        Assert.Equal("jane@example.com", saved.Email);
        Assert.True(saved.LastSeenAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task RecordSignInAsync_NullUser_Throws()
    {
        var sut = new UserService(_repositoryMock.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RecordSignInAsync(null!));
    }

    [Theory]
    [InlineData(AppRoles.Handler, true)]
    [InlineData(AppRoles.Admin, false)]
    [InlineData(AppRoles.Reader, false)]
    public async Task RecordSignInAsync_FlagsOnlyHandlers(string role, bool expectedIsHandler)
    {
        var sut = new UserService(_repositoryMock.Object);

        await sut.RecordSignInAsync(new CurrentUser(Guid.NewGuid(), "Someone", null, [role]));

        _repositoryMock.Verify(r => r.UpsertAsync(It.Is<AppUser>(u => u.IsHandler == expectedIsHandler), It.IsAny<CancellationToken>()), Times.Once);
    }
}
