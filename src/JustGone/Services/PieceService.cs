using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using TMPro;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Reusable native stone model layer and 3D display shelf service.
    /// Extracted and decoupled from WeiXiangQi v0.10.10.0 (WxqStoneView.cs and WxqRecruitShelf.cs).
    /// Each MOD receives isolated layer/shelf nodes under StonePanel with full Material/GameObject cleanup.
    /// </summary>
    internal sealed class PieceService : IPieceService
    {
        private readonly BoardService _board;
        private readonly ResourceService _resources;

        public PieceService(BoardService board, ResourceService resources)
        {
            _board = board;
            _resources = resources;
        }

        public IPieceLayer CreateLayer(ModContext owner, string layerName, bool naturalJitter = true)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || gm.StonePanel == null)
                throw new InvalidOperationException("Cannot create piece layer: board / GameManager is not ready.");

            string safeName = BoardService.ManagedNodePrefix + "PieceLayer_"
                + (owner == null ? "anon" : owner.Info.Id) + "_"
                + (string.IsNullOrWhiteSpace(layerName) ? "default" : layerName);

            var layer = new NativePieceLayer(gm, _board, _resources, safeName, naturalJitter);
            return owner != null ? owner.Track(layer) : layer;
        }

        public IDisplayShelf CreateDisplayShelf(ModContext owner, string shelfName)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || gm.StonePanel == null)
                throw new InvalidOperationException("Cannot create display shelf: board / GameManager is not ready.");

            string safeName = BoardService.ManagedNodePrefix + "Shelf_"
                + (owner == null ? "anon" : owner.Info.Id) + "_"
                + (string.IsNullOrWhiteSpace(shelfName) ? "default" : shelfName);

            var shelf = new NativeDisplayShelf(gm, _board, _resources, safeName);
            return owner != null ? owner.Track(shelf) : shelf;
        }

        // ------------------------------------------------------------------
        // NativePieceLayer
        // ------------------------------------------------------------------

        private sealed class NativePieceLayer : IPieceLayer
        {
            private const float JitterAmplitude = 0.0012f;
            private const float DropHeight = 0.035f;
            private const float DropTime = 0.35f;
            private const float DropAudioDelay = 0.012f;
            private const float MoveLiftHeight = 0.02f;
            private const float MoveLiftTime = 0.12f;
            private const float MoveTravelTime = 0.22f;

            private sealed class VisualEntry
            {
                public int PieceId;
                public BoardCoord Coord;
                public PieceVisualSpec Spec;
                public GameObject Go;
                public GoStone Stone;
                public MeshRenderer Renderer;
                public Material InstanceMaterial;
                public Material SharedOriginalMaterial;
                public Vector3 BaseScale;
                public Vector3 Jitter;
            }

            private readonly GameManager _gm;
            private readonly BoardService _board;
            private readonly ResourceService _resources;
            private readonly Transform _root;
            private readonly bool _naturalJitter;
            private readonly float _soundVolume;
            private readonly Dictionary<int, VisualEntry> _byId = new Dictionary<int, VisualEntry>();
            private bool _disposed;

            public string LayerName { get; }
            public int Count => _byId.Count;

            public NativePieceLayer(
                GameManager gm,
                BoardService board,
                ResourceService resources,
                string rootName,
                bool naturalJitter)
            {
                _gm = gm;
                _board = board;
                _resources = resources;
                _naturalJitter = naturalJitter;
                LayerName = rootName;

                var rootGo = new GameObject(rootName);
                _root = rootGo.transform;
                _root.SetParent(gm.StonePanel.transform, false);
                _root.localPosition = Vector3.zero;
                _root.localRotation = Quaternion.identity;
                _root.localScale = Vector3.one;
                _soundVolume = JgGameBridge.GetSoundEffectVolume(gm);
            }

            public bool Spawn(int pieceId, BoardCoord coord, PieceVisualSpec spec, bool animateDrop = true)
            {
                if (_disposed || _root == null || _gm == null) return false;
                if (pieceId <= 0 || !coord.InBounds(_board.BoardSize)) return false;

                spec = spec == null ? new PieceVisualSpec() : spec.Clone();

                if (_byId.TryGetValue(pieceId, out VisualEntry existing))
                    DestroyEntry(existing);

                GameObject prefab = spec.BaseStyle == StoneBaseStyle.Black
                    ? _gm.BlackStonePrefab
                    : _gm.WhiteStonePrefab;
                if (prefab == null)
                {
                    JgLog.Warn("PieceLayer.Spawn failed: native prefab is null for " + spec.BaseStyle);
                    return false;
                }

                GameObject go = UnityEngine.Object.Instantiate(prefab, _root, false);
                GoStone stone = go.GetComponent<GoStone>() ?? go.GetComponentInChildren<GoStone>(true);
                MeshRenderer renderer = go.GetComponentInChildren<MeshRenderer>(true);

                var entry = new VisualEntry
                {
                    PieceId = pieceId,
                    Coord = coord,
                    Spec = spec,
                    Go = go,
                    Stone = stone,
                    Renderer = renderer,
                    SharedOriginalMaterial = renderer == null ? null : renderer.sharedMaterial,
                    BaseScale = go.transform.localScale,
                    Jitter = _naturalJitter
                        ? new Vector3(
                            UnityEngine.Random.Range(-JitterAmplitude, JitterAmplitude),
                            0f,
                            UnityEngine.Random.Range(-JitterAmplitude, JitterAmplitude))
                        : Vector3.zero
                };

                ApplySpec(entry, spec);

                Vector3 target = _board.CellToLocal(coord) + entry.Jitter;
                go.transform.localPosition = animateDrop ? (target + new Vector3(0f, DropHeight, 0f)) : target;
                _byId[pieceId] = entry;

                if (animateDrop) AnimateDrop(entry, target);
                return true;
            }

            public bool Move(int pieceId, BoardCoord targetCoord, bool animateMove = true)
            {
                if (_disposed || !targetCoord.InBounds(_board.BoardSize)) return false;
                if (!_byId.TryGetValue(pieceId, out VisualEntry entry) || entry.Go == null) return false;

                entry.Coord = targetCoord;
                entry.Go.name = "JG_Piece_" + pieceId + "_" + targetCoord.X + "_" + targetCoord.Y;
                Vector3 target = _board.CellToLocal(targetCoord) + entry.Jitter;

                if (!animateMove)
                {
                    entry.Go.transform.localPosition = target;
                    return true;
                }

                Vector3 lift = entry.Go.transform.localPosition + new Vector3(0f, MoveLiftHeight, 0f);
                try { iTween.Stop(entry.Go); } catch { }
                try
                {
                    iTween.MoveTo(entry.Go, iTween.Hash(
                        "position", lift,
                        "islocal", true,
                        "easeType", iTween.EaseType.easeOutQuad,
                        "time", MoveLiftTime));
                    iTween.MoveTo(entry.Go, iTween.Hash(
                        "position", target,
                        "islocal", true,
                        "easeType", iTween.EaseType.easeOutCubic,
                        "time", MoveTravelTime,
                        "delay", MoveLiftTime));
                }
                catch
                {
                    entry.Go.transform.localPosition = target;
                }
                return true;
            }

            public bool UpdateVisual(int pieceId, PieceVisualSpec spec)
            {
                if (_disposed || spec == null) return false;
                if (!_byId.TryGetValue(pieceId, out VisualEntry entry) || entry.Go == null) return false;
                entry.Spec = spec.Clone();
                ApplySpec(entry, entry.Spec);
                return true;
            }

            public bool Remove(int pieceId)
            {
                if (!_byId.TryGetValue(pieceId, out VisualEntry entry)) return false;
                DestroyEntry(entry);
                return true;
            }

            public void SetSelected(int? pieceId)
            {
                foreach (KeyValuePair<int, VisualEntry> kv in _byId)
                {
                    VisualEntry e = kv.Value;
                    if (e == null || e.Stone == null || e.Stone.LastMoveCircle == null) continue;
                    e.Stone.LastMoveCircle.enabled = pieceId.HasValue && kv.Key == pieceId.Value;
                }
            }

            public void PlayDropSound()
            {
                if (_gm == null || _gm.PieceDropAudios == null || _gm.PieceDropAudios.Count == 0) return;
                try
                {
                    AudioSource src = _gm.PieceDropAudios[UnityEngine.Random.Range(0, _gm.PieceDropAudios.Count)];
                    if (src == null || src.isPlaying) return;
                    src.pitch = UnityEngine.Random.Range(0.9f, 1.05f);
                    src.volume = Mathf.Clamp01(_soundVolume * UnityEngine.Random.Range(0.7f, 1f));
                    src.PlayDelayed(DropAudioDelay);
                }
                catch { }
            }

            public bool TryGetPieceCoord(int pieceId, out BoardCoord coord)
            {
                if (_byId.TryGetValue(pieceId, out VisualEntry entry))
                {
                    coord = entry.Coord;
                    return true;
                }
                coord = BoardCoord.Invalid;
                return false;
            }

            public void Clear()
            {
                var ids = new List<int>(_byId.Keys);
                for (int i = 0; i < ids.Count; i++)
                {
                    if (_byId.TryGetValue(ids[i], out VisualEntry e)) DestroyEntry(e);
                }
                _byId.Clear();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Clear();
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            }

            private void ApplySpec(VisualEntry entry, PieceVisualSpec spec)
            {
                entry.Go.name = "JG_Piece_" + entry.PieceId + "_" + entry.Coord.X + "_" + entry.Coord.Y;
                float scale = spec.Scale > 0.01f ? spec.Scale : 1f;
                entry.Go.transform.localScale = entry.BaseScale * scale;

                // Tint material safely without mutating shared prefab material
                if (entry.Renderer != null)
                {
                    if (spec.Tint.HasValue)
                    {
                        if (entry.InstanceMaterial == null && entry.SharedOriginalMaterial != null)
                        {
                            entry.InstanceMaterial = new Material(entry.SharedOriginalMaterial);
                            entry.Renderer.material = entry.InstanceMaterial;
                        }
                        if (entry.InstanceMaterial != null)
                        {
                            Color c = spec.Tint.Value;
                            if (entry.InstanceMaterial.HasProperty("_Color")) entry.InstanceMaterial.SetColor("_Color", c);
                            if (entry.InstanceMaterial.HasProperty("_BaseColor")) entry.InstanceMaterial.SetColor("_BaseColor", c);
                        }
                    }
                    else if (entry.InstanceMaterial != null)
                    {
                        if (entry.SharedOriginalMaterial != null) entry.Renderer.sharedMaterial = entry.SharedOriginalMaterial;
                        UnityEngine.Object.Destroy(entry.InstanceMaterial);
                        entry.InstanceMaterial = null;
                    }
                }

                // Label / glyph via native GoStone.MoveNumberText
                GoStone gs = entry.Stone;
                if (gs != null && gs.LastMove != null && !gs.LastMove.activeSelf)
                    gs.LastMove.SetActive(true);

                TMP_Text label = gs == null ? null : gs.MoveNumberText;
                if (label != null)
                {
                    if (!string.IsNullOrEmpty(spec.LabelText))
                    {
                        if (spec.UseCjkFont)
                        {
                            TMP_FontAsset font = _resources.FindOrCreateFontInternal(spec.LabelText)
                                                 ?? _resources.GetCjkFontInternal();
                            if (font != null) label.font = font;
                        }
                        label.text = spec.LabelText;
                        label.color = spec.LabelColor;
                        label.alignment = TextAlignmentOptions.Center;
                        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                    }
                    else
                    {
                        label.text = string.Empty;
                        if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
                    }
                }
            }

            private static void AnimateDrop(VisualEntry entry, Vector3 target)
            {
                if (entry.Stone != null && entry.Stone.StoneAnimator != null)
                {
                    try { entry.Stone.StoneAnimator.Play("StoneSway_" + UnityEngine.Random.Range(1, 8)); }
                    catch { }
                }
                try
                {
                    iTween.MoveTo(entry.Go, iTween.Hash(
                        "position", target,
                        "islocal", true,
                        "easeType", iTween.EaseType.easeOutCubic,
                        "time", DropTime));
                    iTween.RotateTo(entry.Go, iTween.Hash(
                        "z", 0f,
                        "islocal", true,
                        "easeType", iTween.EaseType.easeOutCubic,
                        "time", DropTime));
                }
                catch
                {
                    entry.Go.transform.localPosition = target;
                }
            }

            private void DestroyEntry(VisualEntry entry)
            {
                if (entry == null) return;
                _byId.Remove(entry.PieceId);
                if (entry.InstanceMaterial != null) UnityEngine.Object.Destroy(entry.InstanceMaterial);
                if (entry.Go != null) UnityEngine.Object.Destroy(entry.Go);
            }
        }

        // ------------------------------------------------------------------
        // NativeDisplayShelf (extracted from WxqRecruitShelf.cs)
        // ------------------------------------------------------------------

        private sealed class ShelfHitMarker : MonoBehaviour
        {
            public string ItemId;
        }

        private sealed class NativeDisplayShelf : IDisplayShelf
        {
            private sealed class ShelfEntry
            {
                public string ItemId;
                public GameObject Go;
                public Material Material;
                public Vector3 BaseScale;
                public Color BaseColor;
            }

            private readonly GameManager _gm;
            private readonly BoardService _board;
            private readonly ResourceService _resources;
            private readonly Transform _root;
            private readonly Dictionary<string, ShelfEntry> _items = new Dictionary<string, ShelfEntry>(StringComparer.Ordinal);
            private bool _visible = true;
            private bool _disposed;

            public string ShelfName { get; }

            public NativeDisplayShelf(GameManager gm, BoardService board, ResourceService resources, string rootName)
            {
                _gm = gm;
                _board = board;
                _resources = resources;
                ShelfName = rootName;

                var rootGo = new GameObject(rootName);
                _root = rootGo.transform;
                _root.SetParent(gm.StonePanel.transform, false);
                _root.localPosition = Vector3.zero;
                _root.localRotation = Quaternion.identity;
                _root.localScale = Vector3.one;
            }

            public bool AddItem(string itemId, PieceVisualSpec spec, Vector3 boardRelativeOffset)
            {
                if (_disposed || _root == null || string.IsNullOrWhiteSpace(itemId)) return false;
                spec = spec == null ? new PieceVisualSpec() : spec.Clone();

                if (_items.TryGetValue(itemId, out ShelfEntry existing))
                {
                    if (existing.Material != null) UnityEngine.Object.Destroy(existing.Material);
                    if (existing.Go != null) UnityEngine.Object.Destroy(existing.Go);
                    _items.Remove(itemId);
                }

                GameObject prefab = spec.BaseStyle == StoneBaseStyle.Black
                    ? _gm.BlackStonePrefab
                    : _gm.WhiteStonePrefab;
                if (prefab == null) return false;

                float cell = _board.GetCellSpacingLocal();
                int mid = _board.BoardSize / 2;
                Vector3 right = _board.CellToLocal(new BoardCoord(1, mid)) - _board.CellToLocal(new BoardCoord(0, mid));
                Vector3 down = _board.CellToLocal(new BoardCoord(mid, 1)) - _board.CellToLocal(new BoardCoord(mid, 0));
                Vector3 anchor = _board.CellToLocal(new BoardCoord(_board.BoardSize - 1, mid));

                GameObject go = UnityEngine.Object.Instantiate(prefab, _root, false);
                go.name = "JG_ShelfItem_" + itemId;
                go.transform.localPosition = anchor
                    + right.normalized * (cell * boardRelativeOffset.x)
                    + down.normalized * (cell * boardRelativeOffset.z)
                    + Vector3.up * boardRelativeOffset.y;
                go.transform.localRotation = Quaternion.identity;
                Vector3 baseScale = go.transform.localScale * (spec.Scale > 0.01f ? spec.Scale : 1f);
                go.transform.localScale = baseScale;

                ShelfHitMarker marker = go.AddComponent<ShelfHitMarker>();
                marker.ItemId = itemId;

                Collider col = go.GetComponent<Collider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                if (col is BoxCollider bc)
                {
                    bc.size = new Vector3(cell * 1.25f, cell * 1.25f, cell * 1.25f);
                    bc.center = new Vector3(0f, cell * 0.2f, 0f);
                }

                Material mat = null;
                Color baseColor = Color.white;
                MeshRenderer mr = go.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null && mr.sharedMaterial != null)
                {
                    mat = new Material(mr.sharedMaterial);
                    if (spec.Tint.HasValue)
                    {
                        baseColor = spec.Tint.Value;
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        baseColor = mat.GetColor("_Color");
                    }
                    mr.material = mat;
                }

                GoStone gs = go.GetComponent<GoStone>() ?? go.GetComponentInChildren<GoStone>(true);
                if (gs != null && gs.LastMove != null) gs.LastMove.SetActive(true);
                TMP_Text label = gs == null ? go.GetComponentInChildren<TMP_Text>(true) : gs.MoveNumberText;
                if (label != null)
                {
                    if (!string.IsNullOrEmpty(spec.LabelText))
                    {
                        if (spec.UseCjkFont)
                        {
                            TMP_FontAsset font = _resources.FindOrCreateFontInternal(spec.LabelText)
                                                 ?? _resources.GetCjkFontInternal();
                            if (font != null) label.font = font;
                        }
                        label.text = spec.LabelText;
                        label.color = spec.LabelColor;
                        label.alignment = TextAlignmentOptions.Center;
                        label.gameObject.SetActive(true);
                    }
                    else
                    {
                        label.text = string.Empty;
                        label.gameObject.SetActive(false);
                    }
                }

                go.SetActive(_visible);
                _items[itemId] = new ShelfEntry
                {
                    ItemId = itemId,
                    Go = go,
                    Material = mat,
                    BaseScale = baseScale,
                    BaseColor = baseColor
                };
                return true;
            }

            public void SetVisible(bool visible)
            {
                _visible = visible;
                foreach (ShelfEntry item in _items.Values)
                    if (item.Go != null) item.Go.SetActive(visible);
            }

            public void SetItemState(string itemId, bool enabled, bool selected)
            {
                if (!_items.TryGetValue(itemId, out ShelfEntry item) || item.Go == null) return;
                item.Go.transform.localScale = item.BaseScale * (selected ? 1.18f : 1f);
                if (item.Material != null)
                {
                    Color c = item.BaseColor;
                    c.a = enabled ? 1f : 0.28f;
                    if (item.Material.HasProperty("_Color")) item.Material.SetColor("_Color", c);
                    if (item.Material.HasProperty("_BaseColor")) item.Material.SetColor("_BaseColor", c);
                }
            }

            public bool TryRaycastItem(Ray ray, out string itemId)
            {
                itemId = null;
                if (!_visible || _items.Count == 0) return false;
                Camera cam = Camera.main;
                if (cam == null) return false;

                RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
                float nearestAny = float.MaxValue;
                float nearestShelf = float.MaxValue;
                string hitId = null;

                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i].distance < nearestAny) nearestAny = hits[i].distance;
                    ShelfHitMarker marker = hits[i].collider == null
                        ? null
                        : hits[i].collider.GetComponentInParent<ShelfHitMarker>();
                    if (marker == null || string.IsNullOrEmpty(marker.ItemId) || !_items.ContainsKey(marker.ItemId))
                        continue;
                    if (hits[i].distance < nearestShelf)
                    {
                        nearestShelf = hits[i].distance;
                        hitId = marker.ItemId;
                    }
                }

                if (hitId != null)
                {
                    if (nearestShelf <= nearestAny + 0.0001f)
                    {
                        itemId = hitId;
                        return true;
                    }
                    return false;
                }

                if (nearestAny < float.MaxValue) return false;

                // Screen-space fallback tolerance when unoccluded (from WxqRecruitShelf.cs)
                float bestPixels = Mathf.Clamp(Screen.height * 0.035f, 12f, 60f);
                foreach (ShelfEntry item in _items.Values)
                {
                    if (item.Go == null || !item.Go.activeInHierarchy) continue;
                    Vector3 sp = cam.WorldToScreenPoint(item.Go.transform.position);
                    if (sp.z <= 0f) continue;
                    float d = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(Input.mousePosition.x, Input.mousePosition.y));
                    if (d < bestPixels)
                    {
                        bestPixels = d;
                        itemId = item.ItemId;
                    }
                }
                return itemId != null;
            }

            public void Clear()
            {
                foreach (ShelfEntry item in _items.Values)
                {
                    if (item.Material != null) UnityEngine.Object.Destroy(item.Material);
                    if (item.Go != null) UnityEngine.Object.Destroy(item.Go);
                }
                _items.Clear();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Clear();
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            }
        }
    }
}
