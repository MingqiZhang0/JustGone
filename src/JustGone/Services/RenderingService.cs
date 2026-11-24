using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Board rendering overlays: 3D cylinder cell markers, native OwnershipPrefab grid overlays,
    /// and native hover ghost stone control.
    /// Extracted and decoupled from WeiXiangQi v0.10.10.0 (WxqMarkers.cs, WxqNativeOwnershipView.cs, WxqSession.cs).
    /// </summary>
    internal sealed class RenderingService : IRenderingService
    {
        private readonly BoardService _board;

        public RenderingService(BoardService board)
        {
            _board = board;
        }

        public IMarkerLayer CreateMarkerLayer(ModContext owner, string layerName)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || gm.StonePanel == null)
                throw new InvalidOperationException("Cannot create marker layer: board / GameManager is not ready.");

            string safeName = BoardService.ManagedNodePrefix + "Markers_"
                + (owner == null ? "anon" : owner.Info.Id) + "_"
                + (string.IsNullOrWhiteSpace(layerName) ? "default" : layerName);

            var layer = new NativeMarkerLayer(gm, _board, safeName);
            return owner != null ? owner.Track(layer) : layer;
        }

        public IGridOverlayLayer CreateGridOverlay(ModContext owner, string layerName)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || gm.OwnershipPrefab == null)
                throw new InvalidOperationException("Cannot create grid overlay: native OwnershipPrefab is not available.");

            string safeName = BoardService.ManagedNodePrefix + "GridOverlay_"
                + (owner == null ? "anon" : owner.Info.Id) + "_"
                + (string.IsNullOrWhiteSpace(layerName) ? "default" : layerName);

            var overlay = new NativeGridOverlay(gm, _board, safeName);
            return owner != null ? owner.Track(overlay) : overlay;
        }

        public void ShowHoverGhost(StoneBaseStyle style, BoardCoord coord)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || !coord.InBounds(_board.BoardSize))
            {
                HideHoverGhost();
                return;
            }

            HideHoverGhost();
            GameObject ghost = style == StoneBaseStyle.Black ? gm.BlackMouseOverStone : gm.WhiteMouseOverStone;
            if (ghost == null) return;
            ghost.SetActive(true);
            ghost.transform.position = _board.CellToWorld(coord);
        }

        public void HideHoverGhost()
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null) return;
            if (gm.BlackMouseOverStone != null && gm.BlackMouseOverStone.activeSelf)
                gm.BlackMouseOverStone.SetActive(false);
            if (gm.WhiteMouseOverStone != null && gm.WhiteMouseOverStone.activeSelf)
                gm.WhiteMouseOverStone.SetActive(false);
        }

        // ------------------------------------------------------------------
        // NativeMarkerLayer
        // ------------------------------------------------------------------

        private sealed class NativeMarkerLayer : IMarkerLayer
        {
            private sealed class MarkerObj
            {
                public GameObject Go;
                public Material Mat;
            }

            private readonly BoardService _board;
            private readonly Transform _root;
            private readonly float _baseDiameter;
            private readonly float _thickness;
            private readonly Dictionary<int, MarkerObj> _markers = new Dictionary<int, MarkerObj>();
            private bool _disposed;

            public string LayerName { get; }
            public int Count => _markers.Count;

            public NativeMarkerLayer(GameManager gm, BoardService board, string rootName)
            {
                _board = board;
                LayerName = rootName;

                var rootGo = new GameObject(rootName);
                _root = rootGo.transform;
                _root.SetParent(gm.StonePanel.transform, false);
                _root.localPosition = Vector3.zero;
                _root.localRotation = Quaternion.identity;
                _root.localScale = Vector3.one;

                float spacing = _board.GetCellSpacingLocal();
                _baseDiameter = spacing * 0.42f;
                _thickness = Mathf.Max(0.0015f, spacing * 0.035f);
            }

            public void SetMarker(BoardCoord coord, Color color, float diameterScale = 1f, float heightScale = 1f)
            {
                if (_disposed || _root == null || !coord.InBounds(_board.BoardSize)) return;

                int key = coord.ToKey(_board.BoardSize);
                if (_markers.TryGetValue(key, out MarkerObj existing))
                {
                    if (existing.Mat != null) UnityEngine.Object.Destroy(existing.Mat);
                    if (existing.Go != null) UnityEngine.Object.Destroy(existing.Go);
                    _markers.Remove(key);
                }

                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "JG_MARK_" + coord.X + "_" + coord.Y;
                Collider col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                go.transform.SetParent(_root, false);
                Vector3 pos = _board.CellToLocal(coord);
                pos.y += _thickness * 0.8f;
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.identity;
                float diameter = _baseDiameter * Mathf.Max(0.1f, diameterScale);
                go.transform.localScale = new Vector3(diameter, _thickness * Mathf.Max(0.1f, heightScale) * 0.5f, diameter);

                Material mat = null;
                Renderer r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                    if (shader != null)
                    {
                        mat = new Material(shader);
                        mat.color = color;
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                        r.material = mat;
                    }
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }

                _markers[key] = new MarkerObj { Go = go, Mat = mat };
            }

            public bool RemoveMarker(BoardCoord coord)
            {
                if (!coord.InBounds(_board.BoardSize)) return false;
                int key = coord.ToKey(_board.BoardSize);
                if (!_markers.TryGetValue(key, out MarkerObj m)) return false;
                if (m.Mat != null) UnityEngine.Object.Destroy(m.Mat);
                if (m.Go != null) UnityEngine.Object.Destroy(m.Go);
                _markers.Remove(key);
                return true;
            }

            public void Clear()
            {
                foreach (MarkerObj m in _markers.Values)
                {
                    if (m.Mat != null) UnityEngine.Object.Destroy(m.Mat);
                    if (m.Go != null) UnityEngine.Object.Destroy(m.Go);
                }
                _markers.Clear();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Clear();
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            }
        }

        // ------------------------------------------------------------------
        // NativeGridOverlay (extracted from WxqNativeOwnershipView.cs)
        // ------------------------------------------------------------------

        private sealed class NativeGridOverlay : IGridOverlayLayer
        {
            private readonly BoardService _board;
            private readonly int _size;
            private readonly GameObject _root;
            private readonly GameObject _panel;
            private readonly bool _panelWasActive;
            private readonly SpriteRenderer[] _sprites;
            private bool _disposed;

            public string LayerName { get; }

            public NativeGridOverlay(GameManager gm, BoardService board, string rootName)
            {
                _board = board;
                _size = board.BoardSize;
                LayerName = rootName;

                _panel = gm.OwnershipPanel;
                if (_panel != null)
                {
                    _panelWasActive = _panel.activeSelf;
                    _panel.SetActive(true);
                }

                _root = new GameObject(rootName);
                Transform parent = _panel != null ? _panel.transform : gm.StonePanel.transform;
                _root.transform.SetParent(parent, false);
                _root.transform.localPosition = Vector3.zero;
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;

                _sprites = new SpriteRenderer[_size * _size];
                for (int y = 0; y < _size; y++)
                {
                    for (int x = 0; x < _size; x++)
                    {
                        GameObject go = UnityEngine.Object.Instantiate(gm.OwnershipPrefab, _root.transform, false);
                        go.name = "JG_Own_" + x + "_" + y;
                        SpriteRenderer sr = go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>(true);
                        if (sr != null)
                        {
                            sr.color = new Color(1f, 1f, 1f, 0f);
                            sr.enabled = false;
                            if (sr.sortingOrder < 1) sr.sortingOrder = 1;
                        }
                        _sprites[y * _size + x] = sr;
                        go.SetActive(true);
                    }
                }
                _root.SetActive(false);
            }

            public void SetVisible(bool visible)
            {
                if (_disposed || _root == null) return;
                if (_root.activeSelf != visible) _root.SetActive(visible);
            }

            public void SetCellColor(BoardCoord coord, Color color)
            {
                if (_disposed || _sprites == null || !coord.InBounds(_size)) return;
                SpriteRenderer sr = _sprites[coord.ToKey(_size)];
                if (sr == null) return;
                sr.enabled = color.a > 0.001f;
                sr.color = color;
                sr.transform.position = _board.CellToWorld(coord) + new Vector3(0f, 0.001f, 0f);
            }

            public void ClearCell(BoardCoord coord)
            {
                if (_disposed || _sprites == null || !coord.InBounds(_size)) return;
                SpriteRenderer sr = _sprites[coord.ToKey(_size)];
                if (sr != null) sr.enabled = false;
            }

            public void ClearAll()
            {
                if (_sprites == null) return;
                for (int i = 0; i < _sprites.Length; i++)
                    if (_sprites[i] != null) _sprites[i].enabled = false;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_root != null) UnityEngine.Object.Destroy(_root);
                if (_panel != null) _panel.SetActive(_panelWasActive);
            }
        }
    }
}
