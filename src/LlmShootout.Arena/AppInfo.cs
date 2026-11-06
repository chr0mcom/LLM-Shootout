using System.Reflection;

namespace LlmShootout.Arena;

/// <summary>Version and git commit of the running application, as stamped at build time.</summary>
public static class AppInfo
{
    /// <summary>The version and the short commit hash (or <c>unknown</c>).</summary>
    public static (string Version, string Commit) Current { get; } = Read();

    private static (string Version, string Commit) Read()
    {
        var text = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? (text, "unknown") : (text[..plus], text[(plus + 1)..]);
    }
}
