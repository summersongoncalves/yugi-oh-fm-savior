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

    /// <summary>How many past capture sessions are kept on disk before older ones are pruned —
    /// see <see cref="NewCaptureFolder"/>.</summary>
    private const int MaxKeptCaptures = 10;

    /// <summary>
    /// A fresh timestamped folder under <see cref="Captures"/>. One of these is created every
    /// time the user clicks Iniciar (see MainWindow.StartObserving) — they are throwaway
    /// verification artifacts by design (the "eyeball what was actually selected" habit from
    /// CLAUDE.md), never referenced again once a session is confirmed working, so with no
    /// pruning they just accumulate on disk indefinitely across every test run. This keeps only
    /// the most recent <see cref="MaxKeptCaptures"/>, deleting older ones right after the new
    /// folder is created.
    /// </summary>
    public static string NewCaptureFolder(DateTime now)
    {
        var folder = EnsureDirectory(Path.Combine(Captures, now.ToString("yyyy-MM-dd_HH-mm-ss")));
        PruneOldCaptures();
        return folder;
    }

    private static void PruneOldCaptures()
    {
        // The timestamped folder names sort chronologically as plain strings (yyyy-MM-dd_HH-mm-ss
        // is a sortable format by construction), so this needs no date parsing to know which
        // folders are oldest.
        var folders = Directory.GetDirectories(Captures).OrderBy(f => f).ToList();

        for (var i = 0; i < folders.Count - MaxKeptCaptures; i++)
        {
            try
            {
                Directory.Delete(folders[i], recursive: true);
            }
            catch
            {
                // Best-effort: a folder locked by another process (e.g. an image still open in
                // a viewer) should never stop the app from starting a new capture.
            }
        }
    }

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
