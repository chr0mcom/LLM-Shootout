using System.Text.RegularExpressions;

namespace LlmShootout.Tests;

public static partial class Snapshot
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static void Verify(string name, string actual)
    {
        var path = Path.Combine(RepositoryRoot, "tests", "LlmShootout.Tests", "Snapshots", name);
        var normalized = Normalize(actual);
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalized);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot '{name}' is missing. Run the tests once with UPDATE_SNAPSHOTS=1 to create it.");
        Assert.Equal(File.ReadAllText(path), normalized);
    }

    private static string Normalize(string text) =>
        VersionPattern().Replace(text.ReplaceLineEndings("\n").TrimEnd(), "VERSION (COMMIT)") + "\n";

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LlmShootout.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.]+)? \(([0-9a-f]{12}|unknown)\)")]
    private static partial Regex VersionPattern();
}
