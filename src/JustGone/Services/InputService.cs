using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JustGone.Services
{
    /// <summary>
    /// Input focus, UI pointer raycast, multi-MOD vanilla input suppression locks, and menu wheel absorption.
    /// Extracted from WeiXiangQi v0.10.10.0 (WxqSession.cs PointerOverUnityUi / input focus guard and Patches.cs).
    /// </summary>
    internal sealed class InputService : IInputService
    {
        private sealed class WheelAbsorberEntry
        {
            public string OwnerModId;
            public Func<bool> Predicate;
            public bool Active;
        }

        private readonly InputLockRegistry _locks = new InputLockRegistry();
        private readonly List<WheelAbsorberEntry> _wheelAbsorbers = new List<WheelAbsorberEntry>();
        private readonly List<RaycastResult> _raycastBuffer = new List<RaycastResult>(16);

        internal bool FailsafeInputLocked { get; set; }
        internal bool ActiveModeSuppressesVanillaInput { get; set; }

        public bool IsVanillaInputSuppressed =>
            FailsafeInputLocked || ActiveModeSuppressesVanillaInput || _locks.IsLocked;

        public bool IsPointerOverUi
        {
            get
            {
                try
                {
                    EventSystem es = EventSystem.current;
                    if (es == null) return false;
                    var ped = new PointerEventData(es)
                    {
                        position = new Vector2(Input.mousePosition.x, Input.mousePosition.y)
                    };
                    _raycastBuffer.Clear();
                    es.RaycastAll(ped, _raycastBuffer);
                    for (int i = 0; i < _raycastBuffer.Count; i++)
                    {
                        GameObject go = _raycastBuffer[i].gameObject;
                        if (go == null) continue;
                        if (go.GetComponentInParent<Selectable>() != null) return true;
                        if (go.GetComponentInParent<ScrollRect>() != null) return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public bool IsTextInputFocused
        {
            get
            {
                try
                {
                    EventSystem es = EventSystem.current;
                    GameObject selected = es == null ? null : es.currentSelectedGameObject;
                    if (selected == null) return false;
                    return selected.GetComponentInParent<InputField>() != null
                        || selected.GetComponentInParent<TMP_InputField>() != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public IDisposable AcquireVanillaInputLock(ModContext owner, string reason)
        {
            string modId = owner == null ? "anonymous" : owner.Info.Id;
            IDisposable token = _locks.Acquire(modId, reason);
            JgLog.Debug("Input lock acquired by [" + modId + "] (" + (reason ?? "") + "), total=" + _locks.LockCount);
            return owner != null ? owner.Track(token) : token;
        }

        public IDisposable RegisterMenuWheelAbsorber(ModContext owner, Func<bool> shouldAbsorb)
        {
            if (shouldAbsorb == null) throw new ArgumentNullException(nameof(shouldAbsorb));
            string modId = owner == null ? "anonymous" : owner.Info.Id;
            var entry = new WheelAbsorberEntry { OwnerModId = modId, Predicate = shouldAbsorb, Active = true };
            _wheelAbsorbers.Add(entry);
            IDisposable token = new ActionDisposable(() =>
            {
                entry.Active = false;
                _wheelAbsorbers.Remove(entry);
            });
            return owner != null ? owner.Track(token) : token;
        }

        internal bool ShouldAbsorbMenuWheel()
        {
            for (int i = 0; i < _wheelAbsorbers.Count; i++)
            {
                WheelAbsorberEntry entry = _wheelAbsorbers[i];
                if (!entry.Active) continue;
                try
                {
                    if (entry.Predicate()) return true;
                }
                catch (Exception ex)
                {
                    JgLog.Warn("MenuWheel absorber [" + entry.OwnerModId + "] error: " + ex.Message);
                }
            }
            return false;
        }

        internal void ClearSessionLocks()
        {
            ActiveModeSuppressesVanillaInput = false;
        }
    }
}
