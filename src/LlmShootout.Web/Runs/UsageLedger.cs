using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Runs;

/// <summary>
/// Counts what was started today, in a small file in the data folder, so that a restart does not reset it. The per-match
/// cap cannot protect subscription endpoints (their calls cost nothing per call), so the number of matches per day is
/// limited as well, and the money spent per day.
/// </summary>
internal sealed class UsageLedger(IOptions<WebOptions> web, TimeProvider time)
{
    private readonly object _gate = new();

    public void EnsureAllowed()
    {
        lock (_gate)
        {
            var today = Read();
            if (today.Matches >= web.Value.MaxMatchesPerDay)
            {
                throw new StartRejectedException($"The limit of {web.Value.MaxMatchesPerDay} matches per day is reached. It resets at midnight UTC.", StatusCodes.Status429TooManyRequests);
            }

            if (today.CostUsd >= web.Value.MaxCostPerDayUsd)
            {
                throw new StartRejectedException($"The limit of {web.Value.MaxCostPerDayUsd} USD per day is reached. It resets at midnight UTC.", StatusCodes.Status429TooManyRequests);
            }
        }
    }

    public void RecordStart()
    {
        lock (_gate)
        {
            var today = Read();
            Write(today with { Matches = today.Matches + 1 });
        }
    }

    public void RecordCost(decimal costUsd)
    {
        lock (_gate)
        {
            var today = Read();
            Write(today with { CostUsd = today.CostUsd + costUsd });
        }
    }

    private string Path => System.IO.Path.Combine(web.Value.DataDirectory, "usage.json");

    private string Today => time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Day Read()
    {
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<Day>(File.ReadAllText(Path)) is { } stored && stored.Date == Today)
            {
                return stored;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Day(Today, 0, 0m);
        }

        return new Day(Today, 0, 0m);
    }

    private void Write(Day day)
    {
        Directory.CreateDirectory(web.Value.DataDirectory);
        File.WriteAllText(Path, JsonSerializer.Serialize(day));
    }

    private sealed record Day(string Date, int Matches, decimal CostUsd);
}
