using System.Diagnostics;
using System.Text;

namespace mano;

public class WolfTrait : ManoTrait<WolfObject>
{
    private static readonly object _logLock = new();
    private static readonly Random Rng = new();

    // ---------- ゲーム開始 ----------

    public async Task StartGame(WolfObject self)
    {
        var chars = self.Direct<CharacterObject>().ToList();
        if (chars.Count < 3) throw new InvalidOperationException("Not enough players.");

        // ★ 先に前回の記憶を要約（あれば）
        await SummarizeAllMemoriesAsync(self);

        self.Roles.Clear();
        self.Deaths.Clear();
        self.Divinations.Clear();
        self.EventLog.Clear();
        self.TurnOrder.Clear();
        self.VotesReceived = 0;
        self.VotesExpected = 0;
        self.Day = 1;
        self.Phase = "Day";
        self.IsGameOver = false;
        self.Winner = "";
        self.ThinkingId = null;

        foreach (var c in chars)
        {
            c.IsAlive = true;
            c.Role = "";
            // ★ c.Memory.Clear() は削除（要約で置換済み）
            c.PendingAction = "";
            c.PendingVote = "";
            c.Status = "normal";
        }

        // 役職配布
        var shuffled = chars.OrderBy(_ => Rng.Next()).ToList();
        var wolfCount = Math.Max(1, chars.Count / 4);
        for (int i = 0; i < shuffled.Count; i++)
        {
            string role = i < wolfCount ? "wolf" : i == wolfCount ? "seer" : "villager";
            self.Roles[shuffled[i].Id] = role;
            shuffled[i].Role = role;
        }

        self.TurnOrder = chars.OrderBy(_ => Rng.Next()).Select(c => c.Id).ToList();

        Log(self, new GameEvent { Day = 1, Phase = "Setup", Kind = "system", SpeakerName = "SYSTEM", Text = "ゲーム開始" });

        foreach (var c in chars)
        {
            if (c is PlayerObject)
            {
                Log(self, new GameEvent { Day = 1, Phase = "Setup", Kind = "system", SpeakerName = "SYSTEM", Text = $"あなたの役職: {RoleJa(c.Role)}" });
            }
        }
    }

    // ---------- ターン進行 ----------

    public async Task ProcessTurn(WolfObject self)
    {
        if (self.IsGameOver) return;
        if (self.Phase == "Day") await ProcessDay(self);
        else if (self.Phase == "Night") await ProcessNight(self);
    }

