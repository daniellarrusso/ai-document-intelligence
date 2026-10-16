using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.Logging;

namespace AiDocumentIntelligence.Infrastructure;

// For a current link: the claim and the evidence in the text that supports it (null = the text never mentions it).
public record LinkedClaimCheck(Claim Claim, ClaimMatch? Evidence);

public record MatchScan(
    bool TextAvailable,
    IReadOnlyList<ClaimMatch> Suggestions,
    IReadOnlyList<LinkedClaimCheck> LinkedClaims,
    bool AiAttempted,
    bool AiFailed);

// Suggests which claims a document belongs to, and whether its current links look right. It only suggests; a
// person confirms each link.
public class DocumentMatchService
{
    // Claims scanned for name matches. Exact references and policy numbers are looked up in the database
    // regardless, so only name and AI matching are limited to the newest claims.
    private const int MaxNameCandidates = 500;

    // Candidates shown to the AI; more than this makes small models unreliable.
    private const int MaxAiCandidates = 30;

    private readonly IDocumentRepository _documentRepository;
    private readonly IClaimRepository _claimRepository;
    private readonly IClaimMatchAssistant _assistant;
    private readonly ILogger<DocumentMatchService> _logger;

    public DocumentMatchService(
        IDocumentRepository documentRepository,
        IClaimRepository claimRepository,
        IClaimMatchAssistant assistant,
        ILogger<DocumentMatchService> logger)
    {
        _documentRepository = documentRepository;
        _claimRepository = claimRepository;
        _assistant = assistant;
        _logger = logger;
    }

    // Null if the document doesn't exist or the user may not view it. Only claims the user may act on are
    // suggested, since linking needs that right. With useAi, the AI is asked only when the deterministic
    // checks (reference, policy number, claimant name) found nothing.
    public async Task<MatchScan?> ScanAsync(CurrentUser user, Guid documentId, bool useAi, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetDocumentForUserAsync(user, documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var text = document.ExtractedText;
        var linked = await _claimRepository.GetClaimsForDocumentAsync(user, documentId, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MatchScan(false, [], linked.Select(c => new LinkedClaimCheck(c, null)).ToList(), false, false);
        }

        var linkedIds = linked.Select(c => c.Id).ToHashSet();
        var candidates = await LoadCandidatesAsync(user, text, cancellationToken);

        var suggestions = candidates
            .Where(c => !linkedIds.Contains(c.Id) && ClaimAccess.CanAct(user, c))
            .Select(c => ClaimTextMatcher.Match(c, text))
            .OfType<ClaimMatch>()
            .OrderBy(m => m.Method)
            .ThenBy(m => m.Claim.Reference)
            .ToList();

        var aiAttempted = false;
        var aiFailed = false;
        if (useAi && suggestions.Count == 0)
        {
            aiAttempted = true;
            try
            {
                suggestions = (await AskAssistantAsync(user, text, candidates, linkedIds, cancellationToken)).ToList();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "AI claim matching failed for document {DocumentId}", documentId);
                aiFailed = true;
            }
        }

        var checks = linked.Select(c => new LinkedClaimCheck(c, ClaimTextMatcher.Match(c, text))).ToList();

        return new MatchScan(true, suggestions, checks, aiAttempted, aiFailed);
    }

    // The newest claims (for name matching) plus any claim the text names by reference or policy number.
    private async Task<List<Claim>> LoadCandidatesAsync(CurrentUser user, string text, CancellationToken cancellationToken)
    {
        var recent = await _claimRepository.GetClaimsForMatchingAsync(user, MaxNameCandidates, cancellationToken);
        var named = await _claimRepository.GetClaimsByIdentifiersAsync(
            user,
            ClaimTextMatcher.FindReferences(text),
            ClaimTextMatcher.FindPolicyCandidates(text),
            cancellationToken);

        return recent.Concat(named).DistinctBy(c => c.Id).ToList();
    }

    private async Task<IReadOnlyList<ClaimMatch>> AskAssistantAsync(
        CurrentUser user, string text, List<Claim> candidates, HashSet<Guid> linkedIds, CancellationToken cancellationToken)
    {
        var words = ClaimTextMatcher.WordsOf(text);

        // Show the AI the claims whose names look most like the document first, then the newest.
        var shortlist = candidates
            .Where(c => !linkedIds.Contains(c.Id) && ClaimAccess.CanAct(user, c))
            .OrderByDescending(c => ClaimTextMatcher.CountNameParts(c, words))
            .ThenByDescending(c => c.CreatedAt)
            .Take(MaxAiCandidates)
            .ToList();

        var answers = await _assistant.FindMatchesAsync(text, shortlist, cancellationToken);

        // Small models sometimes name a claim the document never mentions. Only keep an answer the text backs
        // up with at least part of the claimant's name; the user still has to confirm it.
        return answers.Where(m => ClaimTextMatcher.CountNameParts(m.Claim, words) > 0).ToList();
    }
}
