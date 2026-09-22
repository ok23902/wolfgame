namespace mano;

public class DeathRecord { public int Day; public string Phase = ""; public string Id = ""; public string Name = ""; public string Cause = ""; }
public class Divination { public int Day; public string SeerId = ""; public string TargetId = ""; public string TargetName = ""; public bool IsWolf; public bool Disclosed; }

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

    // ★ 追加
    public List<string> TurnOrder { get; set; } = new();
    private int _votesReceived;
    public int VotesReceived
    {
        get => _votesReceived;
        set => _votesReceived = value;
    }
    public int VotesExpected { get; set; }

    public event Action<GameEvent>? EventLogged;
    public event Action<string?>? ThinkingChanged;
    public event Action? VoteProgressChanged;
    public bool StartRequested { get; set; }

    internal void RaiseEventLogged(GameEvent e) => EventLogged?.Invoke(e);
    internal void RaiseThinkingChanged(string? id) => ThinkingChanged?.Invoke(id);
    internal void RaiseVoteProgressChanged() => VoteProgressChanged?.Invoke();
}