namespace YgoFm.Core;

/// <summary>
/// Resolves OCR'd text against the 722 known card names. The OCR reading is not expected to be
/// pixel-perfect — measured against real captures, it landed exactly on the name most of the
/// time and one character off the rest — so this matches by edit distance rather than requiring
/// an exact string.
/// </summary>
public static class CardNameMatcher
{
    /// <summary>
    /// Above this distance, a reading is treated as noise rather than a near-miss. 2 was chosen
    /// because every OCR error seen so far was a single substituted character, and requiring
    /// the winner to be strictly closer than every other name (see <see cref="Match"/>) is what
    /// actually guards against a bad read colliding with some other card's name — the distance
    /// cap alone is just there to reject blank or garbage readings quickly.
    /// </summary>
    public const int MaxDistance = 2;

    public sealed record MatchResult(Card? Card, int Distance)
    {
        public bool Confident => Card is not null;
    }

    /// <summary>
    /// The single card whose name is closest to <paramref name="text"/> by edit distance, but
    /// only when that closeness is unambiguous: within <see cref="MaxDistance"/>, and strictly
    /// closer than every other card's name. A tie, or a name that is merely the least-bad among
    /// several similarly-distant options, is not a reading to teach a template from — it is
    /// reported as unmatched instead of guessed at.
    /// </summary>
    public static MatchResult Match(CardDatabase db, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new MatchResult(null, int.MaxValue);

        Card? best = null;
        var bestDistance = int.MaxValue;
        var bestCount = 0;

        foreach (var card in db.Cards)
        {
            var distance = EditDistance(text, card.Name);
            if (distance < bestDistance)
            {
                best = card;
                bestDistance = distance;
                bestCount = 1;
            }
            else if (distance == bestDistance)
            {
                bestCount++;
            }
        }

        return bestDistance <= MaxDistance && bestCount == 1
            ? new MatchResult(best, bestDistance)
            : new MatchResult(null, bestDistance);
    }

    /// <summary>Levenshtein distance, case-insensitive.</summary>
    private static int EditDistance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
