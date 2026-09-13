using System;
using System.Collections.Generic;

namespace FryPdf.Plugin.Chess.Engine;

public static class MoveGenerator
{
    private static readonly int[] KnightFileDeltas = [1, 2, 2, 1, -1, -2, -2, -1];
    private static readonly int[] KnightRankDeltas = [2, 1, -1, -2, -2, -1, 1, 2];

    private static readonly int[][] BishopDirs = [[1, 1], [1, -1], [-1, 1], [-1, -1]];
    private static readonly int[][] RookDirs = [[0, 1], [0, -1], [1, 0], [-1, 0]];
    private static readonly int[][] QueenDirs = [[1, 1], [1, -1], [-1, 1], [-1, -1], [0, 1], [0, -1], [1, 0], [-1, 0]];

    public static List<Move> GenerateLegalMoves(Board board, PieceColor color)
    {
        var pseudo = GeneratePseudoLegalMoves(board, color);
        var legal = new List<Move>(pseudo.Count);

        foreach (var move in pseudo)
        {
            board.MakeMove(move);
            if (!board.IsKingInCheck(color))
            {
                legal.Add(move);
            }
            board.UndoMove();
        }

        return legal;
    }

    public static List<Move> GeneratePseudoLegalMoves(Board board, PieceColor color, bool capturesOnly = false)
    {
        var moves = new List<Move>(35);

        for (int sq = 0; sq < 64; sq++)
        {
            var p = board[sq];
            if (p.IsEmpty || p.Color != color) continue;

            switch (p.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(board, sq, color, moves, capturesOnly);
                    break;
                case PieceType.Knight:
                    GenerateKnightMoves(board, sq, color, moves, capturesOnly);
                    break;
                case PieceType.Bishop:
                    GenerateRayMoves(board, sq, color, BishopDirs, moves, capturesOnly);
                    break;
                case PieceType.Rook:
                    GenerateRayMoves(board, sq, color, RookDirs, moves, capturesOnly);
                    break;
                case PieceType.Queen:
                    GenerateRayMoves(board, sq, color, QueenDirs, moves, capturesOnly);
                    break;
                case PieceType.King:
                    GenerateKingMoves(board, sq, color, moves, capturesOnly);
                    break;
            }
        }

        return moves;
    }

    private static void GeneratePawnMoves(Board board, int sq, PieceColor color, List<Move> moves, bool capturesOnly)
    {
        int f = Square.File(sq);
        int r = Square.Rank(sq);

        int forwardDir = color == PieceColor.White ? 1 : -1;
        int startRank = color == PieceColor.White ? 1 : 6;
        int promoRank = color == PieceColor.White ? 7 : 0;

        // 1. Forward 1 step
        int nextR = r + forwardDir;
        if (!capturesOnly && Square.IsValid(f, nextR) && board[f, nextR].IsEmpty)
        {
            if (nextR == promoRank)
            {
                AddPromotionMoves(sq, Square.Index(f, nextR), moves);
            }
            else
            {
                moves.Add(new Move(sq, Square.Index(f, nextR)));

                // Forward 2 steps from starting rank
                int doubleR = r + (forwardDir * 2);
                if (r == startRank && board[f, doubleR].IsEmpty)
                {
                    moves.Add(new Move(sq, Square.Index(f, doubleR), isDoublePawnPush: true));
                }
            }
        }

        // 2. Diagonal captures
        int[] captureFiles = [f - 1, f + 1];
        foreach (int capF in captureFiles)
        {
            if (!Square.IsValid(capF, nextR)) continue;

            var target = board[capF, nextR];
            int destSq = Square.Index(capF, nextR);

            // Regular enemy capture
            if (!target.IsEmpty && target.Color != color)
            {
                if (nextR == promoRank)
                {
                    AddPromotionMoves(sq, destSq, moves);
                }
                else
                {
                    moves.Add(new Move(sq, destSq));
                }
            }
            // En Passant capture
            else if (board.EnPassantSquare.HasValue && board.EnPassantSquare.Value == destSq)
            {
                moves.Add(new Move(sq, destSq, isEnPassant: true));
            }
        }
    }

    private static void AddPromotionMoves(int from, int to, List<Move> moves)
    {
        moves.Add(new Move(from, to, PieceType.Queen));
        moves.Add(new Move(from, to, PieceType.Rook));
        moves.Add(new Move(from, to, PieceType.Bishop));
        moves.Add(new Move(from, to, PieceType.Knight));
    }

    private static void GenerateKnightMoves(Board board, int sq, PieceColor color, List<Move> moves, bool capturesOnly)
    {
        int f = Square.File(sq);
        int r = Square.Rank(sq);

        for (int i = 0; i < 8; i++)
        {
            int tf = f + KnightFileDeltas[i];
            int tr = r + KnightRankDeltas[i];

            if (!Square.IsValid(tf, tr)) continue;

            var target = board[tf, tr];
            if (target.IsEmpty)
            {
                if (!capturesOnly) moves.Add(new Move(sq, Square.Index(tf, tr)));
            }
            else if (target.Color != color)
            {
                moves.Add(new Move(sq, Square.Index(tf, tr)));
            }
        }
    }

    private static void GenerateRayMoves(Board board, int sq, PieceColor color, int[][] dirs, List<Move> moves, bool capturesOnly)
    {
        int f = Square.File(sq);
        int r = Square.Rank(sq);

        foreach (var dir in dirs)
        {
            int tf = f + dir[0];
            int tr = r + dir[1];

            while (Square.IsValid(tf, tr))
            {
                var target = board[tf, tr];
                if (target.IsEmpty)
                {
                    if (!capturesOnly) moves.Add(new Move(sq, Square.Index(tf, tr)));
                }
                else
                {
                    if (target.Color != color)
                    {
                        moves.Add(new Move(sq, Square.Index(tf, tr)));
                    }
                    break; // Blocked by piece
                }

                tf += dir[0];
                tr += dir[1];
            }
        }
    }

