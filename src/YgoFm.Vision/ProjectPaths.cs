namespace YgoFm.Vision;

/// <summary>
/// Finds the project's data folder whether running from an IDE, the command line,
/// or a published build, so data and exported crops land somewhere predictable.
/// </summary>
public static class ProjectPaths
{
    /// <summary>The repository root when running from a build output, else the executable's folder.</summary>
    public static string Root { get; } = FindRoot();

    public static string Data => EnsureDirectory(Path.Combine(Root, "data"));

    /// <summary>The 722 cards with their stats and fusion table.</summary>
    public static string CardsFile => Path.Combine(Data, "cards.json");

    /// <summary>
    /// The card artwork sheet: a 25-column grid of 102x96 tiles in card id order, which is
    /// the official reference the recogniser falls back to for any card not yet taught (see
    /// <see cref="Templates"/>).
    /// </summary>
    public static string CardArtFile => Path.Combine(Data, "card-art.png");

    /// <summary>
    /// Where the personal, self-taught card template library lives — one PNG per learned card,
    /// captured from this machine's own emulator rendering by pairing an OCR'd card name with
    /// the artwork crop under the current selection. Never shipped or committed; it is built up
    /// locally through play and is meaningless on anyone else's setup.
    /// </summary>
    public static string Templates => EnsureDirectory(Path.Combine(Data, "templates"));

    /// <summary>Scratch folder for exported crops, used to eyeball what the recogniser actually saw.</summary>
    public static string Captures => EnsureDirectory(Path.Combine(Data, "captures"));

    /// <summary>A fresh timestamped folder under <see cref="Captures"/>.</summary>
    public static string NewCaptureFolder(DateTime now) =>
        EnsureDirectory(Path.Combine(Captures, now.ToString("yyyy-MM-dd_HH-mm-ss")));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.sln").Any() || dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;
            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
