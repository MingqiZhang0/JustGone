using System;
using System.Collections.Generic;

namespace JustGone.Core
{
    /// <summary>
    /// Decision outcome for routing Local Play Mode Selection clicks.
    /// Generalized from WeiXiangQi v0.10.10.0 (shared_p4/WxqCompatGuards.cs WxqMenuRoute)
    /// to support multiple Just Gone MODs while preserving vanilla modes [0, VanillaModeCount)
    /// AND coexisting with third-party mods such as WeiXiangQi (index 2).
    /// </summary>
    public enum MenuRouteAction
    {
        /// <summary>Not owned by Just Gone (vanilla 0/1 or another standalone mod like WeiXiangQi): return true in Harmony Prefix.</summary>
        PassThrough = 0,
        /// <summary>Owned by a registered, enabled Just Gone mode: intercept and launch.</summary>
        LaunchRegisteredMode = 1,
        /// <summary>Owned by a registered Just Gone mode, but framework/mode is disabled: block without falling through to vanilla.</summary>
        BlockModeDisabled = 2,
        /// <summary>Owned by a registered Just Gone mode, but scene loading is already in progress.</summary>
        BlockSceneLoading = 3
    }

    public sealed class MenuRouteEntry
    {
        public string ModeId { get; }
        public string OwnerModId { get; }
        public string DisplayTitle { get; }
        public bool Enabled { get; set; }
        public int AssignedSelectorIndex { get; set; }

        public MenuRouteEntry(string modeId, string ownerModId, string displayTitle)
        {
            if (string.IsNullOrWhiteSpace(modeId)) throw new ArgumentException("ModeId is required.", nameof(modeId));
            if (string.IsNullOrWhiteSpace(ownerModId)) throw new ArgumentException("OwnerModId is required.", nameof(ownerModId));
            if (string.IsNullOrWhiteSpace(displayTitle)) throw new ArgumentException("DisplayTitle is required.", nameof(displayTitle));

            ModeId = modeId;
            OwnerModId = ownerModId;
            DisplayTitle = displayTitle;
            Enabled = true;
            AssignedSelectorIndex = -1;
        }
    }

    public struct MenuRouteResult
    {
        public MenuRouteAction Action;
        public MenuRouteEntry MatchedEntry;

        public MenuRouteResult(MenuRouteAction action, MenuRouteEntry matchedEntry)
        {
            Action = action;
            MatchedEntry = matchedEntry;
        }
    }

    /// <summary>
    /// Pure-logic multi-MOD menu routing table (zero Unity dependencies, unit-testable).
    /// Coexistence invariant:
    ///   - Indices [0, VanillaModeCount) ALWAYS return PassThrough.
    ///   - Indices >= VanillaModeCount that are NOT registered in this table ALSO return PassThrough
    ///     (so WeiXiangQi v0.10.10.0 at index 2 runs undisturbed when Just Gone runs at Priority.High).
    ///   - Indices registered in this table are matched by index (and optionally verified by item title).
    /// </summary>
    public sealed class MenuRouteTable
    {
        public const int DefaultVanillaModeCount = 2;

        private readonly List<MenuRouteEntry> _entries = new List<MenuRouteEntry>();

        public int VanillaModeCount { get; }

        public MenuRouteTable(int vanillaModeCount = DefaultVanillaModeCount)
        {
            VanillaModeCount = vanillaModeCount < 1 ? DefaultVanillaModeCount : vanillaModeCount;
        }

        public IReadOnlyList<MenuRouteEntry> Entries => _entries;

        public MenuRouteEntry Register(string modeId, string ownerModId, string displayTitle)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].ModeId, modeId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Duplicate menu mode id: " + modeId);
            }
            var entry = new MenuRouteEntry(modeId, ownerModId, displayTitle);
            _entries.Add(entry);
            return entry;
        }

        public bool Unregister(string modeId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].ModeId, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    _entries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public int UnregisterByOwner(string ownerModId)
        {
            int removed = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_entries[i].OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase))
                {
                    _entries.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        public void ResetAssignedIndices()
        {
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].AssignedSelectorIndex = -1;
        }

        public bool TryAssignIndex(string modeId, int selectorIndex)
        {
            if (selectorIndex < VanillaModeCount) return false;
            MenuRouteEntry target = FindByModeId(modeId);
            if (target == null) return false;
            target.AssignedSelectorIndex = selectorIndex;
            return true;
        }

        public MenuRouteEntry FindByModeId(string modeId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].ModeId, modeId, StringComparison.OrdinalIgnoreCase))
                    return _entries[i];
            }
            return null;
        }

        public MenuRouteEntry FindByAssignedIndex(int selectorIndex, string currentItemTitle = null)
        {
            if (selectorIndex < VanillaModeCount) return null;
            for (int i = 0; i < _entries.Count; i++)
            {
                MenuRouteEntry entry = _entries[i];
                if (entry.AssignedSelectorIndex != selectorIndex) continue;
                if (!string.IsNullOrEmpty(currentItemTitle)
                    && !string.Equals(entry.DisplayTitle, currentItemTitle, StringComparison.Ordinal))
                {
                    continue;
                }
                return entry;
            }
            return null;
        }

        public MenuRouteResult Decide(
            int selectorIndex,
            string currentItemTitle,
            bool frameworkReady,
            bool loadingEnabled)
        {
            if (selectorIndex < VanillaModeCount)
                return new MenuRouteResult(MenuRouteAction.PassThrough, null);

            MenuRouteEntry entry = FindByAssignedIndex(selectorIndex, currentItemTitle);
            if (entry == null)
                return new MenuRouteResult(MenuRouteAction.PassThrough, null);

            if (!frameworkReady || !entry.Enabled)
                return new MenuRouteResult(MenuRouteAction.BlockModeDisabled, entry);

            if (!loadingEnabled)
                return new MenuRouteResult(MenuRouteAction.BlockSceneLoading, entry);

            return new MenuRouteResult(MenuRouteAction.LaunchRegisteredMode, entry);
        }
    }
}
