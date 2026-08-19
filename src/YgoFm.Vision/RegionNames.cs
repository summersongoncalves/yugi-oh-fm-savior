namespace YgoFm.Vision;

/// <summary>
/// The named parts of the screen the tool needs to find. Calibrated once by the user
/// with the cut tool; after that these names are how every other component asks for pixels.
/// </summary>
public static class RegionNames
{
    /// <summary>
    /// The game image itself, inside the captured frame — everything else is measured
    /// relative to this, so resizing the emulator window only invalidates this one box.
    /// </summary>
    public const string Viewport = "viewport";

    /// <summary>The five card slots in the player's hand, left to right.</summary>
    public static readonly string[] Hand = ["hand1", "hand2", "hand3", "hand4", "hand5"];

    /// <summary>
    /// The panel where the game prints the name of the card under the cursor.
    /// Read with text recognition, and used to teach the artwork matcher.
    /// </summary>
    public const string CardName = "cardName";

    /// <summary>Everything the cut tool asks the user to draw, in the order it asks.</summary>
    public static IReadOnlyList<string> CalibrationOrder { get; } =
        [Viewport, .. Hand, CardName];

    /// <summary>Short hint shown in the cut tool while drawing each region.</summary>
    public static string Describe(string region) => region switch
    {
        Viewport => "Drag a box around the game picture only — exclude window borders, menus and black bars.",
        CardName => "Drag a box around the panel where the card's name is printed.",
        _ when region.StartsWith("hand") =>
            $"Drag a box tightly around hand card #{region[^1]} (the artwork, not its frame).",
        _ => "Drag a box around this region."
    };
}
