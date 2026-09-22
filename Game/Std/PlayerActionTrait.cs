namespace mano;

public class PlayerActionTrait : ManoTrait<PlayerObject>
{
    public async Task Chat(PlayerObject self)
    {
        var text = await Input.RequestText("雑談を入力してください");
        self.PendingAction = string.IsNullOrWhiteSpace(text) ? "……。" : text;
    }

    public async Task DayDiscuss(PlayerObject self)
    {
        var text = await Input.RequestText("発言を入力してください");
        self.PendingAction = string.IsNullOrWhiteSpace(text) ? "……。" : text;
    }

    public async Task DayVote(PlayerObject self)
    {
        var wolf = self.Parent as WolfObject;
        if (wolf == null) return;

        var options = wolf.Direct<CharacterObject>()
            .Where(c => c.IsAlive && c.Id != self.Id)
            .Select(c => new ChoiceOption { Id = c.Id, Label = c.Name, ImageKey = c.Id })
            .ToList();

        // ★ 棄権を末尾に追加
        options.Add(new ChoiceOption { Id = "__abstain__", Label = "棄権（投票しない）" });

        var req = new ChoiceRequest
        {
            Prompt = "誰を追放しますか？",
            Options = options,
            AllowSkip = false
        };

        var picked = await Input.RequestChoice(req);
        if (picked != null && picked != "__abstain__")
            self.PendingVote = picked;
    }

    public async Task Night(PlayerObject self)
    {
        var wolf = self.Parent as WolfObject;
        if (wolf == null) return;

        if (self.Role == "wolf")
        {
            var req = new ChoiceRequest
            {
                Prompt = "誰を襲撃しますか？",
                Options = wolf.Direct<CharacterObject>()
                    .Where(c => c.IsAlive && c.Role != "wolf")
                    .Select(c => new ChoiceOption { Id = c.Id, Label = c.Name, ImageKey = c.Id })
                    .ToList()
            };
            var picked = await Input.RequestChoice(req);
            if (picked != null) self.PendingAction = picked;
        }
        else if (self.Role == "seer")
        {
            var req = new ChoiceRequest
            {
                Prompt = "誰を占いますか？",
                Options = wolf.Direct<CharacterObject>()
                    .Where(c => c.IsAlive && c.Id != self.Id)
                    .Select(c => new ChoiceOption { Id = c.Id, Label = c.Name, ImageKey = c.Id })
                    .ToList()
            };
            var picked = await Input.RequestChoice(req);
            if (picked != null)
            {
                var t = wolf.Direct<CharacterObject>().First(c => c.Id == picked);
                var isWolf = wolf.Roles.TryGetValue(t.Id, out var r) && r == "wolf";
                wolf.Divinations.Add(new Divination
                {
                    Day = wolf.Day,
                    SeerId = self.Id,
                    TargetId = t.Id,
                    TargetName = t.Name,
                    IsWolf = isWolf,
                    Disclosed = false
                });
                self.Memory.Add($"[占い] Day {wolf.Day}: {t.Name} は {(isWolf ? "人狼" : "人間")} だった");
            }
        }
    }
}