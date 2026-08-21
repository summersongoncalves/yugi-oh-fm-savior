using System.Text.Json;

namespace YgoFm.Core;

/// <summary>
/// The 722 cards, loaded from the database file and addressable by id.
///
/// The file's own integrity was checked before it was trusted: ids run contiguously from 1 to
/// 722, no name is blank, no name is duplicated, and its 25,131 fusion entries reduce to exactly
/// 25,131 distinct unordered pairs with no pair ever mapping to two different results. That
/// last count is why <see cref="Load"/> can index fusions without worrying about conflicts.
/// </summary>
public sealed class CardDatabase
{
    public const int ExpectedCardCount = 722;

    private readonly Card[] _byId;

    private CardDatabase(Card[] byId) => _byId = byId;

    /// <summary>All cards, in id order, so index 0 is card 1.</summary>
    public IReadOnlyList<Card> Cards => _byId;

    public int Count => _byId.Length;

    /// <summary>The card with this id, where ids start at 1.</summary>
    public Card this[int id] => id >= 1 && id <= _byId.Length
        ? _byId[id - 1]
        : throw new ArgumentOutOfRangeException(nameof(id), id, $"Card ids run from 1 to {_byId.Length}.");

    public bool TryGet(int id, out Card card)
    {
        if (id >= 1 && id <= _byId.Length)
        {
            card = _byId[id - 1];
            return true;
        }

        card = null!;
        return false;
    }

    public static CardDatabase Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Card database not found at '{path}'. It ships in the repository's data folder.", path);

        using var stream = File.OpenRead(path);
        var cards = JsonSerializer.Deserialize<Card[]>(stream, Options)
                    ?? throw new InvalidDataException($"'{path}' did not contain a card array.");

        return FromCards(cards);
    }

    /// <summary>Build from an in-memory list. Kept separate from <see cref="Load"/> so tests need no file.</summary>
    public static CardDatabase FromCards(IEnumerable<Card> cards)
    {
        var ordered = cards.OrderBy(c => c.Id).ToArray();

        // The engine will index cards by position, so a gap or a repeat would silently shift
        // every card after it. Refusing to load is much better than mislabelling a whole hand.
        for (var i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].Id != i + 1)
                throw new InvalidDataException(
                    $"Card ids must run contiguously from 1; found {ordered[i].Id} where {i + 1} was expected.");
        }

        return new CardDatabase(ordered);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        // The file uses PascalCase for card fields but a leading underscore for the fusion
        // ones, which carry explicit attributes instead.
        PropertyNameCaseInsensitive = true,
    };
}
