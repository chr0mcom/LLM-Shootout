namespace LlmShootout.Arena.Matches;

/// <summary>The spending limit was reached. The match stops cleanly.</summary>
public sealed class BudgetExceededException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BudgetExceededException(decimal spent, decimal limit)
        : base($"Cost limit reached: {spent:0.0000} USD spent, limit {limit:0.00} USD.")
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public BudgetExceededException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public BudgetExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public BudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Sums up the cost reported by the models and enforces the limit of one application run.</summary>
public sealed class CostMeter(decimal limitUsd)
{
    /// <summary>Total cost so far in USD.</summary>
    public decimal TotalUsd { get; private set; }

    /// <summary>Number of calls whose cost is unknown (for example after a timeout).</summary>
    public int UnknownCalls { get; private set; }

    /// <summary>The limit in USD.</summary>
    public decimal LimitUsd => limitUsd;

    /// <summary>Records the cost of a call. A null cost counts as unknown.</summary>
    public void Record(decimal? costUsd)
    {
        if (costUsd is { } cost)
        {
            TotalUsd += cost;
        }
        else
        {
            UnknownCalls++;
        }
    }

    /// <summary>Throws before a call if the limit is reached.</summary>
    public void EnsureWithinLimit()
    {
        if (TotalUsd >= limitUsd)
        {
            throw new BudgetExceededException(TotalUsd, limitUsd);
        }
    }
}
