using Microsoft.Extensions.AI;

namespace mano;

public static class Llm
{
    private static IChatClient? _client;

    public static void Init(IChatClient client) => _client = client;

    // 単一行用（セリフなど）
    public static async Task<string> Ask(string system, string user)
    {
        var raw = await AskRaw(system, user);
        var line = raw.Trim().Split('\n').FirstOrDefault() ?? "";
        return line.Trim().Trim('"', '「', '」', '『', '』');
    }

    // 複数行用（投票理由＋投票先など）
    public static async Task<string> AskRaw(string system, string user)
    {
        if (_client == null)
            throw new InvalidOperationException("Llm.Init has not been called.");
        var messages = new List<ChatMessage>
    {
        new(ChatRole.System, system),
        new(ChatRole.User, user),
    };
        var response = await _client.GetResponseAsync(messages);
        return response.Text ?? "";
    }
}