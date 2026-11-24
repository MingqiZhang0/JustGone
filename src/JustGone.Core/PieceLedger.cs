using System;
using System.Collections.Generic;

namespace JustGone.Core
{
    /// <summary>
    /// Deterministic view operation order: Remove -> Move -> Add -> Update.
    /// Generalized from WeiXiangQi v0.10.10.0 (shared_p4/WxqViewPlan.cs and WxqViewLedger.cs).
    /// </summary>
    public enum PieceDeltaOpKind
    {
        Remove = 0,
        Move = 1,
        Add = 2,
        Update = 3
    }

    public struct LedgerPiece<TData>
    {
        public int PieceId;
        public BoardCoord Coord;
        public TData Data;

        public LedgerPiece(int pieceId, BoardCoord coord, TData data)
        {
            PieceId = pieceId;
            Coord = coord;
            Data = data;
        }
    }

    public struct PieceDeltaOp<TData>
    {
        public PieceDeltaOpKind Kind;
        public int PieceId;
        public BoardCoord From;
        public BoardCoord To;
        public TData Data;
    }

    /// <summary>
    /// Pure-logic bidirectional PieceId to BoardCoord ledger with deterministic diff planning
    /// and consistency verification. Supports arbitrary board sizes (9/13/19).
    /// </summary>
    public sealed class PieceLedger<TData>
    {
        private readonly Dictionary<int, LedgerPiece<TData>> _byId = new Dictionary<int, LedgerPiece<TData>>();
        private readonly Dictionary<int, int> _idByCellKey = new Dictionary<int, int>();

        public int BoardSize { get; }
        public int Count => _byId.Count;

