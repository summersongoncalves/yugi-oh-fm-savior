namespace YgoFm.Core;

/// <summary>
/// The 24 card types, matching the numeric codes in the card database file.
///
/// These names are not guesses. Two of them are pinned down by the data itself: every one
/// of the 34 cards with a non-null Equip field is <see cref="Equip"/> (23), and every one of
/// the 24 cards with a non-null Ritual field is <see cref="Ritual"/> (22). The remaining
/// codes were confirmed by reading off known cards — code 0 holds Blue-eyes White Dragon and
/// Baby Dragon, code 4 holds Battle Ox and Hitotsu-me Giant, code 21 holds Bear Trap, and so on.
/// </summary>
public enum CardType
{
    Dragon = 0,
    Spellcaster = 1,
    Zombie = 2,
    Warrior = 3,
    BeastWarrior = 4,
    Beast = 5,
    WingedBeast = 6,
    Fiend = 7,
    Fairy = 8,
    Insect = 9,
    Dinosaur = 10,
    Reptile = 11,
    Fish = 12,
    SeaSerpent = 13,
    Machine = 14,
    Thunder = 15,
    Aqua = 16,
    Pyro = 17,
    Rock = 18,
    Plant = 19,
    Magic = 20,
    Trap = 21,
    Ritual = 22,
    Equip = 23,
}

public static class CardTypeNames
{
    /// <summary>Display name, with the spaces and hyphens the game itself uses.</summary>
    public static string Label(this CardType type) => type switch
    {
        CardType.BeastWarrior => "Beast-Warrior",
        CardType.WingedBeast => "Winged Beast",
        CardType.SeaSerpent => "Sea Serpent",
        _ => type.ToString(),
    };

    /// <summary>
    /// True for the four types that are not monsters. Worth its own helper because the
    /// fusion engine treats them completely differently: they never occupy a monster slot.
    /// </summary>
    public static bool IsMonster(this CardType type) =>
        type is not (CardType.Magic or CardType.Trap or CardType.Ritual or CardType.Equip);
}
