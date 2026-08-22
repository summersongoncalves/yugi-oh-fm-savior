namespace YgoFm.Core;

/// <summary>
/// Looks up monster-fusion results for pairs of cards. Monster-only and pair-only on purpose:
/// the database's Fusions table is actually broader than monster fusion — 27 equip cards and
/// 19 magic/trap cards carry their own non-empty Fusions entries, producing other equip or
/// ritual cards (e.g. Legendary Sword + Sword of Dark Destruction -> Kunai with Chain). That
/// contradicts the "equipment cards do not fuse" assumption in CLAUDE.md and is a real,
/// separate combination mechanic — worth its own feature later, but out of scope for "which
/// monsters in hand can fuse", which is all this class answers.
///
/// This also does not attempt the game's real sequential resolution (materials played left to
/// right, each new card fusing with whatever is already on the field) described in CLAUDE.md.
/// It only reports which unordered pairs among a set of cards have a catalogued result — a
/// building block for that engine, and enough on its own to check that recognition plus the
/// fusion table line up.
/// </summary>
public static class FusionFinder
{
    /// <summary>One possible fusion: two materials and what they produce.</summary>
    public sealed record FusionOption(Card MaterialA, Card MaterialB, Card Result);

    /// <summary>
    /// Every pair among <paramref name="hand"/> that both are monsters and have a catalogued
    /// fusion result. Hand is taken as a list of card ids (not a set) so two slots holding the
    /// same card are still considered as two separate materials.
    /// </summary>
    public static IReadOnlyList<FusionOption> PossibleFusions(this CardDatabase db, IReadOnlyList<int> hand)
    {
        var options = new List<FusionOption>();

        for (var i = 0; i < hand.Count; i++)
        {
            if (!db.TryGet(hand[i], out var a) || !a.Kind.IsMonster()) continue;

            for (var j = i + 1; j < hand.Count; j++)
            {
                if (!db.TryGet(hand[j], out var b) || !b.Kind.IsMonster()) continue;

                if (TryFind(db, a.Id, b.Id, out var result))
                    options.Add(new FusionOption(a, b, result));
            }
        }

        return options;
    }

    /// <summary>The fusion table stores each pair once, on the lower-numbered card.</summary>
    private static bool TryFind(CardDatabase db, int idA, int idB, out Card result)
    {
        var lower = Math.Min(idA, idB);
        var higher = Math.Max(idA, idB);
        var recipe = db[lower].Fusions.FirstOrDefault(f => f.Other(lower) == higher);

        if (recipe is not null)
        {
            result = db[recipe.Result];
            return true;
        }

        result = null!;
        return false;
    }
}
