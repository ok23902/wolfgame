using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace mano;

public class AIActionTrait : ManoTrait<CharacterAIObject>
{
    // ============================================================
    // 感情タグの定義（ここを編集すれば Regex とプロンプト両方に反映）
    // ============================================================
    private static readonly string[] ValidStatuses =
    {
        "normal", "happy", "think", "confused", "angry", "sad", "fear", "surprised", "hello"
    };

    private static readonly Regex StatusRegex = new(
        @"^\s*\[(" + string.Join("|", ValidStatuses) + @")\]\s*",
        RegexOptions.Compiled);

    private static readonly string StatusListForPrompt = string.Join("|", ValidStatuses);
    private static readonly string StatusExample = $"[{ValidStatuses[1 % ValidStatuses.Length]}] やっほー！";

    private static (string status, string text) ParseStatus(string raw)
    {
        // 先頭チェック
        var m = StatusRegex.Match(raw);
        if (m.Success)
            return (m.Groups[1].Value, raw[m.Length..].Trim());

        // ★ 行頭以外のタグも探す（複数行対応）
        var lines = raw.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m2 = StatusRegex.Match(lines[i]);
            if (m2.Success)
            {
                lines[i] = lines[i][m2.Length..];
                return (m2.Groups[1].Value, string.Join('\n', lines).Trim());
            }
        }
        return ("normal", raw.Trim());
    }

    // ---------- 雑談 ----------

    public async Task Chat(CharacterAIObject self)
    {
        var system = BuildSystem(self);
        var user = BuildChatPrompt(self);
        var raw = await Llm.AskRaw(system, user);
        var (status, text) = ParseStatus(raw);
        self.Status = status;
        self.PendingAction = text;
    }

    private static string BuildChatPrompt(CharacterAIObject self)
    {
        var sb = new StringBuilder();
        if (self.Memory.Count > 0)
        {
            sb.AppendLine("# これまでの会話");
            foreach (var m in self.Memory.TakeLast(8))
                sb.AppendLine(m);
            sb.AppendLine();
            sb.AppendLine("# 指示");
            sb.AppendLine("雑談を続けてください。直前の発言に触れて、自然に会話を進めてください。");
            sb.AppendLine("相手の発言を無視してはいけません。短く、セリフだけを返してください。");
        }
        else
        {
            sb.AppendLine("# 指示");
            sb.AppendLine("雑談の最初の一言を言ってください。短く、セリフだけ。");
        }
        return sb.ToString();
    }

    // ---------- 昼・議論 ----------

    public async Task DayDiscuss(CharacterAIObject self)
    {
        var wolf = self.Parent as WolfObject;
        if (wolf == null) return;

        var system = BuildSystem(self);
        var user = BuildDayDiscussPrompt(self, wolf);
        var raw = await Llm.AskRaw(system, user);
        var (status, text) = ParseStatus(raw);
        self.Status = status;
        self.PendingAction = text;
    }

    private static string BuildDayDiscussPrompt(CharacterAIObject self, WolfObject wolf)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildFacts(wolf, self));

        if (self.Memory.Count > 0)
        {
            sb.AppendLine("# これまでの記憶");
            foreach (var m in self.Memory.TakeLast(15))
                sb.AppendLine(m);
            sb.AppendLine();
        }

        sb.AppendLine("# 指示");
        sb.AppendLine("人狼ゲームの昼の議論中です。");
        sb.AppendLine("直前の他者の発言があれば、必ず触れてから自分の意見を述べてください。");
        sb.AppendLine("誰かを疑う、弁明する、占い結果を公表する、のいずれかを1文で。");
        sb.AppendLine("確定事実にない過去（存在しない占い結果など）を参照してはいけません。");

        if (self.Role == "seer")
        {
            var undisclosed = wolf.Divinations
                .Where(d => d.SeerId == self.Id && !d.Disclosed)
                .ToList();
            if (undisclosed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# 占い師としての選択");
                sb.AppendLine("あなたはまだ公表していない占い結果を持っています:");
                foreach (var d in undisclosed)
                    sb.AppendLine($"  - {d.TargetName} は {(d.IsWolf ? "人狼" : "人間")}（Day {d.Day}）");
                sb.AppendLine("公表する場合は「占い結果：◯◯は人狼でした」の形で発言してください。");
                sb.AppendLine("公表を見送ることもできます（人狼に狙われやすくなります）。");
            }
        }

        return sb.ToString();
    }

    // ---------- 昼・投票 ----------

    public async Task DayVote(CharacterAIObject self)
    {
        var wolf = self.Parent as WolfObject;
        if (wolf == null) return;

        var candidates = wolf.Direct<CharacterObject>()
            .Where(c => c.IsAlive && c.Id != self.Id).ToList();
        if (candidates.Count == 0) return;

        var system = BuildSystem(self);
        var sb = new StringBuilder();
        sb.AppendLine(BuildFacts(wolf, self));

        if (self.Memory.Count > 0)
        {
            sb.AppendLine("# 今日の議論");
            foreach (var m in self.Memory.TakeLast(12))
                sb.AppendLine(m);
            sb.AppendLine();
        }

        sb.AppendLine("# 投票");
        sb.AppendLine("以下の候補から1人選び、必ず次の2行だけを返してください。装飾や前置きは禁止。");
        sb.AppendLine("理由: (一文で)");
        sb.AppendLine("投票: (候補のid)");
        sb.AppendLine();
        sb.AppendLine("候補:");
        foreach (var c in candidates)
            sb.AppendLine($"- {c.Id}: {c.Name}");

        var answer = await Llm.AskRaw(system, sb.ToString());

        // 堅牢なパース
        string reason = "";
        string voteId = "";
        foreach (var rawLine in answer.Split('\n'))
        {
            var t = rawLine.Trim();
            t = t.TrimStart('*', '-', '#', '>', ' ', '【', '[').Trim();
            t = t.TrimEnd('】', ']');

            var colon = t.IndexOfAny(new[] { ':', '：' });
            if (colon < 0) continue;

            var key = t[..colon].Trim().Trim('*', ' ', '"', '「', '」', '『', '』', '【', '】');
            var val = t[(colon + 1)..].Trim().Trim('*', ' ', '"', '「', '」', '『', '』', '【', '】');

            if (key.Contains("理由")) reason = val;
            else if (key.Contains("投票")) voteId = val;
        }

        var picked = candidates.FirstOrDefault(c =>
                voteId.Equals(c.Id, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(c =>
                voteId.Contains(c.Id, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(c =>
                answer.Contains(c.Id, StringComparison.OrdinalIgnoreCase));

        if (picked != null)
        {
            self.PendingVote = picked.Id;
            var r = string.IsNullOrEmpty(reason) ? "" : $"（{reason}）";
            Debug.WriteLine($"  {self.Name}: → {picked.Name} に投票{r}");
        }
        else
        {
            Debug.WriteLine($"  {self.Name}: 投票棄権 [raw] {answer.Replace("\n", " / ")}");

            WolfTrait.Log(wolf, new GameEvent
            {
                Day = wolf.Day,
                Phase = wolf.Phase,
                Kind = "system",
                SpeakerName = "SYSTEM",
                Text = $"[投票パース失敗] {self.Name}: {answer.Replace("\n", " / ")}"
            });
        }
    }

    // ---------- 夜 ----------

    public async Task Night(CharacterAIObject self)
    {
        var wolf = self.Parent as WolfObject;
        if (wolf == null) return;

        if (self.Role == "wolf")
            await NightWolf(self, wolf);
        else if (self.Role == "seer")
            await NightSeer(self, wolf);
    }

    private async Task NightWolf(CharacterAIObject self, WolfObject wolf)
    {
        var targets = wolf.Direct<CharacterObject>()
            .Where(c => c.IsAlive && c.Role != "wolf").ToList();
        if (targets.Count == 0) return;

        var system = BuildSystem(self);
        var sb = new StringBuilder();
        sb.AppendLine(BuildFacts(wolf, self));
        sb.AppendLine("# 襲撃");
        sb.AppendLine("今夜の襲撃先を1人選び、idだけを1行で返してください。装飾や前置きは禁止。");
        sb.AppendLine("ヒント: 占い師や、議論をリードしている人物を優先すると有利です。");
        foreach (var c in targets)
            sb.AppendLine($"- {c.Id}: {c.Name}");

        var answer = await Llm.AskRaw(system, sb.ToString());
        var picked = targets.FirstOrDefault(c =>
            answer.Contains(c.Id, StringComparison.OrdinalIgnoreCase));

        if (picked != null)
        {
            self.PendingAction = picked.Id;
            Debug.WriteLine($"  [襲撃] {self.Name} → {picked.Name}");
        }
        else
        {
            Debug.WriteLine($"  [襲撃パース失敗] {self.Name}: {answer.Replace("\n", " / ")}");
        }
    }

    private async Task NightSeer(CharacterAIObject self, WolfObject wolf)
    {
        var already = wolf.Divinations
            .Where(d => d.SeerId == self.Id)
            .Select(d => d.TargetId)
            .ToHashSet();

        var targets = wolf.Direct<CharacterObject>()
            .Where(c => c.IsAlive && c.Id != self.Id && !already.Contains(c.Id))
            .ToList();

        if (targets.Count == 0) return;

        var system = BuildSystem(self);
        var sb = new StringBuilder();
        sb.AppendLine(BuildFacts(wolf, self));
        sb.AppendLine("# 占い");
        sb.AppendLine("占う相手を1人選び、idだけを1行で返してください。装飾や前置きは禁止。");
        sb.AppendLine("ヒント: まだ占っていない相手から選んでください。");
        foreach (var c in targets)
            sb.AppendLine($"- {c.Id}: {c.Name}");

        var answer = await Llm.AskRaw(system, sb.ToString());
        var picked = targets.FirstOrDefault(c =>
            answer.Contains(c.Id, StringComparison.OrdinalIgnoreCase));

        if (picked != null)
        {
            var isWolf = wolf.Roles.TryGetValue(picked.Id, out var r) && r == "wolf";
            wolf.Divinations.Add(new Divination
            {
                Day = wolf.Day,
                SeerId = self.Id,
                TargetId = picked.Id,
                TargetName = picked.Name,
                IsWolf = isWolf,
                Disclosed = false,
            });
            self.Memory.Add($"[占い] Day {wolf.Day}: {picked.Name} は {(isWolf ? "人狼" : "人間")} だった");
            Debug.WriteLine($"  [占い結果] {self.Name} → {picked.Name}: {(isWolf ? "人狼" : "人間")}");
        }
        else
        {
            Debug.WriteLine($"  [占いパース失敗] {self.Name}: {answer.Replace("\n", " / ")}");
        }
    }

    // ---------- プロンプト組み立て ----------

    private static string BuildSystem(CharacterAIObject self)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"あなたは「{self.Name}」というキャラクターです。");
        if (!string.IsNullOrEmpty(self.Personality))
            sb.AppendLine($"性格: {self.Personality}");
        if (!string.IsNullOrEmpty(self.Goal))
            sb.AppendLine($"目標: {self.Goal}");
        sb.AppendLine();
        sb.AppendLine("必ずキャラクターとして1人称で、短く返してください。");
        sb.AppendLine($"発言の冒頭に [{StatusListForPrompt}] のいずれかを付けてください。");
        sb.AppendLine($"例: {StatusExample}");
        sb.AppendLine("以下の「確定事実」に書かれていない過去の出来事を捏造してはいけません。");
        return sb.ToString();
    }

    private static string BuildFacts(WolfObject wolf, CharacterObject self)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 確定事実（これ以外を参照してはいけません）");
        sb.AppendLine($"- 現在: Day {wolf.Day} / {wolf.Phase}");

        if (wolf.Day == 1 && wolf.Deaths.Count == 0)
        {
            sb.AppendLine("- これは初日です。過去の占い結果、処刑、襲撃は存在しません。");
        }
        else if (wolf.Deaths.Count == 0)
        {
            sb.AppendLine("- 死亡者: なし");
        }
        else
        {
            sb.AppendLine("- 死亡者:");
            foreach (var d in wolf.Deaths)
                sb.AppendLine($"  - Day {d.Day} {d.Phase}: {d.Name}（{(d.Cause == "Vote" ? "追放" : "襲撃")}）");
        }

        var disclosed = wolf.Divinations.Where(d => d.Disclosed).ToList();
        if (disclosed.Count > 0)
        {
            sb.AppendLine("- 公表された占い結果:");
            foreach (var d in disclosed)
            {
                var seerName = wolf.Direct<CharacterObject>()
                    .FirstOrDefault(c => c.Id == d.SeerId)?.Name ?? d.SeerId;
                sb.AppendLine($"  - Day {d.Day} {seerName}: {d.TargetName} は {(d.IsWolf ? "人狼" : "人間")}");
            }
        }

        sb.AppendLine($"- あなたの役職: {self.Role}");

        if (self.Role == "seer")
        {
            var divs = self.Memory.Where(m => m.StartsWith("[占い]")).ToList();
            if (divs.Count > 0)
            {
                sb.AppendLine("- あなたの占い結果:");
                foreach (var d in divs) sb.AppendLine($"  {d}");
            }
            else
            {
                sb.AppendLine("- あなたはまだ誰も占っていません。");
            }
        }

        sb.AppendLine("- 占い結果は占い師本人しか知りません。他人が知っている前提で話してはいけません。");
        sb.AppendLine();
        return sb.ToString();
    }
}