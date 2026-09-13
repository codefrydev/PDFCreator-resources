using System;
using System.Collections.Generic;

namespace FryPdf.Plugin.Chess.Engine;

public class Board
{
    private readonly Piece[] _squares = new Piece[64];
    private readonly Stack<MoveUndoState> _history = new();

    public PieceColor ActiveColor { get; set; } = PieceColor.White;
    public CastlingRights CastlingRights { get; set; } = CastlingRights.All;
    public int? EnPassantSquare { get; set; }
    public int HalfmoveClock { get; set; }
    public int FullmoveNumber { get; set; } = 1;

    public Piece this[int index]
    {
        get => _squares[index];
        set => _squares[index] = value;
    }

    public Piece this[int file, int rank]
    {
        get => _squares[Square.Index(file, rank)];
        set => _squares[Square.Index(file, rank)] = value;
    }

    public IReadOnlyCollection<MoveUndoState> History => _history;

    public Board()
    {
        SetupInitialPosition();
    }

    public void Clear()
    {
        Array.Clear(_squares, 0, 64);
        _history.Clear();
        ActiveColor = PieceColor.White;
        CastlingRights = CastlingRights.None;
        EnPassantSquare = null;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
    }

    public void SetupInitialPosition()
    {
        Clear();

        // White pieces
        this[0, 0] = new Piece(PieceType.Rook, PieceColor.White);
        this[1, 0] = new Piece(PieceType.Knight, PieceColor.White);
        this[2, 0] = new Piece(PieceType.Bishop, PieceColor.White);
        this[3, 0] = new Piece(PieceType.Queen, PieceColor.White);
        this[4, 0] = new Piece(PieceType.King, PieceColor.White);
        this[5, 0] = new Piece(PieceType.Bishop, PieceColor.White);
        this[6, 0] = new Piece(PieceType.Knight, PieceColor.White);
        this[7, 0] = new Piece(PieceType.Rook, PieceColor.White);
        for (int f = 0; f < 8; f++)
        {
            this[f, 1] = new Piece(PieceType.Pawn, PieceColor.White);
        }

        // Black pieces
        this[0, 7] = new Piece(PieceType.Rook, PieceColor.Black);
        this[1, 7] = new Piece(PieceType.Knight, PieceColor.Black);
        this[2, 7] = new Piece(PieceType.Bishop, PieceColor.Black);
        this[3, 7] = new Piece(PieceType.Queen, PieceColor.Black);
        this[4, 7] = new Piece(PieceType.King, PieceColor.Black);
        this[5, 7] = new Piece(PieceType.Bishop, PieceColor.Black);
        this[6, 7] = new Piece(PieceType.Knight, PieceColor.Black);
        this[7, 7] = new Piece(PieceType.Rook, PieceColor.Black);
        for (int f = 0; f < 8; f++)
        {
            this[f, 6] = new Piece(PieceType.Pawn, PieceColor.Black);
        }

        ActiveColor = PieceColor.White;
        CastlingRights = CastlingRights.All;
        EnPassantSquare = null;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
    }

    public int FindKing(PieceColor color)
    {
        for (int i = 0; i < 64; i++)
        {
            if (_squares[i].Type == PieceType.King && _squares[i].Color == color)
            {
                return i;
            }
        }
        return -1;
    }

    public bool IsKingInCheck(PieceColor color)
    {
        int kingSq = FindKing(color);
        if (kingSq < 0) return false;
        var enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        return IsSquareAttacked(kingSq, enemy);
    }

