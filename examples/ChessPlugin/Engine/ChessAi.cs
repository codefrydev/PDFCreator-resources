using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FryPdf.Plugin.Chess.Engine;

public static class ChessAi
{
    private static readonly Random RandomGenerator = new();

    public static async Task<Move> FindBestMoveAsync(Board board, PieceColor aiColor, string difficulty, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var legalMoves = MoveGenerator.GenerateLegalMoves(board, aiColor);
            if (legalMoves.Count == 0) return Move.Null;

            ct.ThrowIfCancellationRequested();

            switch (difficulty)
            {
                case "Easy":
                    return PickEasyMove(board, legalMoves, aiColor);

                case "Hard":
                    return PickAlphaBetaMove(board, legalMoves, aiColor, maxDepth: 4, useQuiescence: true, ct);

                case "Medium":
                default:
                    return PickAlphaBetaMove(board, legalMoves, aiColor, maxDepth: 3, useQuiescence: false, ct);
            }
        }, ct);
    }

    private static Move PickEasyMove(Board board, List<Move> moves, PieceColor aiColor)
    {
        // 75% chance to play smart tactical capture/check, 25% random blunder/casual
        if (RandomGenerator.NextDouble() < 0.75)
        {
            // Try to find a good capture or center move
            var scoredMoves = moves.Select(m =>
            {
                var target = board[m.To];
                int score = target.BaseValue;
                if (m.Promotion == PieceType.Queen) score += 800;
                // Favor central squares
                int f = Square.File(m.To);
                int r = Square.Rank(m.To);
                if (f >= 2 && f <= 5 && r >= 2 && r <= 5) score += 15;
                return (Move: m, Score: score);
            }).OrderByDescending(x => x.Score).ToList();

            // Pick randomly among top 3
            int take = Math.Min(3, scoredMoves.Count);
            return scoredMoves[RandomGenerator.Next(take)].Move;
        }

        return moves[RandomGenerator.Next(moves.Count)];
    }

    private static Move PickAlphaBetaMove(Board board, List<Move> legalMoves, PieceColor aiColor, int maxDepth, bool useQuiescence, CancellationToken ct)
    {
        OrderMoves(board, legalMoves);

        Move bestMove = legalMoves[0];
        int bestScore = int.MinValue;
        int alpha = -100000;
        int beta = 100000;

        foreach (var move in legalMoves)
        {
            ct.ThrowIfCancellationRequested();

            board.MakeMove(move);
            int score = -AlphaBeta(board, maxDepth - 1, -beta, -alpha, useQuiescence, ct);
            board.UndoMove();

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            if (score > alpha)
            {
                alpha = score;
            }

            if (alpha >= beta)
            {
                break;
            }
        }

        return bestMove;
    }

    private static int AlphaBeta(Board board, int depth, int alpha, int beta, bool useQuiescence, CancellationToken ct)
    {
        if (depth <= 0)
        {
            return useQuiescence ? Quiescence(board, alpha, beta, 2) : board.Evaluate();
        }

        ct.ThrowIfCancellationRequested();

        var moves = MoveGenerator.GenerateLegalMoves(board, board.ActiveColor);
        if (moves.Count == 0)
        {
            if (board.IsKingInCheck(board.ActiveColor))
            {
                return -20000 - depth; // Checkmated!
            }
            return 0; // Stalemate
        }

        OrderMoves(board, moves);

        foreach (var move in moves)
        {
            board.MakeMove(move);
            int score = -AlphaBeta(board, depth - 1, -beta, -alpha, useQuiescence, ct);
            board.UndoMove();

            if (score >= beta)
            {
                return beta; // Pruning
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }

        return alpha;
    }

    private static int Quiescence(Board board, int alpha, int beta, int maxQDepth)
    {
        int standPat = board.Evaluate();
        if (standPat >= beta) return beta;
        if (alpha < standPat) alpha = standPat;
        if (maxQDepth <= 0) return standPat;

        var captureMoves = MoveGenerator.GeneratePseudoLegalMoves(board, board.ActiveColor, capturesOnly: true);
        OrderMoves(board, captureMoves);

        foreach (var move in captureMoves)
        {
            board.MakeMove(move);
            if (board.IsKingInCheck(board.ActiveColor == PieceColor.White ? PieceColor.Black : PieceColor.White))
            {
                // Illegal move because our king was left in check
                board.UndoMove();
                continue;
            }

            int score = -Quiescence(board, -beta, -alpha, maxQDepth - 1);
            board.UndoMove();

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    private static void OrderMoves(Board board, List<Move> moves)
    {
        // MVV-LVA: Most Valuable Victim - Least Valuable Attacker
        moves.Sort((a, b) =>
        {
            int scoreA = GetMoveScore(board, a);
            int scoreB = GetMoveScore(board, b);
            return scoreB.CompareTo(scoreA);
        });
    }

    private static int GetMoveScore(Board board, Move move)
    {
        int score = 0;
        var captured = board[move.To];
        if (!captured.IsEmpty)
        {
            var attacker = board[move.From];
            score += (captured.BaseValue * 10) - attacker.BaseValue;
        }

        if (move.Promotion != PieceType.None)
        {
            score += 900;
        }

        if (move.IsCastling)
        {
            score += 60;
        }

        return score;
    }
}
