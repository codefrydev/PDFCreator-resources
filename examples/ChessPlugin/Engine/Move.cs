using System;

namespace FryPdf.Plugin.Chess.Engine;

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    WhiteAll = WhiteKingside | WhiteQueenside,
    BlackAll = BlackKingside | BlackQueenside,
    All = WhiteAll | BlackAll
}

public readonly struct Move : IEquatable<Move>
{
    public int From { get; }
    public int To { get; }
    public PieceType Promotion { get; }
    public bool IsCastling { get; }
    public bool IsEnPassant { get; }
    public bool IsDoublePawnPush { get; }

    public static Move Null => new(-1, -1);

    public bool IsNull => From < 0 || To < 0;

    public Move(int from, int to, PieceType promotion = PieceType.None, bool isCastling = false, bool isEnPassant = false, bool isDoublePawnPush = false)
    {
        From = from;
        To = to;
        Promotion = promotion;
        IsCastling = isCastling;
        IsEnPassant = isEnPassant;
        IsDoublePawnPush = isDoublePawnPush;
    }

    public bool Equals(Move other) =>
        From == other.From &&
        To == other.To &&
        Promotion == other.Promotion &&
        IsCastling == other.IsCastling &&
        IsEnPassant == other.IsEnPassant;

    public override bool Equals(object? obj) => obj is Move other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(From, To, (byte)Promotion, IsCastling, IsEnPassant);
    public static bool operator ==(Move left, Move right) => left.Equals(right);
    public static bool operator !=(Move left, Move right) => !left.Equals(right);

    public override string ToString()
    {
        if (IsNull) return "0000";
        string prom = Promotion switch
        {
            PieceType.Queen => "=Q",
            PieceType.Rook => "=R",
            PieceType.Bishop => "=B",
            PieceType.Knight => "=N",
            _ => ""
        };
        return $"{Square.ToAlgebraic(From)}{Square.ToAlgebraic(To)}{prom}";
    }
}

public readonly struct MoveUndoState
{
    public Move Move { get; }
    public Piece CapturedPiece { get; }
    public CastlingRights CastlingRights { get; }
    public int? EnPassantSquare { get; }
    public int HalfmoveClock { get; }
    public string San { get; }

    public MoveUndoState(Move move, Piece capturedPiece, CastlingRights castlingRights, int? enPassantSquare, int halfmoveClock, string san = "")
    {
        Move = move;
        CapturedPiece = capturedPiece;
        CastlingRights = castlingRights;
        EnPassantSquare = enPassantSquare;
        HalfmoveClock = halfmoveClock;
        San = san;
    }
}