        public PieceLedger(int boardSize = 19)
        {
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));
            BoardSize = boardSize;
        }

        public void Clear()
        {
            _byId.Clear();
            _idByCellKey.Clear();
        }

        public void Set(int pieceId, BoardCoord coord, TData data)
        {
            if (pieceId <= 0) throw new ArgumentOutOfRangeException(nameof(pieceId), "PieceId must be positive.");
            if (!coord.InBounds(BoardSize)) throw new ArgumentOutOfRangeException(nameof(coord), "Coord out of bounds.");

            if (_byId.TryGetValue(pieceId, out LedgerPiece<TData> prev))
            {
                int prevKey = prev.Coord.ToKey(BoardSize);
                if (_idByCellKey.TryGetValue(prevKey, out int mappedId) && mappedId == pieceId)
                    _idByCellKey.Remove(prevKey);
            }

            int key = coord.ToKey(BoardSize);
            if (_idByCellKey.TryGetValue(key, out int occupantId) && occupantId != pieceId)
            {
                _byId.Remove(occupantId);
            }

            var entry = new LedgerPiece<TData>(pieceId, coord, data);
            _byId[pieceId] = entry;
            _idByCellKey[key] = pieceId;
        }

        public bool RemoveById(int pieceId)
        {
            if (!_byId.TryGetValue(pieceId, out LedgerPiece<TData> entry)) return false;
            _byId.Remove(pieceId);
            int key = entry.Coord.ToKey(BoardSize);
            if (_idByCellKey.TryGetValue(key, out int mapped) && mapped == pieceId)
                _idByCellKey.Remove(key);
            return true;
        }

        public bool RemoveAt(BoardCoord coord)
        {
            if (!coord.InBounds(BoardSize)) return false;
            int key = coord.ToKey(BoardSize);
            if (!_idByCellKey.TryGetValue(key, out int pieceId)) return false;
            return RemoveById(pieceId);
        }

        public bool TryGetById(int pieceId, out LedgerPiece<TData> piece)
        {
            return _byId.TryGetValue(pieceId, out piece);
        }

        public bool TryGetAt(BoardCoord coord, out LedgerPiece<TData> piece)
        {
            piece = default;
            if (!coord.InBounds(BoardSize)) return false;
            int key = coord.ToKey(BoardSize);
            if (!_idByCellKey.TryGetValue(key, out int pieceId)) return false;
            return _byId.TryGetValue(pieceId, out piece);
        }

        public bool HasAt(BoardCoord coord)
        {
            return coord.InBounds(BoardSize) && _idByCellKey.ContainsKey(coord.ToKey(BoardSize));
        }

        public IEnumerable<LedgerPiece<TData>> EnumeratePieces() => _byId.Values;

        /// <summary>
        /// Computes deterministic operations (Remove -> Move -> Add -> Update) to transition
        /// the current ledger state to <paramref name="desired"/>.
        /// </summary>
        public List<PieceDeltaOp<TData>> PlanTransition(
            IReadOnlyList<LedgerPiece<TData>> desired,
            IEqualityComparer<TData> dataComparer = null)
        {
            var comparer = dataComparer ?? EqualityComparer<TData>.Default;
            var ops = new List<PieceDeltaOp<TData>>();
            var targetById = new Dictionary<int, LedgerPiece<TData>>();

            if (desired != null)
            {
                for (int i = 0; i < desired.Count; i++)
                    targetById[desired[i].PieceId] = desired[i];
            }

            // 1) Removals first so vacated cells are freed before moves/adds.
            foreach (KeyValuePair<int, LedgerPiece<TData>> kv in _byId)
            {
                if (!targetById.ContainsKey(kv.Key))
                {
                    ops.Add(new PieceDeltaOp<TData>
                    {
                        Kind = PieceDeltaOpKind.Remove,
                        PieceId = kv.Key,
                        From = kv.Value.Coord,
                        To = BoardCoord.Invalid,
                        Data = kv.Value.Data
                    });
                }
            }

            // 2) Moves
            foreach (KeyValuePair<int, LedgerPiece<TData>> kv in targetById)
            {
                if (_byId.TryGetValue(kv.Key, out LedgerPiece<TData> current) && current.Coord != kv.Value.Coord)
                {
                    ops.Add(new PieceDeltaOp<TData>
                    {
                        Kind = PieceDeltaOpKind.Move,
                        PieceId = kv.Key,
                        From = current.Coord,
                        To = kv.Value.Coord,
                        Data = kv.Value.Data
                    });
                }
            }

            // 3) Adds
            foreach (KeyValuePair<int, LedgerPiece<TData>> kv in targetById)
            {
                if (!_byId.ContainsKey(kv.Key))
                {
                    ops.Add(new PieceDeltaOp<TData>
                    {
                        Kind = PieceDeltaOpKind.Add,
                        PieceId = kv.Key,
                        From = BoardCoord.Invalid,
                        To = kv.Value.Coord,
                        Data = kv.Value.Data
                    });
                }
            }

            // 4) In-place visual/data updates
            foreach (KeyValuePair<int, LedgerPiece<TData>> kv in targetById)
            {
                if (_byId.TryGetValue(kv.Key, out LedgerPiece<TData> current)
                    && current.Coord == kv.Value.Coord
                    && !comparer.Equals(current.Data, kv.Value.Data))
                {
                    ops.Add(new PieceDeltaOp<TData>
                    {
                        Kind = PieceDeltaOpKind.Update,
                        PieceId = kv.Key,
                        From = current.Coord,
                        To = kv.Value.Coord,
                        Data = kv.Value.Data
                    });
                }
            }

            return ops;
        }

        public void ApplyOps(IReadOnlyList<PieceDeltaOp<TData>> ops)
        {
            if (ops == null) return;
            for (int i = 0; i < ops.Count; i++)
            {
                PieceDeltaOp<TData> op = ops[i];
                switch (op.Kind)
                {
                    case PieceDeltaOpKind.Remove:
                        RemoveById(op.PieceId);
                        break;
                    case PieceDeltaOpKind.Move:
                    case PieceDeltaOpKind.Add:
                    case PieceDeltaOpKind.Update:
                        Set(op.PieceId, op.To, op.Data);
                        break;
                }
            }
        }

        public List<string> VerifyIntegrity()
        {
            var issues = new List<string>();
            if (_byId.Count != _idByCellKey.Count)
                issues.Add("index-count-mismatch(byId=" + _byId.Count + ",byCell=" + _idByCellKey.Count + ")");

            foreach (KeyValuePair<int, LedgerPiece<TData>> kv in _byId)
            {
                if (!kv.Value.Coord.InBounds(BoardSize))
                {
                    issues.Add("out-of-bounds#" + kv.Key + "@" + kv.Value.Coord);
                    continue;
                }
                int key = kv.Value.Coord.ToKey(BoardSize);
                if (!_idByCellKey.TryGetValue(key, out int mapped) || mapped != kv.Key)
                    issues.Add("cell-map-mismatch#" + kv.Key + "@" + kv.Value.Coord);
            }
            return issues;
        }
    }
}
