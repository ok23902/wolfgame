using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace mano;

public class GameEngine
{
    public WolfObject Wolf { get; private set; } = null!;
    public RoomObject Room { get; private set; } = null!;
    public CharacterObject? Player { get; private set; }

    public event Action<GameEvent>? EventEmitted;
    public event Action<WolfObject>? StateChanged;
    public event Action<string?>? ThinkingChanged;
    public event Action? VoteProgressChanged;
    public event Action<string>? GameEnded;
    public event Action? ChatRoundStarted;


    public bool StartRequested { get; set; }

    public void RequestStartGame() => Wolf.StartRequested = true;

    public async Task SetupAsync(string jsonPath)
    {
        TraitRegistry.Load();

        var world = new WorldObject { Id = "world", Name = "世界" };
        Room = new RoomObject { Id = "room", Name = "部屋" };
        world.Add(Room);

        var chars = Mano.Load<CharacterObject>(File.ReadAllText(jsonPath));
        var slots = new (double x, double y)[]
        {
            (0.20, 0.05), (0.50, 0.00), (0.80, 0.05),
            (0.20, 0.55), (0.50, 0.50), (0.80, 0.55),
            (0.35, 0.28), (0.65, 0.28),
        };
        var rng = new Random();
        for (int i = 0; i < chars.Length; i++)
        {
            var s = slots[i % slots.Length];
            chars[i].X = s.x + (rng.NextDouble() - 0.5) * 0.04;
            chars[i].Y = s.y + (rng.NextDouble() - 0.5) * 0.03;
            Room.Add(chars[i]);
        }
        Player = chars.OfType<PlayerObject>().FirstOrDefault();

        Wolf = new WolfObject { Id = "wolf", Name = "人狼ゲーム" };
        foreach (var c in Room.Direct<CharacterObject>().ToList())
        {
            Room.Rem(c);
            Wolf.Add(c);
        }

        Wolf.EventLogged += e => EventEmitted?.Invoke(e);
        Wolf.ThinkingChanged += id => ThinkingChanged?.Invoke(id);
        Wolf.VoteProgressChanged += () => VoteProgressChanged?.Invoke();

        Wolf.Phase = "Chat";
        Wolf.IsGameOver = false;
        Wolf.StartRequested = false;
        StateChanged?.Invoke(Wolf);
        ChatRoundStarted?.Invoke();
    }

    public async Task RunAsync()
    {
        try
        {
            while (true)   // ★ ゲーム終了後もループ
            {
                await Wolf.Exec("ResetForChat");
                Wolf.Phase = "Chat";
                Wolf.IsGameOver = false;
                Wolf.StartRequested = false;   // ★ リセット
                StateChanged?.Invoke(Wolf);
                ChatRoundStarted?.Invoke();

                while (!Wolf.StartRequested)
                {
                    await Wolf.Exec("ChatRound");
                    await Task.Yield();   // ★ 他スレッドに処理を譲る
                }

                // ---- ゲーム開始 ----
                await Wolf.Exec("StartGame");
                StateChanged?.Invoke(Wolf);

                while (!Wolf.IsGameOver)
                {
                    await Wolf.Exec("ProcessTurn");
                    StateChanged?.Invoke(Wolf);
                    await Task.Yield();
                }

                // ---- ゲーム終了 ----
                GameEnded?.Invoke(Wolf.Winner);

                // ダイアログ閉じるまで待機（UI 側で ReadyForNext をセット）
                await WaitForNextRoundAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RUN FAIL] {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            // ★ UIにも知らせる
            GameEnded?.Invoke($"ERROR: {ex.Message}");
            throw;
        }
    }

    // ★ UI が「次のラウンドへ」を押すまで待つ
    private TaskCompletionSource<bool>? _nextRoundTcs;

    public void ProceedToNextRound()
    {
        _nextRoundTcs?.TrySetResult(true);
    }

    private Task WaitForNextRoundAsync()
    {
        _nextRoundTcs = new TaskCompletionSource<bool>();
        return _nextRoundTcs.Task;
    }
}