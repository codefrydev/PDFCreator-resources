using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FryPdf.Plugin.Chess.Engine;

namespace FryPdf.Plugin.Chess.ViewModels;

public partial class ChessViewModel : ObservableObject
{
    private readonly Board _board = new();
    private readonly ChessSquareViewModel[] _allSquares = new ChessSquareViewModel[64];
    private CancellationTokenSource? _cpuCts;

    private int? _selectedSquare;
    private List<Move> _currentLegalMoves = [];
    private int? _promotionFrom;
    private int? _promotionTo;

    public ObservableCollection<ChessSquareViewModel> DisplaySquares { get; } = [];
    public ObservableCollection<MoveHistoryItemViewModel> MoveHistory { get; } = [];
    public ObservableCollection<Piece> CapturedWhitePieces { get; } = [];
    public ObservableCollection<Piece> CapturedBlackPieces { get; } = [];

    [ObservableProperty]
    private string _statusMessage = "White to move";

    [ObservableProperty]
    private string _currentTurn = "White";

    [ObservableProperty]
    private bool _isCpuThinking;

    [ObservableProperty]
    private bool _isGameOver;

    [ObservableProperty]
    private string _gameOverTitle = "";

    [ObservableProperty]
    private string _gameOverMessage = "";

    [ObservableProperty]
    private string _winner = "";

