using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;

namespace JustGone.Services
{
    internal sealed class MenuService : IMenuService
    {
        private sealed class RegisteredMode
        {
            public MenuRouteEntry Entry;
            public MenuModeDescriptor Descriptor;
        }

        private sealed class MenuModeRegistration : IMenuModeRegistration
        {
            private readonly MenuService _ownerService;
            private readonly MenuRouteEntry _entry;
            private bool _disposed;

            public MenuModeRegistration(MenuService ownerService, MenuRouteEntry entry)
            {
                _ownerService = ownerService;
                _entry = entry;
            }

            public string ModeId => _entry.ModeId;
            public string DisplayTitle => _entry.DisplayTitle;
            public int AssignedSelectorIndex => _entry.AssignedSelectorIndex;

            public bool Enabled
            {
                get => _entry.Enabled;
                set => _entry.Enabled = value;
            }

            public void RequestPageRebuild(bool scrollToTop = false)
            {
                JgMenuHost.Current?.RequestRebuild(scrollToTop);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _ownerService.UnregisterMode(_entry.ModeId);
            }
        }

        private readonly Dictionary<string, RegisteredMode> _modes = new Dictionary<string, RegisteredMode>(StringComparer.OrdinalIgnoreCase);

        internal MenuRouteTable RouteTable { get; } = new MenuRouteTable(MenuRouteTable.DefaultVanillaModeCount);

        public bool IsMainMenuReady => JgMenuHost.Current != null && JgMenuHost.Current.IsMainMenuReady;

        public IMenuModeRegistration RegisterMode(ModContext owner, MenuModeDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.ModeId))
                throw new ArgumentException("MenuModeDescriptor.ModeId is required.", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.DisplayTitle))
                throw new ArgumentException("MenuModeDescriptor.DisplayTitle is required.", nameof(descriptor));

            string modId = owner == null ? "anonymous" : owner.Info.Id;
            MenuRouteEntry entry = RouteTable.Register(descriptor.ModeId, modId, descriptor.DisplayTitle);
            _modes[descriptor.ModeId] = new RegisteredMode { Entry = entry, Descriptor = descriptor };

            JgMenuHost.Current?.SyncRegisteredModesIntoSelector();

            var reg = new MenuModeRegistration(this, entry);
            return owner != null ? owner.Track(reg) : reg;
        }

        public void ShowMenuNotice(string message, float durationSeconds = 8f)
        {
            if (JgMenuHost.Current != null)
            {
                JgMenuHost.Current.ShowMenuNotice(message, durationSeconds);
            }
            else
            {
                JgLog.Info("[MenuNotice] " + message);
            }
        }

        internal void UnregisterMode(string modeId)
        {
            _modes.Remove(modeId);
            RouteTable.Unregister(modeId);
        }

        internal void InvokeBuildConfigPage(string modeId, IMenuPageBuilder builder)
        {
            if (!_modes.TryGetValue(modeId, out RegisteredMode mode)) return;
            mode.Descriptor.BuildConfigPage?.Invoke(builder);
        }

        internal bool TryHandleMenuLaunch(int selectorIndex, string itemTitle, bool loadingEnabled)
        {
            MenuRouteResult decision = RouteTable.Decide(selectorIndex, itemTitle, JustGoneApi.IsReady, loadingEnabled);
            if (decision.Action == MenuRouteAction.PassThrough)
                return false; // Let WeiXiangQi or Vanilla handle this index

            if (decision.Action == MenuRouteAction.BlockSceneLoading)
            {
                JgLog.Warn("Menu launch ignored: scene already loading (" + decision.MatchedEntry.ModeId + ").");
                return true; // Intercepted (do not fall through)
            }

            if (decision.Action == MenuRouteAction.BlockModeDisabled)
            {
                ShowMenuNotice("模式 \"" + decision.MatchedEntry.DisplayTitle + "\" 当前已禁用或未就绪。", 8f);
                return true;
            }

            if (!_modes.TryGetValue(decision.MatchedEntry.ModeId, out RegisteredMode reg))
                return true;

            if (reg.Descriptor.ValidateLaunch != null)
            {
                MenuLaunchValidation v = reg.Descriptor.ValidateLaunch();
                if (v != null && !v.IsValid)
                {
                    JgLog.Warn("Launch blocked by validation (" + reg.Entry.ModeId + "): " + v.ErrorMessage);
                    ShowMenuNotice(v.ErrorMessage, 8f);
                    return true;
                }
            }

            try
            {
                reg.Descriptor.OnLaunchRequested?.Invoke();
            }
            catch (Exception ex)
            {
                JgLog.Error("Menu launch handler crashed for " + reg.Entry.ModeId, ex);
                ShowMenuNotice("启动失败：" + ex.Message, 10f);
            }
            return true;
        }

        internal void OnMainMenuEntered(ResourceService resources)
        {
            RouteTable.ResetAssignedIndices();
            JgMenuHost.EnsureCreated(this, resources);
        }
    }
}
