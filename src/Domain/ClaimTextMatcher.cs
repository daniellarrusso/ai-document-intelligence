using System.Text.RegularExpressions;

namespace AiDocumentIntelligence.Domain;

// Finds evidence in a document's text that it belongs to a claim. Strongest evidence wins:
// the claim's reference, then its policy number, then its claimant's name (fuzzy).
public static partial class ClaimTextMatcher
{
    private const int MinPolicyNumberLength = 4;
    private const int MaxPolicyNumberLength = Claim.MaxPolicyNumberLength;

    // Bounds on candidate generation so a long, messy document can't produce an enormous database query.
    private const int MaxTokenLengthForPieces = 80;
    private const int MaxCutsPerToken = 12;
    private const int MaxPolicyCandidates = 2000;

    // The shape of a generated reference, CLM-yyyyMMdd-XXXXXXXX.
    [GeneratedRegex(@"CLM-\d{8}-[0-9A-F]{8}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferencePattern();

    // Runs of letters, digits and the separators policy numbers use.
    [GeneratedRegex(@"[A-Za-z0-9][A-Za-z0-9\-/]*[A-Za-z0-9]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();

    // Upper-cased, distinct references mentioned in the text.
    public static IReadOnlyList<string> FindReferences(string text) =>
        ReferencePattern().Matches(text).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();

    // Upper-cased, distinct strings that could be a policy number (long enough and containing a digit). Extracted
    // PDF text often loses its line breaks ("...1234567Claimant: Test"), so a token is also offered in the pieces
    // either side of each letter/digit switch ("1234567CLAIMANT" also yields "1234567").
    public static IReadOnlyList<string> FindPolicyCandidates(string text)
    {
        var candidates = new HashSet<string>();
        foreach (Match token in IdentifierPattern().Matches(text))
        {
            var value = token.Value.ToUpperInvariant();
            if (value.Length > MaxTokenLengthForPieces)
            {
                continue;
            }

            var cuts = new List<int> { 0 };
            for (var i = 1; i < value.Length && cuts.Count < MaxCutsPerToken; i++)
            {
                if (IsSwitchBetweenLettersAndDigits(value[i - 1], value[i])) cuts.Add(i);
            }
            cuts.Add(value.Length);

            for (var a = 0; a < cuts.Count - 1; a++)
            {
                for (var b = a + 1; b < cuts.Count; b++)
                {
                    var piece = value[cuts[a]..cuts[b]].Trim('-', '/');
                    if (piece.Length is >= MinPolicyNumberLength and <= MaxPolicyNumberLength && piece.Any(char.IsDigit))
                    {
                        candidates.Add(piece);
                    }
                }
            }
        }

        return candidates.Take(MaxPolicyCandidates).ToList();
    }

    // True if the identifier appears as a token of its own. It must not be part of a longer run of digits or of
    // letters ("1234567" is not in "12345678"), but a switch between the two counts as a boundary, because
    // glued-together lines ("number1234567Claimant") are common in extracted PDF text.
    public static bool ContainsIdentifier(string text, string identifier)
    {
        if (identifier.Length == 0)
        {
            return false;
        }

        var from = 0;
        while (from <= text.Length - identifier.Length)
        {
            var index = text.IndexOf(identifier, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var end = index + identifier.Length;
            var startsClean = index == 0 || IsBoundary(text[index - 1], identifier[0]);
            var endsClean = end == text.Length || IsBoundary(text[end], identifier[^1]);
            if (startsClean && endsClean)
            {
                return true;
            }

            from = index + 1;
        }

        return false;
    }

    private static bool IsBoundary(char neighbour, char edge) =>
        !char.IsLetterOrDigit(neighbour) || IsSwitchBetweenLettersAndDigits(neighbour, edge);

    private static bool IsSwitchBetweenLettersAndDigits(char a, char b) =>
        (char.IsDigit(a) && char.IsLetter(b)) || (char.IsLetter(a) && char.IsDigit(b));

    // The strongest evidence that the text belongs to the claim, or null if there is none.
    public static ClaimMatch? Match(Claim claim, string text)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(text);

        if (FindReferences(text).Contains(claim.Reference.ToUpperInvariant()))
        {
            return new ClaimMatch(claim, MatchMethod.Reference, $"The document quotes the claim reference {claim.Reference}.");
        }

        if (claim.PolicyNumber.Length >= MinPolicyNumberLength && ContainsIdentifier(text, claim.PolicyNumber))
        {
            return new ClaimMatch(claim, MatchMethod.PolicyNumber, $"The document mentions policy number {claim.PolicyNumber}.");
        }

        return MatchName(claim, text, WordsOf(text));
    }

    // Distinct lower-cased words, for repeated name matching against one document.
    public static HashSet<string> WordsOf(string text) =>
        WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToHashSet();

    // How many parts of the claimant's name appear in the words (exactly or nearly). Used to rank AI candidates.
    public static int CountNameParts(Claim claim, HashSet<string> words) =>
        NameParts(claim.ClaimantName).Count(part => FindWord(part, words) != null);

    // Every part of the claimant's name must be present, allowing for small typos. Names with a single part are
    // too ambiguous to match on their own, so they are left to the AI step and to manual linking.
    private static ClaimMatch? MatchName(Claim claim, string text, HashSet<string> words)
    {
        var parts = NameParts(claim.ClaimantName);
        if (parts.Count < 2)
        {
            return null;
        }

        var approximate = false;
        foreach (var part in parts)
        {
            var found = FindWord(part, words);
            if (found == null)
            {
                return MatchInitialAndSurname(claim, parts, text);
            }
            approximate |= found != part;
        }

        return new ClaimMatch(
            claim,
            MatchMethod.ClaimantName,
            approximate
                ? $"The document contains a name close to the claimant \"{claim.ClaimantName}\"."
                : $"The document mentions the claimant \"{claim.ClaimantName}\".");
    }

    // "K. Johnson" for "Katherine Johnson": the surname exactly, preceded by the first name's initial.
    private static ClaimMatch? MatchInitialAndSurname(Claim claim, List<string> parts, string text)
    {
        var surname = parts[^1];
        if (surname.Length < 3)
        {
            return null;
        }

        // Shown as written on the claim, e.g. "K. Johnson", not as lower-cased for comparison.
        var words = WordPattern().Matches(claim.ClaimantName).Select(m => m.Value).Where(w => w.Length >= 2).ToList();
        var display = $"{char.ToUpperInvariant(words[0][0])}. {words[^1]}";

        var pattern = $@"\b{Regex.Escape(parts[0][..1])}\.?\s+{Regex.Escape(surname)}\b";
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                ? new ClaimMatch(claim, MatchMethod.ClaimantName, $"The document mentions \"{display}\", which could be the claimant \"{claim.ClaimantName}\".")
                : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static List<string> NameParts(string name) =>
        WordPattern().Matches(name).Select(m => m.Value.ToLowerInvariant()).Where(p => p.Length >= 2).ToList();

    // The word in the set equal or near to the part. Longer words tolerate more typos; short ones none.
    private static string? FindWord(string part, HashSet<string> words)
    {
        if (words.Contains(part))
        {
            return part;
        }

        var allowed = part.Length >= 7 ? 2 : part.Length >= 5 ? 1 : 0;
        if (allowed == 0)
        {
            return null;
        }

        // A typo is unlikely in the first letter, and checking it first keeps this cheap on long documents.
        return words.FirstOrDefault(w =>
            w[0] == part[0] && Math.Abs(w.Length - part.Length) <= allowed && EditDistance(w, part, allowed) <= allowed);
    }

    // Levenshtein distance, giving up early (returning max + 1) once it must exceed max.
    private static int EditDistance(string a, string b, int max)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max) return max + 1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
