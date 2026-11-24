using System;

namespace JustGone.Core
{
    /// <summary>
    /// Immutable board grid coordinate (0-based X/Y).
    /// Generalized from WeiXiangQi v0.10.10.0 (shared_p4/WxqCoreTypes.cs WxqCell) to support arbitrary board sizes (9/13/19).
    /// </summary>
    public struct BoardCoord : IEquatable<BoardCoord>
    {
        public static readonly BoardCoord Invalid = new BoardCoord(-1, -1);

        public readonly int X;
        public readonly int Y;

        public BoardCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool IsValid => X >= 0 && Y >= 0;

        public bool InBounds(int boardSize)
        {
            return boardSize > 0 && X >= 0 && X < boardSize && Y >= 0 && Y < boardSize;
        }

        public int ToKey(int boardSize)
        {
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));
            return Y * boardSize + X;
        }

        public static BoardCoord FromKey(int key, int boardSize)
        {
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));
            if (key < 0 || key >= boardSize * boardSize) return Invalid;
            return new BoardCoord(key % boardSize, key / boardSize);
        }

        /// <summary>
        /// Formats coordinate in standard Go GTP notation (A1..T19, skipping 'I').
        /// </summary>
        public string ToGtp(int boardSize = 19)
        {
            if (!InBounds(boardSize)) return "INVALID";
            char col = (char)('A' + X + (X >= 8 ? 1 : 0));
            int row = boardSize - Y;
            return col.ToString() + row;
        }

        public static bool TryParseGtp(string text, int boardSize, out BoardCoord coord)
        {
            coord = Invalid;
            if (string.IsNullOrWhiteSpace(text) || boardSize <= 0) return false;
            string s = text.Trim().ToUpperInvariant();
            if (s.Length < 2) return false;
            char c = s[0];
            if (c < 'A' || c > 'Z' || c == 'I') return false;
            int x = (c - 'A') - (c > 'I' ? 1 : 0);
            if (!int.TryParse(s.Substring(1), out int row)) return false;
            int y = boardSize - row;
            var candidate = new BoardCoord(x, y);
            if (!candidate.InBounds(boardSize)) return false;
            coord = candidate;
            return true;
        }

        public bool Equals(BoardCoord other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is BoardCoord other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public static bool operator ==(BoardCoord left, BoardCoord right) => left.Equals(right);

        public static bool operator !=(BoardCoord left, BoardCoord right) => !left.Equals(right);

        public override string ToString() => IsValid ? "(" + X + "," + Y + ")" : "(invalid)";
    }
}
