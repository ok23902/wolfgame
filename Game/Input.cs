namespace mano;

public static class Input
{
    private static Func<string, Task<string>>? _text;
    private static Func<ChoiceRequest, Task<string?>>? _choice;

    public static void SetHandlers(
        Func<string, Task<string>> text,
        Func<ChoiceRequest, Task<string?>> choice)
    {
        _text = text;
        _choice = choice;
    }

    public static Task<string> RequestText(string prompt)
        => _text?.Invoke(prompt) ?? Task.FromResult("");

    public static Task<string?> RequestChoice(ChoiceRequest req)
        => _choice?.Invoke(req) ?? Task.FromResult<string?>(null);
}