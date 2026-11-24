using System;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Board geometry, coordinate conversions (logical to local to world to screen),
    /// raycast picking, and vanilla stone cleanup.
    /// Generalized from WeiXiangQi v0.10.10.0 (WxqStoneView.cs and WxqSession.cs) to dynamically support
    /// all Just Go board sizes (9/13/19) and environments.
    /// </summary>
    internal sealed class BoardService : IBoardService
    {
        internal const string ManagedNodePrefix = "JG_";

        private BoardCoord _hoveredCell = BoardCoord.Invalid;

        public bool IsAvailable
        {
            get
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                return gm != null && gm.StonePanel != null;
            }
        }

        public int BoardSize => JgGameBridge.GetBoardSize(JgGameBridge.TryGetGameManager());

        public GameEnvironmentKind Environment => JgGameBridge.GetEnvironment(JgGameBridge.TryGetGameManager());

        public BoardCoord HoveredCell => _hoveredCell;

        internal void SetHoveredCell(BoardCoord coord)
        {
            _hoveredCell = coord;
        }

        public Vector3 CellToLocal(BoardCoord coord)
        {
            return JgGameBridge.ConvertCellToLocal(Environment, coord, BoardSize);
        }

        public Vector3 CellToWorld(BoardCoord coord)
        {
            Vector3 local = CellToLocal(coord);
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm != null && gm.StonePanel != null)
                return gm.StonePanel.transform.TransformPoint(local);
            return local;
        }

        public bool TryCellToScreen(BoardCoord coord, out Vector2 screenPos)
        {
            screenPos = Vector2.zero;
            Camera cam = Camera.main;
            if (cam == null || !coord.InBounds(BoardSize)) return false;
            Vector3 sp = cam.WorldToScreenPoint(CellToWorld(coord));
            if (sp.z <= 0f) return false;
            screenPos = new Vector2(sp.x, sp.y);
            return true;
        }

        public bool TryScreenToCell(Vector2 screenPos, out BoardCoord coord)
        {
            coord = BoardCoord.Invalid;
            Camera cam = Camera.main;
            if (cam == null || !IsAvailable) return false;
            return TryRayToCell(cam.ScreenPointToRay(screenPos), out coord);
        }

        public bool TryRayToCell(Ray ray, out BoardCoord coord)
        {
            coord = BoardCoord.Invalid;
            if (!IsAvailable) return false;

            int boardMask = JgGameBridge.GetBoardLayerMask();
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, boardMask))
                return false;

            coord = JgGameBridge.ConvertWorldToCell(Environment, hit.point, BoardSize);
            return coord.IsValid;
        }

        public bool TryGetBoardScreenCenter(out Vector2 screenCenter)
        {
            int centerIndex = BoardSize / 2;
            return TryCellToScreen(new BoardCoord(centerIndex, centerIndex), out screenCenter);
        }

        public float GetCellSpacingLocal()
        {
            if (BoardSize < 2) return 0.02f;
            Vector3 a = CellToLocal(new BoardCoord(0, 0));
            Vector3 b = CellToLocal(new BoardCoord(1, 0));
            return Mathf.Max(0.001f, Vector3.Distance(a, b));
        }

        public int ClearVanillaStones()
        {
            int removed = 0;
            try
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                if (gm == null || gm.StonePanel == null) return 0;

                Transform panel = gm.StonePanel.transform;
                for (int i = panel.childCount - 1; i >= 0; i--)
                {
                    Transform child = panel.GetChild(i);
                    if (child == null) continue;
                    // Preserve all Just Gone managed layers, vanilla hover ghosts, and other mod roots (e.g., WXQ_)
                    if (child.name.StartsWith(ManagedNodePrefix, StringComparison.Ordinal)) continue;
                    if (child.name.StartsWith("WXQ_", StringComparison.Ordinal)) continue;
                    if (gm.BlackMouseOverStone != null && child == gm.BlackMouseOverStone.transform) continue;
                    if (gm.WhiteMouseOverStone != null && child == gm.WhiteMouseOverStone.transform) continue;
                    UnityEngine.Object.Destroy(child.gameObject);
                    removed++;
                }
                JgLog.Info("Cleared " + removed + " vanilla stone object(s) from StonePanel.");
            }
            catch (Exception ex)
            {
                JgLog.Warn("ClearVanillaStones failed: " + ex.Message);
            }
            return removed;
        }
    }
}
