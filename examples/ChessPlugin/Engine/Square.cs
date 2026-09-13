using System;

namespace FryPdf.Plugin.Chess.Engine;

public static class Square
{
    public static int Index(int file, int rank) => (rank * 8) + file;
    public static int File(int index) => index % 8;
    public static int Rank(int index) => index / 8;

    public static bool IsValid(int file, int rank) => file >= 0 && file < 8 && rank >= 0 && rank < 8;

    public static string ToAlgebraic(int index)
    {
        if (index < 0 || index >= 64) return "-";
        char fileChar = (char)('a' + File(index));
        char rankChar = (char)('1' + Rank(index));
        return $"{fileChar}{rankChar}";
    }

    public static int FromAlgebraic(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 2) return -1;
        int file = s[0] - 'a';
        int rank = s[1] - '1';
        if (!IsValid(file, rank)) return -1;
        return Index(file, rank);
    }
}
