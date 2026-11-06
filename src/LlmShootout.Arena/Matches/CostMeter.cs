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
public sealed class CostMeter(decimal limitUsd, decimal unknownCallReserveUsd = 0.01m)
{
    /// <summary>Cost so far in USD: reported costs plus estimates from token counts and prices.</summary>
    public decimal TotalUsd { get; private set; }

    /// <summary>Number of calls whose cost is unknown (for example after a timeout).</summary>
    public int UnknownCalls { get; private set; }

    /// <summary>Number of calls without reported cost for which the cost was estimated from tokens and prices.</summary>
    public int EstimatedCalls { get; private set; }

    /// <summary>
    /// A fixed amount held back for each call with unknown cost, so that the limit still bites when OpenRouter stops
    /// reporting cost or when calls time out. It is not part of <see cref="TotalUsd"/>.
    /// </summary>
    public decimal ReservedUsd => UnknownCalls * unknownCallReserveUsd;

    /// <summary>The limit in USD.</summary>
    public decimal LimitUsd => limitUsd;

    /// <summary>Records the cost of a call. Without reported cost the estimate is used; without both the call counts as unknown.</summary>
    public void Record(decimal? costUsd, decimal? estimatedUsd = null)
    {
        if (costUsd is { } cost)
        {
            TotalUsd += cost;
        }
        else if (estimatedUsd is { } estimate)
        {
            TotalUsd += estimate;
            EstimatedCalls++;
        }
        else
        {
            UnknownCalls++;
        }
    }

    /// <summary>Throws before a call if the limit is reached.</summary>
    public void EnsureWithinLimit()
    {
        if (TotalUsd + ReservedUsd >= limitUsd)
        {
            throw new BudgetExceededException(TotalUsd + ReservedUsd, limitUsd);
        }
    }
}
