namespace mano;

public class GameEvent
{
    public int Day { get; set; }
    public string Phase { get; set; } = "";
    public string Kind { get; set; } = "";          // "utterance" | "gm" | "system"
    public string SpeakerId { get; set; } = "";
    public string SpeakerName { get; set; } = "";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "";        // 感情（utterance 時）
    public DateTime Timestamp { get; set; } = DateTime.Now;
}