using System;

namespace FryPdf.Plugin.Chess.Engine;

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6
}

public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

public readonly struct Piece : IEquatable<Piece>
{
    public PieceType Type { get; }
    public PieceColor Color { get; }

    public static Piece Empty => new(PieceType.None, PieceColor.White);

    public bool IsEmpty => Type == PieceType.None;
    public bool IsWhite => !IsEmpty && Color == PieceColor.White;
    public bool IsBlack => !IsEmpty && Color == PieceColor.Black;

    public Piece(PieceType type, PieceColor color)
    {
        Type = type;
        Color = color;
    }

    public int BaseValue => Type switch
    {
        PieceType.Pawn => 100,
        PieceType.Knight => 320,
        PieceType.Bishop => 330,
        PieceType.Rook => 500,
        PieceType.Queen => 900,
        PieceType.King => 20000,
        _ => 0
    };

    public char ToChar()
    {
        char c = Type switch
        {
            PieceType.Pawn => 'P',
            PieceType.Knight => 'N',
            PieceType.Bishop => 'B',
            PieceType.Rook => 'R',
            PieceType.Queen => 'Q',
            PieceType.King => 'K',
            _ => '.'
        };
        return Color == PieceColor.White ? c : char.ToLowerInvariant(c);
    }

    public string ToGlyph => Type switch
    {
        PieceType.King => Color == PieceColor.White ? "♔" : "♚",
        PieceType.Queen => Color == PieceColor.White ? "♕" : "♛",
        PieceType.Rook => Color == PieceColor.White ? "♖" : "♜",
        PieceType.Bishop => Color == PieceColor.White ? "♗" : "♝",
        PieceType.Knight => Color == PieceColor.White ? "♘" : "♞",
        PieceType.Pawn => Color == PieceColor.White ? "♙" : "♟",
        _ => ""
    };

    public static Piece FromChar(char c)
    {
        var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        var type = char.ToUpperInvariant(c) switch
        {
            'P' => PieceType.Pawn,
            'N' => PieceType.Knight,
            'B' => PieceType.Bishop,
            'R' => PieceType.Rook,
            'Q' => PieceType.Queen,
            'K' => PieceType.King,
            _ => PieceType.None
        };
        return type == PieceType.None ? Empty : new Piece(type, color);
    }

    public bool Equals(Piece other) => Type == other.Type && Color == other.Color;
    public override bool Equals(object? obj) => obj is Piece other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((byte)Type, (byte)Color);
    public static bool operator ==(Piece left, Piece right) => left.Equals(right);
    public static bool operator !=(Piece left, Piece right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? "." : $"{Color} {Type}";
}