    [ObservableProperty]
    private string _materialAdvantageText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEasySelected))]
    [NotifyPropertyChangedFor(nameof(IsMediumSelected))]
    [NotifyPropertyChangedFor(nameof(IsHardSelected))]
    private string _difficulty = "Medium";

    public bool IsEasySelected => Difficulty == "Easy";
    public bool IsMediumSelected => Difficulty == "Medium";
    public bool IsHardSelected => Difficulty == "Hard";

    [ObservableProperty]
    private bool _isPlayerWhite = true;

    [ObservableProperty]
    private bool _isFlipped;

    [ObservableProperty]
    private bool _showLegalMoveHints = true;

    [ObservableProperty]
    private bool _showCoordinates = true;

    [ObservableProperty]
    private string _boardTheme = "Emerald";

    partial void OnBoardThemeChanged(string value)
    {
        foreach (var sq in _allSquares)
        {
            sq.UpdateTheme(value);
        }
    }

    [ObservableProperty]
    private bool _isPawnPromotionOpen;

    [ObservableProperty]
    private int _whiteWins;

    [ObservableProperty]
    private int _blackWins;

    [ObservableProperty]
    private int _draws;

    public ChessViewModel()
    {
        for (int i = 0; i < 64; i++)
        {
            _allSquares[i] = new ChessSquareViewModel(i);
        }

        ResetGame();
    }

    [RelayCommand]
    public void ResetGame()
    {
        _cpuCts?.Cancel();
        _cpuCts?.Dispose();
        _cpuCts = null;

        _board.SetupInitialPosition();
        _selectedSquare = null;
        _currentLegalMoves.Clear();
        IsPawnPromotionOpen = false;
        _promotionFrom = null;
        _promotionTo = null;

        IsGameOver = false;
        GameOverTitle = "";
        GameOverMessage = "";
        Winner = "";
        IsCpuThinking = false;

        MoveHistory.Clear();
        RefreshBoardState();

        StatusMessage = "White's Turn (User)";
        CurrentTurn = "White";

        if (!IsPlayerWhite)
        {
            // CPU is White! Trigger CPU move
            _ = TriggerCpuTurnAsync();
        }
    }

    [RelayCommand]
    public void UndoMove()
    {
        if (IsCpuThinking || _board.History.Count == 0) return;

        _selectedSquare = null;
        ClearHighlights();
        IsPawnPromotionOpen = false;
        IsGameOver = false;

        // In vs CPU mode, undo 2 plies to get back to user turn
        if (_board.History.Count >= 2)
        {
            _board.UndoMove();
            _board.UndoMove();
            if (MoveHistory.Count > 0)
            {
                var last = MoveHistory[^1];
                if (!string.IsNullOrEmpty(last.BlackMove))
                {
                    MoveHistory.RemoveAt(MoveHistory.Count - 1);
                }
                else
                {
                    MoveHistory.RemoveAt(MoveHistory.Count - 1);
                }
            }
        }
        else if (_board.History.Count == 1)
        {
            _board.UndoMove();
            MoveHistory.Clear();
        }

        RefreshBoardState();
        CurrentTurn = _board.ActiveColor == PieceColor.White ? "White" : "Black";
        StatusMessage = $"{CurrentTurn}'s Turn";
    }

    [RelayCommand]
    public void FlipBoard()
    {
        IsFlipped = !IsFlipped;
        RebuildDisplaySquares();
    }

    [RelayCommand]
    public void SelectDifficulty(string level)
    {
        if (level is "Easy" or "Medium" or "Hard")
        {
            Difficulty = level;
        }
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        BoardTheme = BoardTheme switch
        {
            "Emerald" => "ClassicWood",
            "ClassicWood" => "MidnightSlate",
            _ => "Emerald"
        };
    }

    [RelayCommand]
    public void SquareClicked(int index)
    {
        if (IsGameOver || IsCpuThinking || IsPawnPromotionOpen) return;

        // Check if it's player's turn
        var playerColor = IsPlayerWhite ? PieceColor.White : PieceColor.Black;
        if (_board.ActiveColor != playerColor) return;

        var clickedSq = _allSquares[index];

        // 1. If clicking on own piece -> Select it
        if (clickedSq.HasPiece && clickedSq.PieceColor == playerColor)
        {
            SelectSquare(index);
            return;
        }

        // 2. If a piece is already selected, check if destination is a valid legal move
        if (_selectedSquare.HasValue)
        {
            var matchingMoves = _currentLegalMoves.Where(m => m.From == _selectedSquare.Value && m.To == index).ToList();
            if (matchingMoves.Count > 0)
            {
                // Check for pawn promotion
                if (matchingMoves.Any(m => m.Promotion != PieceType.None))
                {
                    _promotionFrom = _selectedSquare.Value;
                    _promotionTo = index;
                    IsPawnPromotionOpen = true;
                    return;
                }

                // Execute move
                var chosenMove = matchingMoves[0];
                ExecutePlayerMove(chosenMove);
                return;
            }
        }

        // 3. Otherwise deselect
        _selectedSquare = null;
        ClearHighlights();
    }

    [RelayCommand]
    public void PromotePawn(string pieceTypeStr)
    {
        if (!_promotionFrom.HasValue || !_promotionTo.HasValue)
        {
            IsPawnPromotionOpen = false;
            return;
        }

        var promoType = pieceTypeStr switch
        {
            "Rook" => PieceType.Rook,
            "Bishop" => PieceType.Bishop,
            "Knight" => PieceType.Knight,
            _ => PieceType.Queen
        };

        var move = _currentLegalMoves.FirstOrDefault(m =>
            m.From == _promotionFrom.Value &&
            m.To == _promotionTo.Value &&
            m.Promotion == promoType);

        IsPawnPromotionOpen = false;
        _promotionFrom = null;
        _promotionTo = null;

        if (!move.IsNull)
        {
            ExecutePlayerMove(move);
        }
    }

    private void SelectSquare(int index)
    {
        _selectedSquare = index;
        ClearHighlights();

        _allSquares[index].IsSelected = true;

        if (ShowLegalMoveHints)
        {
            var movesFromSq = _currentLegalMoves.Where(m => m.From == index);
            foreach (var m in movesFromSq)
            {
                var target = _allSquares[m.To];
                if (target.HasPiece || m.IsEnPassant)
                {
                    target.IsCaptureTarget = true;
                }
                else
                {
                    target.IsValidTarget = true;
                }
            }
        }
    }

    private void ExecutePlayerMove(Move move)
    {
        string san = MoveGenerator.GenerateSan(_board, move, _currentLegalMoves);
        _board.MakeMove(move, san);
        RecordMoveHistory(san, PieceColor.White);

        _selectedSquare = null;
        ClearHighlights();

        // Highlight last move squares
        _allSquares[move.From].IsLastMove = true;
        _allSquares[move.To].IsLastMove = true;

        RefreshBoardState();

        if (CheckGameOver()) return;

        // Trigger CPU Turn
        _ = TriggerCpuTurnAsync();
    }

    private async Task TriggerCpuTurnAsync()
    {
        var cpuColor = IsPlayerWhite ? PieceColor.Black : PieceColor.White;
        if (_board.ActiveColor != cpuColor || IsGameOver) return;

        IsCpuThinking = true;
        StatusMessage = "🤖 CPU is thinking...";

        _cpuCts?.Dispose();
        _cpuCts = new CancellationTokenSource();

        try
        {
            // Give a realistic tactile delay for quick calculations
            await Task.Delay(350, _cpuCts.Token);

            var bestMove = await ChessAi.FindBestMoveAsync(_board, cpuColor, Difficulty, _cpuCts.Token);
            if (!bestMove.IsNull && !IsGameOver)
            {
                var legalMoves = MoveGenerator.GenerateLegalMoves(_board, cpuColor);
                string san = MoveGenerator.GenerateSan(_board, bestMove, legalMoves);
                _board.MakeMove(bestMove, san);
                RecordMoveHistory(san, cpuColor);

                ClearHighlights();
                _allSquares[bestMove.From].IsLastMove = true;
                _allSquares[bestMove.To].IsLastMove = true;

                RefreshBoardState();
                CheckGameOver();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled on reset or undo
        }
        finally
        {
            IsCpuThinking = false;
            if (!IsGameOver)
            {
                CurrentTurn = _board.ActiveColor == PieceColor.White ? "White" : "Black";
                StatusMessage = $"{CurrentTurn}'s Turn (User)";
            }
        }
    }

    private bool CheckGameOver()
    {
        var active = _board.ActiveColor;
        var legal = MoveGenerator.GenerateLegalMoves(_board, active);
        bool inCheck = _board.IsKingInCheck(active);

        if (legal.Count == 0)
        {
            IsGameOver = true;
            if (inCheck)
            {
                // Checkmate!
                var winnerColor = active == PieceColor.White ? PieceColor.Black : PieceColor.White;
                Winner = winnerColor == PieceColor.White ? "White" : "Black";
                bool playerWon = (winnerColor == PieceColor.White && IsPlayerWhite) ||
                                 (winnerColor == PieceColor.Black && !IsPlayerWhite);

                GameOverTitle = playerWon ? "🏆 Victory! Checkmate!" : "💀 Defeat! Checkmate!";
                GameOverMessage = $"{Winner} delivered checkmate and won the game.";
                StatusMessage = $"🎉 Checkmate! {Winner} Wins!";

                if (Winner == "White") WhiteWins++;
                else BlackWins++;
            }
            else
            {
                // Stalemate!
                Winner = "Draw";
                GameOverTitle = "🤝 Stalemate!";
                GameOverMessage = "The active player has no legal moves and is not in check. The game is a draw.";
                StatusMessage = "🤝 Game Drawn by Stalemate!";
                Draws++;
            }
            return true;
        }

        // 50-move rule
        if (_board.HalfmoveClock >= 100)
        {
            IsGameOver = true;
            Winner = "Draw";
            GameOverTitle = "🤝 Draw!";
            GameOverMessage = "50 consecutive moves occurred without a pawn move or capture.";
            StatusMessage = "🤝 Game Drawn (50-move rule)";
            Draws++;
            return true;
        }

        if (inCheck)
        {
            int kingSq = _board.FindKing(active);
            if (kingSq >= 0) _allSquares[kingSq].IsInCheck = true;
            StatusMessage = $"⚠️ {active} is in Check!";
        }

        return false;
    }

    private void RefreshBoardState()
    {
        for (int i = 0; i < 64; i++)
        {
            _allSquares[i].UpdatePiece(_board[i]);
        }

        _currentLegalMoves = MoveGenerator.GenerateLegalMoves(_board, _board.ActiveColor);

        UpdateCapturedPieces();
        RebuildDisplaySquares();
    }

    private void RebuildDisplaySquares()
    {
        DisplaySquares.Clear();

        if (!IsFlipped)
        {
            // White on bottom: row 0 (top) = rank 7 (56..63) down to row 7 (bottom) = rank 0 (0..7)
            for (int r = 7; r >= 0; r--)
            {
                for (int f = 0; f < 8; f++)
                {
                    DisplaySquares.Add(_allSquares[Square.Index(f, r)]);
                }
            }
        }
        else
        {
            // Black on bottom: row 0 (top) = rank 0, file 7 down to 0
            for (int r = 0; r < 8; r++)
            {
                for (int f = 7; f >= 0; f--)
                {
                    DisplaySquares.Add(_allSquares[Square.Index(f, r)]);
                }
            }
        }
    }

    private void ClearHighlights()
    {
        for (int i = 0; i < 64; i++)
        {
            _allSquares[i].ClearHighlights();
        }
    }

    private void RecordMoveHistory(string san, PieceColor color)
    {
        if (color == PieceColor.White)
        {
            MoveHistory.Add(new MoveHistoryItemViewModel(_board.FullmoveNumber, san));
        }
        else
        {
            if (MoveHistory.Count > 0 && string.IsNullOrEmpty(MoveHistory[^1].BlackMove))
            {
                MoveHistory[^1].BlackMove = san;
            }
            else
            {
                MoveHistory.Add(new MoveHistoryItemViewModel(_board.FullmoveNumber, "-", san));
            }
        }
    }

    private void UpdateCapturedPieces()
    {
        CapturedWhitePieces.Clear();
        CapturedBlackPieces.Clear();

        // Count pieces currently on board
        var counts = new Dictionary<Piece, int>();
        for (int i = 0; i < 64; i++)
        {
            var p = _board[i];
            if (!p.IsEmpty)
            {
                counts[p] = counts.GetValueOrDefault(p, 0) + 1;
            }
        }

        // Expected starting pieces
        void CheckCaptured(PieceType type, PieceColor color, int expectedCount)
        {
            var p = new Piece(type, color);
            int current = counts.GetValueOrDefault(p, 0);
            int missing = Math.Max(0, expectedCount - current);
            for (int i = 0; i < missing; i++)
            {
                if (color == PieceColor.White)
                    CapturedWhitePieces.Add(p);
                else
                    CapturedBlackPieces.Add(p);
            }
        }

        CheckCaptured(PieceType.Queen, PieceColor.White, 1);
        CheckCaptured(PieceType.Rook, PieceColor.White, 2);
        CheckCaptured(PieceType.Bishop, PieceColor.White, 2);
        CheckCaptured(PieceType.Knight, PieceColor.White, 2);
        CheckCaptured(PieceType.Pawn, PieceColor.White, 8);

        CheckCaptured(PieceType.Queen, PieceColor.Black, 1);
        CheckCaptured(PieceType.Rook, PieceColor.Black, 2);
        CheckCaptured(PieceType.Bishop, PieceColor.Black, 2);
        CheckCaptured(PieceType.Knight, PieceColor.Black, 2);
        CheckCaptured(PieceType.Pawn, PieceColor.Black, 8);

        // Material Advantage
        int whiteMat = 0;
        int blackMat = 0;
        for (int i = 0; i < 64; i++)
        {
            var p = _board[i];
            if (p.Type == PieceType.King) continue;
            if (p.Color == PieceColor.White) whiteMat += p.BaseValue;
            else if (p.Color == PieceColor.Black) blackMat += p.BaseValue;
        }

        int diff = (whiteMat - blackMat) / 100;
        if (diff > 0)
        {
            MaterialAdvantageText = $"+{diff}";
        }
        else if (diff < 0)
        {
            MaterialAdvantageText = $"{diff}";
        }
        else
        {
            MaterialAdvantageText = "";
        }
    }
}
