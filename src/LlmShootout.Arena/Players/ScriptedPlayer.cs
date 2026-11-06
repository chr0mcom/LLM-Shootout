using LlmShootout.Arena.Identity;

namespace LlmShootout.Arena.Players;

/// <summary>
/// A player that returns prepared answers in order. Used in tests to force every case: invalid JSON, refusals,
/// timeouts, long messages. When the script is empty the call fails with a technical failure, which aborts the match cleanly.
/// </summary>
public sealed class ScriptedPlayer : IPlayer
{
    private readonly Queue<Func<PlayerCall, PlayerReply>> _script = new();
    private readonly List<PlayerCall> _calls = [];

    /// <summary>Creates the player. <paramref name="model"/> is the identity it claims, or null.</summary>
    public ScriptedPlayer(string name = "scripted", ModelIdentity? model = null)
    {
        Name = name;
        Model = model;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ModelIdentity? Model { get; }

    /// <summary>All calls received so far.</summary>
    public IReadOnlyList<PlayerCall> Calls => _calls;

    /// <summary>Queues a plain answer.</summary>
    public ScriptedPlayer Reply(string content, string? reasoning = null, ModelCallInfo? info = null)
    {
        _script.Enqueue(_ => new PlayerReply(content, reasoning, info));
        return this;
    }

    /// <summary>Queues an answer computed from the call.</summary>
    public ScriptedPlayer Reply(Func<PlayerCall, PlayerReply> factory)
    {
        _script.Enqueue(factory);
        return this;
    }

    /// <summary>Queues a timeout.</summary>
    public ScriptedPlayer Timeout()
    {
        _script.Enqueue(_ => throw new PlayerFailureException("timeout", isTimeout: true));
        return this;
    }

    /// <summary>Queues a technical failure that aborts the match.</summary>
    public ScriptedPlayer Fail(string message)
    {
        _script.Enqueue(_ => throw new PlayerFailureException(message));
        return this;
    }

    /// <inheritdoc />
    public Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _calls.Add(request);
        if (!_script.TryDequeue(out var next))
        {
            throw new PlayerFailureException($"Script of '{Name}' is empty at call {_calls.Count}.");
        }

        return Task.FromResult(next(request));
    }
}
