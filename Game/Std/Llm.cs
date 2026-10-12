using Microsoft.Extensions.AI;

namespace mano;

public static class Llm
{
    public class LlmRateLimitException : Exception
    {
        public LlmRateLimitException() : base("Rate limited") { }
        public LlmRateLimitException(Exception inner) : base("Rate limited", inner) { }
    }

    private static IChatClient? _client;
    public static void Init(IChatClient client) => _client = client;

    public static async Task<string> Ask(string system, string user)
    {
        var raw = await AskRaw(system, user);
        var line = raw.Trim().Split('\n').FirstOrDefault() ?? "";
        return line.Trim().Trim('"', '「', '」', '『', '』');
    }

    public static async Task<string> AskRaw(string system, string user)
    {
        if (_client == null)
            throw new InvalidOperationException("Llm.Init has not been called.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, user),
        };

        try
        {
            var response = await _client.GetResponseAsync(messages);
            return response.Text ?? "";
        }
        catch (Exception ex) when (IsRateLimit(ex))
        {
            System.Diagnostics.Debug.WriteLine($"[LLM] rate limit detected: {ex.Message}");
            throw new LlmRateLimitException(ex);
        }
    }

    private static bool IsRateLimit(Exception ex)
    {
        var msg = ex.ToString();
        return msg.Contains("429") || msg.Contains("TooManyRequests")
            || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("ResourceExhausted", StringComparison.OrdinalIgnoreCase);
    }
}