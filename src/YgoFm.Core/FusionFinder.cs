namespace YgoFm.Core;

/// <summary>
/// Finds monster-fusion chains achievable from a hand: two or more monster materials, played in
/// some order, where every play in that order successfully fuses with whatever is already on
/// the field. This is the game's real mechanic described in CLAUDE.md — materials are played
/// left to right, and each new card either fuses with the current field monster or, if the
/// table has no entry for that pair, replaces it outright and the earlier monster is lost — so
/// which orderings are even worth playing depends on the whole sequence, not just any one pair.
///
/// A 5-monster hand has at most 320 orderings of two or more cards (sizes 2 through 5), which is
/// cheap enough to brute-force exhaustively rather than search cleverly, exactly as CLAUDE.md
/// calls for.
///
/// Monster-only on purpose: the database's Fusions table is actually broader than monster
/// fusion — 27 equip cards and 19 magic/trap cards carry their own non-empty Fusions entries,
/// producing other equip or ritual cards (e.g. Legendary Sword + Sword of Dark Destruction ->
/// Kunai with Chain). That contradicts the "equipment cards do not fuse" assumption in
/// CLAUDE.md and is a real, separate combination mechanic — worth its own feature later, but
/// out of scope for "which monster fusions can this hand make."
/// </summary>
public static class FusionFinder
{
    /// <summary>
    /// One achievable chain: the materials in the order they would need to be played, and what
    /// they end up producing. Every step from the second material onward was a successful
    /// fusion — nothing in <see cref="Materials"/> was discarded along the way.
    /// </summary>
    public sealed record FusionChain(IReadOnlyList<Card> Materials, Card Result);

    /// <summary>
    /// Every ordering of two or more monsters in <paramref name="hand"/> that fuses cleanly all
    /// the way through. Hand is taken as a list of card ids by slot (not a set), so two slots
    /// holding the same card are still two separate materials, and swapping which physical copy
    /// is played first does not produce a duplicate entry.
    /// </summary>
    public static IReadOnlyList<FusionChain> PossibleChains(this CardDatabase db, IReadOnlyList<int> hand)
    {
        var monsterSlots = new List<int>();
        for (var i = 0; i < hand.Count; i++)
        {
            if (db.TryGet(hand[i], out var card) && card.Kind.IsMonster())
                monsterSlots.Add(i);
        }

        var seen = new HashSet<string>();
        var chains = new List<FusionChain>();

        foreach (var subset in Subsets(monsterSlots))
        {
            if (subset.Count < 2) continue;

            foreach (var order in Permutations(subset))
            {
                Card? field = null;
                var clean = true;

                foreach (var slot in order)
                {
                    var card = db[hand[slot]];
                    if (field is null) { field = card; continue; }

                    if (TryFind(db, field.Id, card.Id, out var result))
                        field = result;
                    else
                    {
                        clean = false;
                        break;
                    }
                }

                if (!clean) continue;

                // Two slots holding identical cards produce the identical id sequence and
                // result no matter which physical copy leads — collapse those rather than
                // showing the same chain twice.
                var key = string.Join(",", order.Select(slot => hand[slot])) + "|" + field!.Id;
                if (!seen.Add(key)) continue;

                chains.Add(new FusionChain(order.Select(slot => db[hand[slot]]).ToList(), field));
            }
        }

        return chains;
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

    private static IEnumerable<List<int>> Subsets(List<int> items)
    {
        var n = items.Count;
        for (var mask = 1; mask < (1 << n); mask++)
        {
            var subset = new List<int>();
            for (var i = 0; i < n; i++)
                if ((mask & (1 << i)) != 0) subset.Add(items[i]);
            yield return subset;
        }
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count == 0)
        {
            yield return [];
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = new List<int>(items);
            rest.RemoveAt(i);

            foreach (var tail in Permutations(rest))
            {
                var full = new List<int> { items[i] };
                full.AddRange(tail);
                yield return full;
            }
        }
    }
}