    public bool IsSquareAttacked(int targetSq, PieceColor attackerColor)
    {
        int tf = Square.File(targetSq);
        int tr = Square.Rank(targetSq);

        // 1. Pawn attacks against targetSq
        int pawnRank = attackerColor == PieceColor.White ? tr - 1 : tr + 1;
        if (pawnRank >= 0 && pawnRank < 8)
        {
            if (tf > 0)
            {
                var p = this[tf - 1, pawnRank];
                if (p.Type == PieceType.Pawn && p.Color == attackerColor) return true;
            }
            if (tf < 7)
            {
                var p = this[tf + 1, pawnRank];
                if (p.Type == PieceType.Pawn && p.Color == attackerColor) return true;
            }
        }

        // 2. Knight attacks
        int[] kf = [1, 2, 2, 1, -1, -2, -2, -1];
        int[] kr = [2, 1, -1, -2, -2, -1, 1, 2];
        for (int i = 0; i < 8; i++)
        {
            int nf = tf + kf[i];
            int nr = tr + kr[i];
            if (Square.IsValid(nf, nr))
            {
                var p = this[nf, nr];
                if (p.Type == PieceType.Knight && p.Color == attackerColor) return true;
            }
        }

        // 3. King attacks (1 square radius)
        int[] kingD = [-1, 0, 1];
        foreach (int df in kingD)
        {
            foreach (int dr in kingD)
            {
                if (df == 0 && dr == 0) continue;
                int kxf = tf + df;
                int kxr = tr + dr;
                if (Square.IsValid(kxf, kxr))
                {
                    var p = this[kxf, kxr];
                    if (p.Type == PieceType.King && p.Color == attackerColor) return true;
                }
            }
        }

        // 4. Straight rays (Rook / Queen)
        int[][] orthogonalDirs = [[0, 1], [0, -1], [1, 0], [-1, 0]];
        foreach (var dir in orthogonalDirs)
        {
            int cf = tf + dir[0];
            int cr = tr + dir[1];
            while (Square.IsValid(cf, cr))
            {
                var p = this[cf, cr];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackerColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen))
                    {
                        return true;
                    }
                    break;
                }
                cf += dir[0];
                cr += dir[1];
            }
        }

        // 5. Diagonal rays (Bishop / Queen)
        int[][] diagonalDirs = [[1, 1], [1, -1], [-1, 1], [-1, -1]];
        foreach (var dir in diagonalDirs)
        {
            int cf = tf + dir[0];
            int cr = tr + dir[1];
            while (Square.IsValid(cf, cr))
            {
                var p = this[cf, cr];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackerColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen))
                    {
                        return true;
                    }
                    break;
                }
                cf += dir[0];
                cr += dir[1];
            }
        }

        return false;
    }

    public MoveUndoState MakeMove(Move move, string san = "")
    {
        var movingPiece = _squares[move.From];
        var capturedPiece = _squares[move.To];

        var oldCastling = CastlingRights;
        var oldEp = EnPassantSquare;
        var oldHalfmove = HalfmoveClock;

        // Reset EnPassant target square for next turn by default
        EnPassantSquare = null;

        // Halfmove clock resets on pawn move or capture
        if (movingPiece.Type == PieceType.Pawn || !capturedPiece.IsEmpty || move.IsEnPassant)
        {
            HalfmoveClock = 0;
        }
        else
        {
            HalfmoveClock++;
        }

        // Handle En Passant capture
        if (move.IsEnPassant)
        {
            int capSq = movingPiece.Color == PieceColor.White ? move.To - 8 : move.To + 8;
            capturedPiece = _squares[capSq];
            _squares[capSq] = Piece.Empty;
        }

        // Handle Castling
        if (move.IsCastling)
        {
            if (move.To == 6) // White Kingside (g1)
            {
                _squares[5] = _squares[7]; // Rook f1
                _squares[7] = Piece.Empty;
            }
            else if (move.To == 2) // White Queenside (c1)
            {
                _squares[3] = _squares[0]; // Rook d1
                _squares[0] = Piece.Empty;
            }
            else if (move.To == 62) // Black Kingside (g8)
            {
                _squares[61] = _squares[63]; // Rook f8
                _squares[63] = Piece.Empty;
            }
            else if (move.To == 58) // Black Queenside (c8)
            {
                _squares[59] = _squares[56]; // Rook d8
                _squares[56] = Piece.Empty;
            }
        }

        // Set en passant target if double pawn push
        if (move.IsDoublePawnPush)
        {
            EnPassantSquare = movingPiece.Color == PieceColor.White ? move.From + 8 : move.From - 8;
        }

        // Move the piece
        _squares[move.From] = Piece.Empty;
        if (move.Promotion != PieceType.None)
        {
            _squares[move.To] = new Piece(move.Promotion, movingPiece.Color);
        }
        else
        {
            _squares[move.To] = movingPiece;
        }

        // Update castling rights if King moves or Rook moves/captured
        UpdateCastlingRightsOnMove(move.From, move.To, movingPiece.Type);

        if (ActiveColor == PieceColor.Black)
        {
            FullmoveNumber++;
            ActiveColor = PieceColor.White;
        }
        else
        {
            ActiveColor = PieceColor.Black;
        }

        var state = new MoveUndoState(move, capturedPiece, oldCastling, oldEp, oldHalfmove, san);
        _history.Push(state);
        return state;
    }

    public bool UndoMove()
    {
        if (_history.Count == 0) return false;

        var state = _history.Pop();
        var move = state.Move;

        // Switch active color back
        if (ActiveColor == PieceColor.White)
        {
            ActiveColor = PieceColor.Black;
            FullmoveNumber--;
        }
        else
        {
            ActiveColor = PieceColor.White;
        }

        CastlingRights = state.CastlingRights;
        EnPassantSquare = state.EnPassantSquare;
        HalfmoveClock = state.HalfmoveClock;

        var pieceOnTo = _squares[move.To];
        var restoredPiece = move.Promotion != PieceType.None
            ? new Piece(PieceType.Pawn, pieceOnTo.Color)
            : pieceOnTo;

        _squares[move.From] = restoredPiece;
        _squares[move.To] = state.CapturedPiece;

        // Undo En Passant
        if (move.IsEnPassant)
        {
            _squares[move.To] = Piece.Empty;
            int capSq = restoredPiece.Color == PieceColor.White ? move.To - 8 : move.To + 8;
            _squares[capSq] = state.CapturedPiece;
        }

        // Undo Castling
        if (move.IsCastling)
        {
            if (move.To == 6) // White Kingside
            {
                _squares[7] = _squares[5];
                _squares[5] = Piece.Empty;
            }
            else if (move.To == 2) // White Queenside
            {
                _squares[0] = _squares[3];
                _squares[3] = Piece.Empty;
            }
            else if (move.To == 62) // Black Kingside
            {
                _squares[63] = _squares[61];
                _squares[61] = Piece.Empty;
            }
            else if (move.To == 58) // Black Queenside
            {
                _squares[56] = _squares[59];
                _squares[59] = Piece.Empty;
            }
        }

        return true;
    }

    private void UpdateCastlingRightsOnMove(int from, int to, PieceType movingType)
    {
        if (movingType == PieceType.King)
        {
            if (from == 4) // White King
                CastlingRights &= ~CastlingRights.WhiteAll;
            else if (from == 60) // Black King
                CastlingRights &= ~CastlingRights.BlackAll;
        }

        // If Rook moves
        if (from == 0) CastlingRights &= ~CastlingRights.WhiteQueenside;
        else if (from == 7) CastlingRights &= ~CastlingRights.WhiteKingside;
        else if (from == 56) CastlingRights &= ~CastlingRights.BlackQueenside;
        else if (from == 63) CastlingRights &= ~CastlingRights.BlackKingside;

        // If Rook is captured on its home square
        if (to == 0) CastlingRights &= ~CastlingRights.WhiteQueenside;
        else if (to == 7) CastlingRights &= ~CastlingRights.WhiteKingside;
        else if (to == 56) CastlingRights &= ~CastlingRights.BlackQueenside;
        else if (to == 63) CastlingRights &= ~CastlingRights.BlackKingside;
    }

    public Board Clone()
    {
        var clone = new Board();
        Array.Copy(_squares, clone._squares, 64);
        clone.ActiveColor = ActiveColor;
        clone.CastlingRights = CastlingRights;
        clone.EnPassantSquare = EnPassantSquare;
        clone.HalfmoveClock = HalfmoveClock;
        clone.FullmoveNumber = FullmoveNumber;
        return clone;
    }

    // Classic Simplified Piece-Square Tables (midgame)
    private static readonly int[] PstPawn =
    [
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0
    ];

    private static readonly int[] PstKnight =
    [
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50
    ];

    private static readonly int[] PstBishop =
    [
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20
    ];

    private static readonly int[] PstRook =
    [
          0,  0,  0,  0,  0,  0,  0,  0,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  0,  5,  5,  0,  0,  0
    ];

    private static readonly int[] PstQueen =
    [
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    ];

    private static readonly int[] PstKingMid =
    [
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20
    ];

    public int Evaluate()
    {
        int score = 0;

        for (int sq = 0; sq < 64; sq++)
        {
            var p = _squares[sq];
            if (p.IsEmpty) continue;

            int pieceVal = p.BaseValue;
            int pstVal = GetPstValue(p.Type, p.Color, sq);

            int totalPieceScore = pieceVal + pstVal;
            if (p.Color == PieceColor.White)
                score += totalPieceScore;
            else
                score -= totalPieceScore;
        }

        return ActiveColor == PieceColor.White ? score : -score;
    }

    private static int GetPstValue(PieceType type, PieceColor color, int sq)
    {
        // Tables are defined from White's perspective (rank 7 top, rank 0 bottom).
        // Since square 0 is a1 (bottom left), table index is (7 - rank) * 8 + file.
        int file = Square.File(sq);
        int rank = Square.Rank(sq);

        int tableIdx = color == PieceColor.White
            ? (7 - rank) * 8 + file
            : rank * 8 + file;

        return type switch
        {
            PieceType.Pawn => PstPawn[tableIdx],
            PieceType.Knight => PstKnight[tableIdx],
            PieceType.Bishop => PstBishop[tableIdx],
            PieceType.Rook => PstRook[tableIdx],
            PieceType.Queen => PstQueen[tableIdx],
            PieceType.King => PstKingMid[tableIdx],
            _ => 0
        };
    }
}
