namespace mano;

public class ChoiceOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ImageKey { get; set; }
}

public class ChoiceRequest
{
    public string Prompt { get; set; } = "";
    public List<ChoiceOption> Options { get; set; } = new();
    public bool AllowSkip { get; set; }
}