namespace LlmShootout.Tests;

internal static class TempDirectory
{
    /// <summary>A new, empty directory below the system temp folder. The test that asks for it deletes it when done.</summary>
    public static string Create(string purpose) => Directory.CreateTempSubdirectory($"llm-shootout-{purpose}-").FullName;
}
