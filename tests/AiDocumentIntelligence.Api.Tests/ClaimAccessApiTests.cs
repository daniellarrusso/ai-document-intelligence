using System.Net;
using System.Net.Http.Json;
using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Api.Tests;

// What the API asks the data layer, and how it answers, depending on who is calling. The visibility rules
// themselves are covered in ClaimAccessTests; these check the controllers hand the right user to them.
public class ClaimAccessApiTests : IDisposable
{
    private static readonly Guid ClaimId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    // A factory per test: the mocks record invocations and keep setups, so sharing one would leak between tests.
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientFor(string user, string roles, bool withOid = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (!withOid) client.DefaultRequestHeaders.Add(TestAuthHandler.NoOidHeader, "1");
        return client;
    }

    private static MultipartFormDataContent File() =>
        new() { { new ByteArrayContent([1, 2, 3]), "file", "a.pdf" } };

    [Fact]
    public async Task GetClaims_AsHandler_QueriesOnBehalfOfThatUser()
    {
        using var client = ClientFor("handler-a", AppRoles.Handler);

        using var response = await client.GetAsync("/api/claims");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Claims.Verify(r => r.GetClaimsAsync(
            It.Is<CurrentUser>(u => u.Id == TestAuthHandler.OidFor("handler-a") && u.IsHandler && !u.IsAdmin),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<ClaimStatus?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("GET", "/api/claims")]
    [InlineData("GET", "/api/documents")]
    [InlineData("POST", "/api/claims")]
    public async Task Endpoints_TokenWithoutObjectId_AreForbidden(string method, string url)
    {
        using var client = ClientFor("someone", AppRoles.Admin, withOid: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { policyNumber = "P", claimantName = "C", incidentDate = "2026-01-01", amountClaimed = 5 });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateClaim_AsHandler_AssignsTheClaimToThem()
    {
        Claim? created = null;
        _factory.Claims
            .Setup(r => r.CreateClaimAsync(It.IsAny<Claim>(), It.IsAny<CancellationToken>()))
            .Callback<Claim, CancellationToken>((c, _) => created = c)
            .Returns(Task.CompletedTask);
        using var client = ClientFor("handler-b", AppRoles.Handler);

        using var response = await client.PostAsJsonAsync("/api/claims", new
        {
            policyNumber = "POL-1",
            claimantName = "Jane",
            incidentDate = "2026-01-01",
            amountClaimed = 10,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(TestAuthHandler.OidFor("handler-b"), created.AssignedToUserId);
        Assert.Equal(TestAuthHandler.OidFor("handler-b"), created.CreatedByUserId);
    }

    [Fact]
    public async Task GetClaimById_ClaimNotVisibleToCaller_ReturnsNotFound()
    {
        // The repository returns null for claims the caller may not view (e.g. a private claim, for a Reader).
        _factory.Claims
            .Setup(r => r.GetClaimWithDocumentsAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Claim?)null);
        using var client = ClientFor("reader-a", AppRoles.Reader);

        using var response = await client.GetAsync($"/api/claims/{ClaimId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadClaimDocument_ClaimNotVisibleToCaller_ReturnsNotFoundAndStoresNothing()
    {
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Claim?)null);
        using var client = ClientFor("handler-c", AppRoles.Handler);

        using var response = await client.PostAsync($"/api/claims/{ClaimId}/documents", File());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _factory.Documents.Verify(r => r.CreateDocumentAsync(It.IsAny<Document>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadClaimDocument_OwnClaim_RecordsUploader()
    {
        Document? saved = null;
        Guid? savedClaimId = null;
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Claim { Id = ClaimId, AssignedToUserId = TestAuthHandler.OidFor("handler-d") });
        _factory.Documents
            .Setup(r => r.CreateDocumentAsync(It.IsAny<Document>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Document, Guid?, Guid?, CancellationToken>((d, claimId, _, _) => { saved = d; savedClaimId = claimId; })
            .ReturnsAsync(Guid.NewGuid());
        using var client = ClientFor("handler-d", AppRoles.Handler);

        using var response = await client.PostAsync($"/api/claims/{ClaimId}/documents", File());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(saved);
        Assert.Equal(ClaimId, savedClaimId);
        Assert.Equal(TestAuthHandler.OidFor("handler-d"), saved.UploadedByUserId);
    }

    [Fact]
    public async Task LinkDocument_OwnClaimAndVisibleDocument_LinksAndReturnsNoContent()
    {
        var handler = TestAuthHandler.OidFor("handler-f");
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Claim { Id = ClaimId, AssignedToUserId = handler });
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document { Id = DocumentId });
        using var client = ClientFor("handler-f", AppRoles.Handler);

        using var response = await client.PutAsync($"/api/claims/{ClaimId}/documents/{DocumentId}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _factory.Documents.Verify(r => r.LinkToClaimAsync(DocumentId, ClaimId, handler, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkDocument_ClaimVisibleButAssignedToSomeoneElse_ReturnsForbidden()
    {
        // Visible (so not a 404) but assigned elsewhere: a Handler can't act on it.
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Claim { Id = ClaimId, AssignedToUserId = Guid.NewGuid() });
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document { Id = DocumentId });
        using var client = ClientFor("handler-g", AppRoles.Handler);

        using var response = await client.PutAsync($"/api/claims/{ClaimId}/documents/{DocumentId}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _factory.Documents.Verify(r => r.LinkToClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkDocument_DocumentNotVisibleToCaller_ReturnsNotFound()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = ClientFor("handler-h", AppRoles.Handler);

        using var response = await client.PutAsync($"/api/claims/{ClaimId}/documents/{DocumentId}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkDocument_AsAdmin_RemovesLinkAndReturnsNoContent()
    {
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Claim { Id = ClaimId, AssignedToUserId = Guid.NewGuid() });
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document { Id = DocumentId });
        using var client = ClientFor("admin-b", AppRoles.Admin);

        using var response = await client.DeleteAsync($"/api/claims/{ClaimId}/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _factory.Documents.Verify(r => r.UnlinkFromClaimAsync(DocumentId, ClaimId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDocumentClaims_DocumentNotVisibleToCaller_ReturnsNotFound()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = ClientFor("reader-c", AppRoles.Reader);

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/claims");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_Unmatched_PassesFilterAndSearchToTheRepository()
    {
        using var client = ClientFor("admin-c", AppRoles.Admin);

        using var response = await client.GetAsync("/api/documents?unmatched=true&q=%20invoice%20&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Documents.Verify(r => r.GetDocumentsAsync(
            It.IsAny<CurrentUser>(), 1, 5, "invoice", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDocuments_PageSizeTooLarge_ReturnsBadRequest()
    {
        using var client = ClientFor("admin-d", AppRoles.Admin);

        using var response = await client.GetAsync("/api/documents?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMatchSuggestions_DocumentNotVisibleToCaller_ReturnsNotFound()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = ClientFor("handler-i", AppRoles.Handler);

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/match-suggestions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMatchSuggestions_TextQuotesOwnClaimsReference_ReturnsItAsAReferenceMatch()
    {
        var handler = TestAuthHandler.OidFor("handler-j");
        var claim = new Claim { Id = ClaimId, Reference = "CLM-20261015-011076AB", PolicyNumber = "P", ClaimantName = "Jane Doe", AssignedToUserId = handler };
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document { Id = DocumentId, ExtractedText = "About CLM-20261015-011076AB." });
        _factory.Claims.Setup(r => r.GetClaimsForDocumentAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _factory.Claims.Setup(r => r.GetClaimsForMatchingAsync(It.IsAny<CurrentUser>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([claim]);
        _factory.Claims.Setup(r => r.GetClaimsByIdentifiersAsync(It.IsAny<CurrentUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        using var client = ClientFor("handler-j", AppRoles.Handler);

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/match-suggestions");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var suggestion = body.GetProperty("suggestions").EnumerateArray().Single();
        Assert.Equal("CLM-20261015-011076AB", suggestion.GetProperty("claim").GetProperty("reference").GetString());
        Assert.Equal((int)MatchMethod.Reference, suggestion.GetProperty("method").GetInt32());
        Assert.False(body.GetProperty("aiAttempted").GetBoolean());
    }

    [Fact]
    public async Task GetDocuments_IncludesHowManyClaimsEachIsLinkedTo()
    {
        // The factory sets its defaults when the first client is created, so create it before overriding them.
        using var client = ClientFor("admin-e", AppRoles.Admin);
        var linked = new Document { Id = Guid.NewGuid(), FileName = "a.pdf" };
        var unmatched = new Document { Id = Guid.NewGuid(), FileName = "b.pdf" };
        _factory.Documents
            .Setup(r => r.GetDocumentsAsync(It.IsAny<CurrentUser>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([linked, unmatched]);
        _factory.Documents
            .Setup(r => r.GetLinkCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [linked.Id] = 2 });

        var items = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/documents");

        Assert.Equal([2, 0], items.EnumerateArray().Select(i => i.GetProperty("claimCount").GetInt32()));
        Assert.Equal("a.pdf", items[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task DeleteDocument_NotVisibleToCaller_ReturnsNotFound()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = ClientFor("handler-e", AppRoles.Handler);

        using var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AskDocument_NotVisibleToCaller_ReturnsNotFound()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = ClientFor("reader-b", AppRoles.Reader);

        using var response = await client.PostAsJsonAsync($"/api/documents/{DocumentId}/ask", new { question = "what?" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHandlers_ReturnsIdAndNameOnly()
    {
        var olive = new AppUser { Id = Guid.NewGuid(), DisplayName = "Olive", Email = "olive@example.com", IsHandler = true };
        _factory.Users.Setup(r => r.GetHandlersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([olive]);
        using var client = ClientFor("admin-a", AppRoles.Admin);

        using var response = await client.GetAsync("/api/users/handlers");
        var body = await response.Content.ReadFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var only = Assert.Single(body!);
        Assert.Equal(olive.Id, only["id"].GetGuid());
        Assert.Equal("Olive", only["displayName"].GetString());
        Assert.Equal(2, only.Count);
    }

    private void GivenOwnClaim(string user)
    {
        _factory.Claims
            .Setup(r => r.GetClaimAsync(It.IsAny<CurrentUser>(), ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Claim { Id = ClaimId, AssignedToUserId = TestAuthHandler.OidFor(user) });
    }

    [Fact]
    public async Task AssignClaim_ToKnownHandler_ReturnsNoContentAndUpdatesAssignee()
    {
        var target = Guid.NewGuid();
        GivenOwnClaim("handler-f");
        _factory.Users
            .Setup(r => r.FindHandlerAsync(target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = target, DisplayName = "T", IsHandler = true });
        using var client = ClientFor("handler-f", AppRoles.Handler);

        using var response = await client.PutAsJsonAsync($"/api/claims/{ClaimId}/assignee", new { userId = target });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _factory.Claims.Verify(r => r.UpdateAssigneeAsync(ClaimId, target, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignClaim_ToSomeoneWhoIsNotAHandler_ReturnsBadRequest()
    {
        GivenOwnClaim("handler-g");
        using var client = ClientFor("handler-g", AppRoles.Handler);

        using var response = await client.PutAsJsonAsync($"/api/claims/{ClaimId}/assignee", new { userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _factory.Claims.Verify(r => r.UpdateAssigneeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignClaim_ClaimNotVisibleToCaller_ReturnsNotFound()
    {
        using var client = ClientFor("handler-h", AppRoles.Handler);

        using var response = await client.PutAsJsonAsync($"/api/claims/{Guid.NewGuid()}/assignee", new { userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AssignClaim_EmptyUserId_ReturnsBadRequest()
    {
        using var client = ClientFor("handler-i", AppRoles.Handler);

        using var response = await client.PutAsJsonAsync($"/api/claims/{ClaimId}/assignee", new { userId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetPrivacy_OwnClaim_ReturnsNoContent()
    {
        GivenOwnClaim("handler-j");
        using var client = ClientFor("handler-j", AppRoles.Handler);

        using var response = await client.PutAsJsonAsync($"/api/claims/{ClaimId}/privacy", new { isPrivate = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _factory.Claims.Verify(r => r.UpdatePrivacyAsync(ClaimId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Me_IncludesTheCallersObjectId()
    {
        using var client = ClientFor("me-user", AppRoles.Handler);

        var me = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/me");

        Assert.Equal(TestAuthHandler.OidFor("me-user"), me!["id"].GetGuid());
    }
}
