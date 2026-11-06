using System.ComponentModel.DataAnnotations;

namespace LlmShootout.Web;

/// <summary>Settings of the web interface. The password comes from the environment and is never logged.</summary>
internal sealed class WebOptions
{
    /// <summary>The one password that opens the interface.</summary>
    [Required]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Where match directories are written and read.</summary>
    [Required]
    public string MatchesDirectory { get; set; } = "matches";

    /// <summary>Where the keys that sign the session cookie are kept, so that a restart does not log everyone out.</summary>
    [Required]
    public string DataDirectory { get; set; } = "data";

    /// <summary>Upper bound for the spending limit that a request may ask for.</summary>
    [Range(0.01, 100)]
    public decimal MaxCostCapUsd { get; set; } = 5m;

    /// <summary>Spending limit when a request names none.</summary>
    [Range(0.01, 100)]
    public decimal DefaultMaxCostUsd { get; set; } = 1m;

    /// <summary>Matches that may be started per day (UTC). Protects the subscription endpoints, whose calls cost nothing per call.</summary>
    [Range(1, 10000)]
    public int MaxMatchesPerDay { get; set; } = 30;

    /// <summary>Money that may be spent per day (UTC) across all matches.</summary>
    [Range(0.01, 1000)]
    public decimal MaxCostPerDayUsd { get; set; } = 10m;

    /// <summary>OpenRouter models that charge more than this per million output tokens are refused.</summary>
    [Range(0.01, 10000)]
    public decimal MaxOutputUsdPerMillion { get; set; } = 40m;

    /// <summary>Failed logins allowed per client address and minute.</summary>
    [Range(1, 100)]
    public int LoginAttemptsPerMinute { get; set; } = 5;

    /// <summary>The text of the record without the password.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"MatchesDirectory = {MatchesDirectory}, DataDirectory = {DataDirectory}");
        return true;
    }
}