    private static void GenerateKingMoves(Board board, int sq, PieceColor color, List<Move> moves, bool capturesOnly)
    {
        int f = Square.File(sq);
        int r = Square.Rank(sq);

        for (int df = -1; df <= 1; df++)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                if (df == 0 && dr == 0) continue;
                int tf = f + df;
                int tr = r + dr;

                if (!Square.IsValid(tf, tr)) continue;

                var target = board[tf, tr];
                if (target.IsEmpty)
                {
                    if (!capturesOnly) moves.Add(new Move(sq, Square.Index(tf, tr)));
                }
                else if (target.Color != color)
                {
                    moves.Add(new Move(sq, Square.Index(tf, tr)));
                }
            }
        }

        // Castling (cannot castle while in check or capturesOnly)
        if (capturesOnly) return;

        var enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        if (board.IsKingInCheck(color)) return;

        if (color == PieceColor.White && sq == 4) // e1
        {
            // Kingside (e1 to g1)
            if ((board.CastlingRights & CastlingRights.WhiteKingside) != 0 &&
                board[5].IsEmpty && board[6].IsEmpty &&
                !board.IsSquareAttacked(5, enemy) && !board.IsSquareAttacked(6, enemy) &&
                board[7].Type == PieceType.Rook && board[7].Color == PieceColor.White)
            {
                moves.Add(new Move(4, 6, isCastling: true));
            }

            // Queenside (e1 to c1)
            if ((board.CastlingRights & CastlingRights.WhiteQueenside) != 0 &&
                board[1].IsEmpty && board[2].IsEmpty && board[3].IsEmpty &&
                !board.IsSquareAttacked(3, enemy) && !board.IsSquareAttacked(2, enemy) &&
                board[0].Type == PieceType.Rook && board[0].Color == PieceColor.White)
            {
                moves.Add(new Move(4, 2, isCastling: true));
            }
        }
        else if (color == PieceColor.Black && sq == 60) // e8
        {
            // Kingside (e8 to g8)
            if ((board.CastlingRights & CastlingRights.BlackKingside) != 0 &&
                board[61].IsEmpty && board[62].IsEmpty &&
                !board.IsSquareAttacked(61, enemy) && !board.IsSquareAttacked(62, enemy) &&
                board[63].Type == PieceType.Rook && board[63].Color == PieceColor.Black)
            {
                moves.Add(new Move(60, 62, isCastling: true));
            }

            // Queenside (e8 to c8)
            if ((board.CastlingRights & CastlingRights.BlackQueenside) != 0 &&
                board[57].IsEmpty && board[58].IsEmpty && board[59].IsEmpty &&
                !board.IsSquareAttacked(59, enemy) && !board.IsSquareAttacked(58, enemy) &&
                board[56].Type == PieceType.Rook && board[56].Color == PieceColor.Black)
            {
                moves.Add(new Move(60, 58, isCastling: true));
            }
        }
    }

    public static string GenerateSan(Board board, Move move, List<Move> legalMoves)
    {
        if (move.IsCastling)
        {
            return move.To > move.From ? "O-O" : "O-O-O";
        }

        var movingPiece = board[move.From];
        var destPiece = board[move.To];
        bool isCapture = !destPiece.IsEmpty || move.IsEnPassant;

        string pieceLetter = movingPiece.Type switch
        {
            PieceType.Knight => "N",
            PieceType.Bishop => "B",
            PieceType.Rook => "R",
            PieceType.Queen => "Q",
            PieceType.King => "K",
            _ => ""
        };

        string disambiguation = "";
        if (movingPiece.Type != PieceType.Pawn && movingPiece.Type != PieceType.King)
        {
            // Check if other pieces of the same type can move to the same square
            bool sameFile = false;
            bool sameRank = false;
            bool duplicate = false;

            foreach (var alt in legalMoves)
            {
                if (alt.From != move.From && alt.To == move.To && board[alt.From].Type == movingPiece.Type)
                {
                    duplicate = true;
                    if (Square.File(alt.From) == Square.File(move.From)) sameFile = true;
                    if (Square.Rank(alt.From) == Square.Rank(move.From)) sameRank = true;
                }
            }

            if (duplicate)
            {
                if (!sameFile) disambiguation = ((char)('a' + Square.File(move.From))).ToString();
                else if (!sameRank) disambiguation = ((char)('1' + Square.Rank(move.From))).ToString();
                else disambiguation = Square.ToAlgebraic(move.From);
            }
        }

        string captureStr = "";
        if (isCapture)
        {
            if (movingPiece.Type == PieceType.Pawn)
                captureStr = $"{(char)('a' + Square.File(move.From))}x";
            else
                captureStr = "x";
        }

        string destStr = Square.ToAlgebraic(move.To);

        string promoStr = move.Promotion switch
        {
            PieceType.Queen => "=Q",
            PieceType.Rook => "=R",
            PieceType.Bishop => "=B",
            PieceType.Knight => "=N",
            _ => ""
        };

        // Simulate move to check for check/checkmate
        board.MakeMove(move);
        var opponent = movingPiece.Color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        bool inCheck = board.IsKingInCheck(opponent);
        var oppMoves = GenerateLegalMoves(board, opponent);
        bool checkmate = inCheck && oppMoves.Count == 0;
        board.UndoMove();

        string checkStr = checkmate ? "#" : inCheck ? "+" : "";

        return $"{pieceLetter}{disambiguation}{captureStr}{destStr}{promoStr}{checkStr}";
    }
}
