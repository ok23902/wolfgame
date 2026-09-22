namespace mano;

public class CharacterObject : ManoObject
{
    public string Personality { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Status { get; set; } = "normal";     // 感情タグ
    public double X { get; set; }                       // 画面座標
    public double Y { get; set; }

    public List<string> Memory { get; set; } = new();
    public string Role { get; set; } = "";
    public bool IsAlive { get; set; } = true;
    public string PendingAction { get; set; } = "";
    public string PendingVote { get; set; } = "";
}

public class CharacterAIObject : CharacterObject
{
    public CharacterAIObject() => Attach(TraitRegistry.Get<AIActionTrait>());
}

public class PlayerObject : CharacterObject
{
    public PlayerObject() => Attach(TraitRegistry.Get<PlayerActionTrait>());
}