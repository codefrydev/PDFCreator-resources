using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FryPdf.Plugin.TicTacToe;

/// <summary>
/// Individual cell on the 3x3 Tic-Tac-Toe grid.
/// </summary>
public partial class TicTacToeCellViewModel : ObservableObject
{
    public int Index { get; }

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private bool _isWinningCell;

    [ObservableProperty]
    private bool _isEnabled = true;

    public TicTacToeCellViewModel(int index)
    {
        Index = index;
    }

    public void Reset()
    {
        Value = "";
        IsWinningCell = false;
        IsEnabled = true;
    }
}

/// <summary>
/// Reactive ViewModel for the Tic-Tac-Toe mini-game.
/// Manages turn state, win detection, score tracking, and single-player AI heuristics.
/// </summary>
public partial class TicTacToeViewModel : ObservableObject
{
    private static readonly int[][] WinningLines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8], // Rows
        [0, 3, 6], [1, 4, 7], [2, 5, 8], // Columns
        [0, 4, 8], [2, 4, 6]             // Diagonals
    ];

    private readonly Random _random = new();

    public ObservableCollection<TicTacToeCellViewModel> Cells { get; } = new();

    [ObservableProperty]
    private string _currentTurn = "X";

    [ObservableProperty]
    private string _statusMessage = "Player X's Turn (❌)";

    [ObservableProperty]
    private bool _isGameOver;

    [ObservableProperty]
    private string _winner = "";

    [ObservableProperty]
    private bool _isAiMode = true;

    [ObservableProperty]
    private int _xWins;

    [ObservableProperty]
    private int _oWins;

    [ObservableProperty]
    private int _draws;

    public TicTacToeViewModel()
    {
        for (int i = 0; i < 9; i++)
        {
            Cells.Add(new TicTacToeCellViewModel(i));
        }

        ResetGame();
    }

    [RelayCommand]
    public void MakeMove(int index)
    {
        if (IsGameOver || index < 0 || index >= Cells.Count) return;

        var cell = Cells[index];
        if (!string.IsNullOrEmpty(cell.Value)) return;

        // Apply Player Move
        cell.Value = CurrentTurn;
        cell.IsEnabled = false;

        if (EvaluateBoard()) return;

        // Advance Turn
        CurrentTurn = CurrentTurn == "X" ? "O" : "X";
        StatusMessage = CurrentTurn == "X" ? "Player X's Turn (❌)" : "Player O's Turn (⭕)";

        // If Playing against AI and it's O's turn, calculate AI move
        if (IsAiMode && CurrentTurn == "O" && !IsGameOver)
        {
            ExecuteAiTurn();
        }
    }

    [RelayCommand]
    public void ResetGame()
    {
        foreach (var cell in Cells)
        {
            cell.Reset();
        }

        CurrentTurn = "X";
        IsGameOver = false;
        Winner = "";
        StatusMessage = "Player X's Turn (❌)";
    }

    [RelayCommand]
    public void ResetScores()
    {
        XWins = 0;
        OWins = 0;
        Draws = 0;
        ResetGame();
    }

    [RelayCommand]
    public void ToggleAiMode()
    {
        IsAiMode = !IsAiMode;
        ResetGame();
    }

    private void ExecuteAiTurn()
    {
        int move = FindBestMove("O", "X");
        if (move >= 0 && move < Cells.Count)
        {
            var cell = Cells[move];
            cell.Value = "O";
            cell.IsEnabled = false;

            if (EvaluateBoard()) return;

            CurrentTurn = "X";
            StatusMessage = "Player X's Turn (❌)";
        }
    }

    private bool EvaluateBoard()
    {
        // 1. Check for a Winning Line
        foreach (var line in WinningLines)
        {
            string a = Cells[line[0]].Value;
            string b = Cells[line[1]].Value;
            string c = Cells[line[2]].Value;

            if (!string.IsNullOrEmpty(a) && a == b && b == c)
            {
                IsGameOver = true;
                Winner = a;

                // Highlight winning combination
                Cells[line[0]].IsWinningCell = true;
                Cells[line[1]].IsWinningCell = true;
                Cells[line[2]].IsWinningCell = true;

                // Disable remaining empty cells
                foreach (var cell in Cells.Where(c => string.IsNullOrEmpty(c.Value)))
                {
                    cell.IsEnabled = false;
                }

                if (a == "X")
                {
                    XWins++;
                    StatusMessage = "🎉 Player X (❌) Wins!";
                }
                else
                {
                    OWins++;
                    StatusMessage = IsAiMode ? "🤖 Computer (⭕) Wins!" : "🎉 Player O (⭕) Wins!";
                }

                return true;
            }
        }

        // 2. Check for Draw
        if (Cells.All(c => !string.IsNullOrEmpty(c.Value)))
        {
            IsGameOver = true;
            Winner = "Draw";
            Draws++;
            StatusMessage = "🤝 It's a Draw!";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates tactical moves: 1) Win if possible, 2) Block opponent win, 3) Take center, 4) Take corner, 5) Any open.
    /// </summary>
    private int FindBestMove(string ai, string opponent)
    {
        var available = Cells.Where(c => string.IsNullOrEmpty(c.Value)).Select(c => c.Index).ToList();
        if (available.Count == 0) return -1;

        // 1. Check if AI can win in one move
        foreach (var idx in available)
        {
            if (SimulateMoveWins(idx, ai)) return idx;
        }

        // 2. Block opponent winning move
        foreach (var idx in available)
        {
            if (SimulateMoveWins(idx, opponent)) return idx;
        }

        // 3. Take center cell (4) if free
        if (available.Contains(4)) return 4;

        // 4. Take any corner (0, 2, 6, 8)
        int[] corners = [0, 2, 6, 8];
        var openCorners = corners.Where(available.Contains).ToList();
        if (openCorners.Count > 0)
        {
            return openCorners[_random.Next(openCorners.Count)];
        }

        // 5. Pick random available cell
        return available[_random.Next(available.Count)];
    }

    private bool SimulateMoveWins(int index, string mark)
    {
        foreach (var line in WinningLines)
        {
            if (!line.Contains(index)) continue;

            int other1 = line.First(i => i != index);
            int other2 = line.Last(i => i != index);

            if (Cells[other1].Value == mark && Cells[other2].Value == mark)
            {
                return true;
            }
        }
        return false;
    }
}
