namespace YgoFm.Vision;

/// <summary>
/// Finds the project's data folder whether running from an IDE, the command line,
/// or a published build, so calibration files land somewhere predictable.
/// </summary>
public static class ProjectPaths
{
    /// <summary>The repository root when running from a build output, else the executable's folder.</summary>
    public static string Root { get; } = FindRoot();

    public static string Data => EnsureDirectory(Path.Combine(Root, "data"));

    /// <summary>Where the cut tool saves its calibration by default.</summary>
    public static string LayoutFile => Path.Combine(Data, "layout.json");

    /// <summary>Scratch folder for exported crops, used to eyeball whether a calibration is right.</summary>
    public static string Captures => EnsureDirectory(Path.Combine(Data, "captures"));

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
