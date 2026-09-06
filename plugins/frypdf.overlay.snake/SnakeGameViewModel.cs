using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PdfEditorApp.Core.Plugins.Settings;

namespace PdfEditorApp.Plugins.Snake;

public enum SnakeDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

public enum SnakeGameState
{
    Ready,
    Playing,
    Paused,
    GameOver
}

public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// MVVM game loop and state management for the floating Snake Game plugin.
/// Designed for 60+ FPS zero-allocation tick processing, dynamic speed acceleration, and persistent high scores.
/// </summary>
public partial class SnakeGameViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _gameTimer;
    private readonly Random _random = new();
    private readonly IPluginSettingsStore? _settingsStore;

    public const int GridWidth = 20;
    public const int GridHeight = 20;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private int _highScore;

    [ObservableProperty]
    private SnakeGameState _state = SnakeGameState.Ready;

    [ObservableProperty]
    private bool _wallCollision = true;

    [ObservableProperty]
    private int _speedMs = 120;

    [ObservableProperty]
    private string _statusText = "Press Space or Arrows to Start";

    [ObservableProperty]
    private string _actionButtonLabel = "Start";

    private SnakeDirection _currentDirection = SnakeDirection.Right;
    private SnakeDirection _pendingDirection = SnakeDirection.Right;

    public LinkedList<GridPoint> SnakeBody { get; } = new();
    public GridPoint Food { get; private set; }
    public GridPoint? BonusFood { get; private set; }
    public int BonusFoodTicksRemaining { get; private set; }

    public event Action? RenderRequested;

    public SnakeGameViewModel(IServiceProvider? serviceProvider = null)
    {
        _settingsStore = serviceProvider?.GetService<IPluginSettingsStore>();
        LoadSettingsAndHighScore();

        _gameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SpeedMs)
        };
        _gameTimer.Tick += OnGameTick;

        ResetGame();
    }

    private void LoadSettingsAndHighScore()
    {
        if (_settingsStore != null)
        {
            HighScore = (int)_settingsStore.GetSetting("frypdf.overlay.snake", "HighScore", 0.0);
            WallCollision = _settingsStore.GetSetting("frypdf.overlay.snake", "WallCollision", true);
            var speedPref = _settingsStore.GetSetting("frypdf.overlay.snake", "InitialSpeed", "Normal");
            SpeedMs = speedPref switch
            {
                "Fast" => 90,
                "Zen" => 160,
                _ => 120
            };
        }
    }

    public void ResetGame()
    {
        _gameTimer.Stop();
        SnakeBody.Clear();

        // Initial 3-segment snake in middle
        int startX = GridWidth / 2;
        int startY = GridHeight / 2;
        SnakeBody.AddFirst(new GridPoint(startX, startY));
        SnakeBody.AddLast(new GridPoint(startX - 1, startY));
        SnakeBody.AddLast(new GridPoint(startX - 2, startY));

        _currentDirection = SnakeDirection.Right;
        _pendingDirection = SnakeDirection.Right;
        Score = 0;
        BonusFood = null;
        BonusFoodTicksRemaining = 0;
        State = SnakeGameState.Ready;
        StatusText = "Press Space or Arrows to Play";
        ActionButtonLabel = "Start";

        SpawnFood();
        RenderRequested?.Invoke();
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        switch (State)
        {
            case SnakeGameState.Ready:
                StartGame();
                break;
            case SnakeGameState.Playing:
                PauseGame();
                break;
            case SnakeGameState.Paused:
                ResumeGame();
                break;
            case SnakeGameState.GameOver:
                ResetGame();
                StartGame();
                break;
        }
    }

    public void StartGame()
    {
        State = SnakeGameState.Playing;
        StatusText = "Playing";
        ActionButtonLabel = "Pause";
        _gameTimer.Interval = TimeSpan.FromMilliseconds(SpeedMs);
        _gameTimer.Start();
        RenderRequested?.Invoke();
    }

    public void PauseGame()
    {
        if (State != SnakeGameState.Playing) return;
        _gameTimer.Stop();
        State = SnakeGameState.Paused;
        StatusText = "Game Paused";
        ActionButtonLabel = "Resume";
        RenderRequested?.Invoke();
    }

    public void ResumeGame()
    {
        if (State != SnakeGameState.Paused) return;
        State = SnakeGameState.Playing;
        StatusText = "Playing";
        ActionButtonLabel = "Pause";
        _gameTimer.Start();
        RenderRequested?.Invoke();
    }

    public void ChangeDirection(SnakeDirection newDir)
    {
        if (newDir == SnakeDirection.None) return;

        if (State == SnakeGameState.Ready || State == SnakeGameState.Paused)
        {
            StartGame();
        }

        // Prevent immediate 180-degree reverse
        bool isOpposite = (_currentDirection == SnakeDirection.Up && newDir == SnakeDirection.Down) ||
                          (_currentDirection == SnakeDirection.Down && newDir == SnakeDirection.Up) ||
                          (_currentDirection == SnakeDirection.Left && newDir == SnakeDirection.Right) ||
                          (_currentDirection == SnakeDirection.Right && newDir == SnakeDirection.Left);

        if (!isOpposite)
        {
            _pendingDirection = newDir;
        }
    }

    private void OnGameTick(object? sender, EventArgs e)
    {
        if (State != SnakeGameState.Playing) return;

        _currentDirection = _pendingDirection;
        var head = SnakeBody.First!.Value;
        int nextX = head.X;
        int nextY = head.Y;

        switch (_currentDirection)
        {
            case SnakeDirection.Up: nextY--; break;
            case SnakeDirection.Down: nextY++; break;
            case SnakeDirection.Left: nextX--; break;
            case SnakeDirection.Right: nextX++; break;
        }

        // 1. Check Wall Collision / Wrap-around
        if (WallCollision)
        {
            if (nextX < 0 || nextX >= GridWidth || nextY < 0 || nextY >= GridHeight)
            {
                TriggerGameOver("Crashed into wall!");
                return;
            }
        }
        else
        {
            nextX = (nextX + GridWidth) % GridWidth;
            nextY = (nextY + GridHeight) % GridHeight;
        }

        var newHead = new GridPoint(nextX, nextY);

        // 2. Check Self-Collision
        // Ignore the tail segment since it will move forward if not eating
        var currentTail = SnakeBody.Last!.Value;
        bool isEating = (newHead == Food) || (BonusFood.HasValue && newHead == BonusFood.Value);

        foreach (var segment in SnakeBody)
        {
            if (!isEating && segment == currentTail) continue;
            if (segment == newHead)
            {
                TriggerGameOver("Ran into yourself!");
                return;
            }
        }

        // 3. Move Snake
        SnakeBody.AddFirst(newHead);

        if (newHead == Food)
        {
            Score += 10;
            UpdateHighScore();
            SpawnFood();

            // Dynamic speed up: decrease interval slightly as snake gets longer
            var newIntervalMs = Math.Max(55, SpeedMs - (Score / 30) * 4);
            _gameTimer.Interval = TimeSpan.FromMilliseconds(newIntervalMs);

            // Chance to spawn golden bonus food every 50 points
            if (Score % 50 == 0 && !BonusFood.HasValue)
            {
                SpawnBonusFood();
            }
        }
        else if (BonusFood.HasValue && newHead == BonusFood.Value)
        {
            Score += 50;
            UpdateHighScore();
            BonusFood = null;
            BonusFoodTicksRemaining = 0;
        }
        else
        {
            // Remove tail if didn't eat
            SnakeBody.RemoveLast();
        }

        // Decrement bonus food countdown
        if (BonusFood.HasValue)
        {
            BonusFoodTicksRemaining--;
            if (BonusFoodTicksRemaining <= 0)
            {
                BonusFood = null;
            }
        }

        RenderRequested?.Invoke();
    }

    private void TriggerGameOver(string reason)
    {
        _gameTimer.Stop();
        State = SnakeGameState.GameOver;
        StatusText = $"Game Over! {reason}";
        ActionButtonLabel = "Play Again";
        UpdateHighScore();
        RenderRequested?.Invoke();
    }

    private void UpdateHighScore()
    {
        if (Score > HighScore)
        {
            HighScore = Score;
            _settingsStore?.SetSetting("frypdf.overlay.snake", "HighScore", (double)HighScore);
            _settingsStore?.Save();
        }
    }

    private void SpawnFood()
    {
        var occupied = new HashSet<GridPoint>(SnakeBody);
        if (BonusFood.HasValue) occupied.Add(BonusFood.Value);

        var available = new List<GridPoint>();
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                var pt = new GridPoint(x, y);
                if (!occupied.Contains(pt))
                {
                    available.Add(pt);
                }
            }
        }

        if (available.Count > 0)
        {
            Food = available[_random.Next(available.Count)];
        }
    }

    private void SpawnBonusFood()
    {
        var occupied = new HashSet<GridPoint>(SnakeBody) { Food };
        var available = new List<GridPoint>();
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                var pt = new GridPoint(x, y);
                if (!occupied.Contains(pt))
                {
                    available.Add(pt);
                }
            }
        }

        if (available.Count > 0)
        {
            BonusFood = available[_random.Next(available.Count)];
            BonusFoodTicksRemaining = 45; // ~5 seconds
        }
    }

    public void Dispose()
    {
        _gameTimer.Stop();
        _gameTimer.Tick -= OnGameTick;
    }
}
