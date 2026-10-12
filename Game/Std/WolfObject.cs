namespace mano;

public class DeathRecord
{
    public int Day;
    public string Phase = "";
    public string Id = "";
    public string Name = "";
    public string Cause = "";
}

public class Divination
{
    public int Day;
    public string SeerId = "";
    public string TargetId = "";
    public string TargetName = "";
    public bool IsWolf;
    public bool Disclosed;
}

public class WolfObject : ManoObject
{
    public WolfObject() => Attach(TraitRegistry.Get<WolfTrait>());

    public Dictionary<string, string> Roles { get; set; } = new();
    public List<DeathRecord> Deaths { get; set; } = new();
    public List<Divination> Divinations { get; set; } = new();
    public List<GameEvent> EventLog { get; set; } = new();

    public string Phase { get; set; } = "Setup";
    public int Day { get; set; }
    public bool IsGameOver { get; set; }
    public string Winner { get; set; } = "";

    public string? ThinkingId { get; set; }
    public bool StartRequested { get; set; }

    public List<string> TurnOrder { get; set; } = new();
    public int VotesReceived { get; set; }
    public int VotesExpected { get; set; }

    public event Action<GameEvent>? EventLogged;
    public event Action<string?>? ThinkingChanged;
    public event Action? VoteProgressChanged;
    public event Action<string, string>? CharacterStatusChanged;
    public event Action<string, string, int, int, double>? RateLimitWaiting;
    public event Action<string, string>? RateLimitExhausted;
    public event Action? RateLimitRecovered;

    public TaskCompletionSource<bool>? RateLimitResume;

    internal void RaiseEventLogged(GameEvent e) => EventLogged?.Invoke(e);
    internal void RaiseThinkingChanged(string? id) => ThinkingChanged?.Invoke(id);
    internal void RaiseVoteProgressChanged() => VoteProgressChanged?.Invoke();
    internal void RaiseCharacterStatusChanged(string id, string status)
        => CharacterStatusChanged?.Invoke(id, status);
    internal void RaiseRateLimitWaiting(string id, string name, int attempt, int max, double waitSec)
        => RateLimitWaiting?.Invoke(id, name, attempt, max, waitSec);
    internal void RaiseRateLimitExhausted(string id, string name)
        => RateLimitExhausted?.Invoke(id, name);
    internal void RaiseRateLimitRecovered() => RateLimitRecovered?.Invoke();

    public void ResumeRateLimit(bool resume) => RateLimitResume?.TrySetResult(resume);
}