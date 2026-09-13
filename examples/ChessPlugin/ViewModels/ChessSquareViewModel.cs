using CommunityToolkit.Mvvm.ComponentModel;
using FryPdf.Plugin.Chess.Engine;

namespace FryPdf.Plugin.Chess.ViewModels;

public partial class ChessSquareViewModel : ObservableObject
{
    public int Index { get; }
    public int File => Square.File(Index);
    public int Rank => Square.Rank(Index);
    public string Coordinate => Square.ToAlgebraic(Index);
    public bool IsLightSquare => (File + Rank) % 2 != 0;

    [ObservableProperty]
    private PieceType _pieceType = PieceType.None;

    [ObservableProperty]
    private PieceColor _pieceColor = PieceColor.White;

    [ObservableProperty]
    private bool _hasPiece;

    [ObservableProperty]
    private string _pieceIconKind = "";

    [ObservableProperty]
    private string _pieceGlyph = "";

    [ObservableProperty]
    private string _pieceForeground = "#FFFFFF";

    [ObservableProperty]
    private string _pieceOutline = "#0F172A";

    [ObservableProperty]
    private string _squareBackground = "#EBECD0";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isValidTarget;

    [ObservableProperty]
    private bool _isCaptureTarget;

    [ObservableProperty]
    private bool _isLastMove;

    [ObservableProperty]
    private bool _isInCheck;

    public ChessSquareViewModel(int index)
    {
        Index = index;
        UpdateTheme("Emerald");
    }

    public void UpdateTheme(string theme)
    {
        SquareBackground = theme switch
        {
            "ClassicWood" => IsLightSquare ? "#F0D9B5" : "#B58863",
            "MidnightSlate" => IsLightSquare ? "#94A3B8" : "#334155",
            _ => IsLightSquare ? "#EBECD0" : "#739552" // Emerald Tournament
        };
    }

    public void UpdatePiece(Piece piece)
    {
        PieceType = piece.Type;
        PieceColor = piece.Color;
        HasPiece = !piece.IsEmpty;

        if (piece.Color == PieceColor.White)
        {
            PieceForeground = "#FFFFFF";
            PieceOutline = "#0F172A";
        }
        else
        {
            PieceForeground = "#1E293B";
            PieceOutline = "#F8FAFC";
        }

        PieceIconKind = piece.Type switch
        {
            PieceType.King => "ChessKing",
            PieceType.Queen => "ChessQueen",
            PieceType.Rook => "ChessRook",
            PieceType.Bishop => "ChessBishop",
            PieceType.Knight => "ChessKnight",
            PieceType.Pawn => "ChessPawn",
            _ => ""
        };

        PieceGlyph = piece.Type switch
        {
            PieceType.King => piece.Color == PieceColor.White ? "♔" : "♚",
            PieceType.Queen => piece.Color == PieceColor.White ? "♕" : "♛",
            PieceType.Rook => piece.Color == PieceColor.White ? "♖" : "♜",
            PieceType.Bishop => piece.Color == PieceColor.White ? "♗" : "♝",
            PieceType.Knight => piece.Color == PieceColor.White ? "♘" : "♞",
            PieceType.Pawn => piece.Color == PieceColor.White ? "♙" : "♟",
            _ => ""
        };
    }

    public void ClearHighlights()
    {
        IsSelected = false;
        IsValidTarget = false;
        IsCaptureTarget = false;
        IsLastMove = false;
        IsInCheck = false;
    }
}