    private async Task ProcessDay(WolfObject self)
    {
        Log(self, new GameEvent
        {
            Day = self.Day,
            Phase = "Day",
            Kind = "system",
            SpeakerName = "SYSTEM",
            Text = $"Day {self.Day} - 議論"
        });

        foreach (var c in OrderedByTurn(self, self.Direct<CharacterObject>().Where(x => x.IsAlive)))
        {
            await Speak(self, c, "DayDiscuss");

            if (c.Role == "seer")
            {
                foreach (var d in self.Divinations.Where(d => d.SeerId == c.Id && !d.Disclosed))
                {
                    if (c.PendingAction.Contains(d.TargetName) &&
                        (c.PendingAction.Contains("人狼") || c.PendingAction.Contains("人間")))
                    {
                        d.Disclosed = true;
                        Log(self, new GameEvent
                        {
                            Day = self.Day,
                            Phase = "Day",
                            Kind = "gm",
                            SpeakerName = "GM",
                            Text = $"占い結果公表: {d.TargetName} = {(d.IsWolf ? "人狼" : "人間")}"
                        });
                    }
                }
            }
        }

        if (self.Day > 1)
        {
            await ProcessVote(self);
        }
        else
        {
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = "Day",
                Kind = "gm",
                SpeakerName = "GM",
                Text = "初日のため投票はありません。"
            });
        }

        if (CheckWin(self)) return;
        self.Phase = "Night";
    }

    private async Task ProcessVote(WolfObject self)
    {
        Log(self, new GameEvent
        {
            Day = self.Day,
            Phase = "Day",
            Kind = "system",
            SpeakerName = "SYSTEM",
            Text = "投票"
        });

        foreach (var c in self.Direct<CharacterObject>())
            c.PendingVote = "";

        var voters = self.Direct<CharacterObject>().Where(x => x.IsAlive).ToList();
        self.VotesReceived = 0;
        self.VotesExpected = voters.Count;
        self.RaiseVoteProgressChanged();

        int received = 0;
        await Task.WhenAll(voters.Select(async c =>
        {
            await c.Exec("DayVote");
            var n = Interlocked.Increment(ref received);
            self.VotesReceived = n;
            self.RaiseVoteProgressChanged();
        }));

        foreach (var c in self.Direct<CharacterObject>().Where(x => x.IsAlive))
        {
            var line = string.IsNullOrEmpty(c.PendingVote)
                ? $"{c.Name} → 棄権"
                : $"{c.Name} → {self.Direct<CharacterObject>().FirstOrDefault(t => t.Id == c.PendingVote)?.Name ?? c.PendingVote} に投票";
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = "Day",
                Kind = "gm",
                SpeakerName = "GM",
                Text = line
            });
        }

        var tally = new Dictionary<string, int>();
        foreach (var c in self.Direct<CharacterObject>().Where(x => x.IsAlive))
            if (!string.IsNullOrEmpty(c.PendingVote))
                tally[c.PendingVote] = tally.GetValueOrDefault(c.PendingVote) + 1;

        if (tally.Count == 0)
        {
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = "Day",
                Kind = "gm",
                SpeakerName = "GM",
                Text = "票が集まらず、追放なし。"
            });
            return;
        }

        var max = tally.Values.Max();
        var top = tally.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();

        if (top.Count == 1)
        {
            var victim = self.Direct<CharacterObject>().First(c => c.Id == top[0]);
            victim.IsAlive = false;
            self.Deaths.Add(new DeathRecord
            {
                Day = self.Day,
                Phase = "Day",
                Id = victim.Id,
                Name = victim.Name,
                Cause = "Vote"
            });
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = "Day",
                Kind = "gm",
                SpeakerName = "GM",
                Text = $"{victim.Name} が追放された。（{max}票）"
            });
            NotifyDeath(victim);
        }
        else
        {
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = "Day",
                Kind = "gm",
                SpeakerName = "GM",
                Text = $"同票（{max}票×{top.Count}名）のため、追放なし。"
            });
        }
    }

    private async Task ProcessNight(WolfObject self)
    {
        Log(self, new GameEvent
        {
            Day = self.Day,
            Phase = "Night",
            Kind = "system",
            SpeakerName = "SYSTEM",
            Text = $"Night {self.Day}"
        });

        foreach (var c in self.Direct<CharacterObject>())
            c.PendingAction = "";

        foreach (var c in self.Direct<CharacterObject>().Where(x => x.IsAlive).ToList())
            await c.Exec("Night");

        var wolfChoice = self.Direct<CharacterObject>()
            .FirstOrDefault(c => c.IsAlive && c.Role == "wolf" && !string.IsNullOrEmpty(c.PendingAction));

        if (wolfChoice != null)
        {
            var target = self.Direct<CharacterObject>()
                .FirstOrDefault(c => c.Id == wolfChoice.PendingAction);
            if (target != null)
            {
                target.IsAlive = false;
                self.Deaths.Add(new DeathRecord
                {
                    Day = self.Day,
                    Phase = "Night",
                    Id = target.Id,
                    Name = target.Name,
                    Cause = "Attack"
                });
                Log(self, new GameEvent
                {
                    Day = self.Day,
                    Phase = "Night",
                    Kind = "gm",
                    SpeakerName = "GM",
                    Text = $"昨夜、{target.Name} が襲撃された。"
                });
                NotifyDeath(target);
            }
        }

        if (CheckWin(self)) return;
        self.Phase = "Day";
        self.Day++;
    }

    // ---------- 勝敗 ----------

    private bool CheckWin(WolfObject self)
    {
        var alive = self.Direct<CharacterObject>().Where(c => c.IsAlive).ToList();
        var wolves = alive.Count(c => c.Role == "wolf");
        var others = alive.Count - wolves;

        if (wolves == 0)
        {
            self.IsGameOver = true;
            self.Winner = "villager";
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = self.Phase,
                Kind = "system",
                SpeakerName = "SYSTEM",
                Text = "村人陣営の勝利"
            });
            AppendGameResult(self);   // ★ 勝敗確定時に結果を記憶へ
            return true;
        }

        if (wolves >= others)
        {
            self.IsGameOver = true;
            self.Winner = "wolf";
            Log(self, new GameEvent
            {
                Day = self.Day,
                Phase = self.Phase,
                Kind = "system",
                SpeakerName = "SYSTEM",
                Text = "人狼陣営の勝利"
            });
            AppendGameResult(self);   // ★
            return true;
        }

        return false;
    }

    /// <summary>
    /// ゲーム終了時に、全キャラの Memory に結果（役職一覧）を追加する。
    /// Memory にはこの時点で会話ログも残っているので、後日談として成立する。
    /// 次のゲーム開始時に SummarizeAllMemoriesAsync が全記憶を要約する。
    /// </summary>
    private static void AppendGameResult(WolfObject self)
    {
        var winnerJa = self.Winner == "wolf" ? "人狼陣営" : "村人陣営";
        var sb = new StringBuilder();
        sb.AppendLine($"【ゲーム結果】{winnerJa}の勝利");
        sb.AppendLine("役職:");
        foreach (var kv in self.Roles)
        {
            var c = self.Direct<CharacterObject>().FirstOrDefault(x => x.Id == kv.Key);
            if (c == null) continue;
            var role = RoleJa(kv.Value);
            sb.AppendLine($"- {c.Name}: {role}");
        }
        var text = sb.ToString();

        foreach (var c in self.Direct<CharacterObject>())
            c.Memory.Add(text);
    }

    // ---------- ヘルパー ----------

    private static async Task Speak(WolfObject wolf, CharacterObject c, string eventId)
    {
        wolf.ThinkingId = c.Id;
        c.Status = "think";
        wolf.RaiseThinkingChanged(c.Id);
        try
        {
            await c.Exec(eventId);
        }
        finally
        {
            wolf.ThinkingId = null;
            wolf.RaiseThinkingChanged(null);
        }

        Log(wolf, new GameEvent
        {
            Day = wolf.Day,
            Phase = wolf.Phase,
            Kind = "utterance",
            SpeakerId = c.Id,
            SpeakerName = c.Name,
            Text = c.PendingAction,
            Status = c.Status
        });

        var line = $"{c.Name}: {c.PendingAction}";
        foreach (var o in wolf.Direct<CharacterObject>().Where(o => o.IsAlive))
            o.Memory.Add(line);
    }

    internal static void Log(WolfObject wolf, GameEvent e)
    {
        lock (_logLock) { wolf.EventLog.Add(e); }
        wolf.RaiseEventLogged(e);

        var prefix = e.Kind switch
        {
            "gm" => "[GM]",
            "system" => "[SYS]",
            _ => $"  {e.SpeakerName}"
        };

        if (e.Kind == "utterance")
            Debug.WriteLine($"  {e.SpeakerName}[{e.Status}]: {e.Text}");
        else
            Debug.WriteLine($"{prefix} {e.Text}");
    }

    private static void NotifyDeath(CharacterObject victim)
    {
        if (victim is PlayerObject)
            Debug.WriteLine("*** あなたは死にました ***");
    }

    private static string RoleJa(string role) => role switch
    {
        "wolf" => "人狼",
        "seer" => "占い師",
        "villager" => "村人",
        _ => role
    };

    // ---------- 雑談 ----------

    public async Task ChatRound(WolfObject self)
    {
        if (self.Phase != "Chat")
        {
            self.Phase = "Chat";
            self.Day = 0;
        }

        var alive = self.Direct<CharacterObject>().Where(x => x.IsAlive).ToList();
        foreach (var c in OrderedByTurn(self, alive))
        {
            if (self.StartRequested) return;
            await Speak(self, c, "Chat");
            if (self.StartRequested) return;
        }
    }

    public void ResetForChat(WolfObject self)
    {
        foreach (var c in self.Direct<CharacterObject>())
        {
            c.IsAlive = true;
            c.Status = "normal";
            c.PendingAction = "";
            c.PendingVote = "";
        }
        self.Deaths.Clear();
    }

    private static IEnumerable<CharacterObject> OrderedByTurn(WolfObject wolf, IEnumerable<CharacterObject> candidates)
    {
        var map = candidates.ToDictionary(c => c.Id, c => c);
        foreach (var id in wolf.TurnOrder)
            if (map.TryGetValue(id, out var c)) yield return c;

        foreach (var c in candidates)
            if (!wolf.TurnOrder.Contains(c.Id)) yield return c;
    }

    /// <summary>
    /// 全キャラの Memory を1回のLLM呼び出しで要約し、置換する。
    /// 次のゲーム開始時に呼ばれる。
    /// </summary>
    private static async Task SummarizeAllMemoriesAsync(WolfObject self)
    {
        var memories = self.Direct<CharacterObject>()
            .Where(c => c.Memory.Count > 0)
            .ToList();
        if (memories.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var c in memories)
        {
            sb.AppendLine($"## {c.Name} の記憶");
            foreach (var m in c.Memory)
                sb.AppendLine(m);
            sb.AppendLine();
        }

        var prompt =
            "以下はAIキャラたちの人狼ゲームでの会話ログと結果です。" +
            "次のゲームに備えて、重要な事実だけを簡潔な箇条書きに要約してください。" +
            "誰がどんな役職で、誰がどう疑われ、何が起きたかを、キャラごとに整理してください。";

        var summary = await Llm.AskRaw(prompt, sb.ToString());

        foreach (var c in self.Direct<CharacterObject>())
        {
            c.Memory.Clear();
            c.Memory.Add("【前回までのゲーム】");
            c.Memory.Add(summary);
        }
    }
}