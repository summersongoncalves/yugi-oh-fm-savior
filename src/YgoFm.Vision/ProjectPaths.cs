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
    /// The card artwork sheet: a 25-column grid of 40x32 tiles in card id order, which is
    /// the reference the recogniser matches captured card art against.
    /// </summary>
    public static string CardArtFile => Path.Combine(Data, "card-art.png");

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
