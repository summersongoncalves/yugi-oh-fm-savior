using System.Text.Json.Serialization;

namespace YgoFm.Core;

/// <summary>
/// One entry of the fusion table: <see cref="Card1"/> combined with <see cref="Card2"/> produces
/// <see cref="Result"/>. The file stores each pair exactly once, on the card whose id is the lower
/// of the two, which is why the engine will have to index these both ways round.
/// </summary>
public sealed record FusionRecipe
{
    [JsonPropertyName("_card1")] public int Card1 { get; init; }
    [JsonPropertyName("_card2")] public int Card2 { get; init; }
    [JsonPropertyName("_result")] public int Result { get; init; }

    /// <summary>The material that is not <paramref name="known"/>.</summary>
    public int Other(int known) => known == Card1 ? Card2 : Card1;
}

/// <summary>A ritual summon: three specific materials, one fixed result.</summary>
public sealed record RitualRecipe
{
    public int RitualCard { get; init; }
    public int Card1 { get; init; }
    public int Card2 { get; init; }
    public int Card3 { get; init; }
    public int Result { get; init; }
}

/// <summary>One of the 722 cards, as stored in the database file.</summary>
public sealed record Card
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Raw numeric type code. Prefer <see cref="Kind"/>.</summary>
    public int Type { get; init; }

    public int Attack { get; init; }
    public int Defense { get; init; }
    public int Level { get; init; }

    /// <summary>
    /// The two Guardian Stars, 1..10, or 0 for none. Deliberately left as raw numbers:
    /// which planet each index means, and the table of which star beats which, have not
    /// been verified against the game yet, and a wrong mapping would silently produce bad
    /// advice. Naming them is a job for whoever builds the scoring unit.
    /// </summary>
    public int GuardianStarA { get; init; }

    public int GuardianStarB { get; init; }

    /// <summary>Every fusion recipe in which this card is the lower-numbered material.</summary>
    public FusionRecipe[] Fusions { get; init; } = [];

    /// <summary>
    /// For an equip card, the ids of the monsters it may be attached to. Empty for everything
    /// else. Only the 34 cards of type <see cref="CardType.Equip"/> have entries here — that
    /// exact correspondence is what pins down the type code.
    /// </summary>
    public int[] Equip { get; init; } = [];

    public RitualRecipe? Ritual { get; init; }

    public CardType Kind => (CardType)Type;

    public override string ToString() => $"#{Id} {Name}";
}
