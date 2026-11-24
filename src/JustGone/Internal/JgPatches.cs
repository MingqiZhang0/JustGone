using System;
using HarmonyLib;
using JustGone.Core;
using Michsky.UI.ModernUIPack;

namespace JustGone.Internal
{
    /// <summary>
    /// Coexistence-safe Harmony patches owned exclusively by dev.justgone.framework.
    /// Extracted and decoupled from WeiXiangQi v0.10.10.0 (Patches.cs).
    ///
    /// Coexistence invariants:
    ///   - LoadLocalMultiMatch uses Priority.High (600) so Just Gone intercepts ONLY its own registered
    ///     custom modes BEFORE WeiXiangQi (default Priority.Normal = 400) sees them.
    ///   - Any index not registered in Just Gone (vanilla 0/1 and WeiXiangQi index 2) returns true immediately.
    ///   - In-game patches pass through (return true) unless a Just Gone mode or explicit input lock is active.
    /// </summary>
    [HarmonyPatch(typeof(TitleManager), "LoadLocalMultiMatch")]
    internal static class Patch_JgLoadLocalMultiMatch
    {
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(TitleManager __instance)
        {
            try
            {
                if (__instance == null) return true;
                HorizontalSelector selector = __instance.LocalMultiModeSelector;
                if (selector == null || selector.itemList == null) return true;

                int idx = selector.index;
                string title = (idx >= 0 && idx < selector.itemList.Count) ? selector.itemList[idx]?.itemTitle : null;
                bool loadingEnabled = JgGameBridge.IsTitleLoadingEnabled(__instance);

                bool intercepted = JustGoneApi.MenuInternal.TryHandleMenuLaunch(idx, title, loadingEnabled);
                return !intercepted;
            }
            catch (Exception ex)
            {
                JgLog.Error("Patch_JgLoadLocalMultiMatch failed", ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), "UpdateMouseInput")]
    internal static class Patch_JgUpdateMouseInput
    {
        private static bool Prefix()
        {
            return !JustGoneApi.InputInternal.IsVanillaInputSuppressed;
        }
    }

    [HarmonyPatch(typeof(GameManager), "UpdateScrollWheel")]
    internal static class Patch_JgUpdateScrollWheel
    {
        private static bool Prefix()
        {
            return !JustGoneApi.InputInternal.IsVanillaInputSuppressed;
        }
    }

    [HarmonyPatch(typeof(GameManager), "UpdateKeyboardInput")]
    internal static class Patch_JgUpdateKeyboardInput
    {
        private static bool Prefix()
        {
            return !JustGoneApi.InputInternal.IsVanillaInputSuppressed;
        }
    }

    [HarmonyPatch(typeof(GameManager), "UndoWrapper")]
    internal static class Patch_JgUndoWrapper
    {
        private static bool Prefix()
        {
            return !HandleNativeAction(NativeGameAction.Undo);
        }

        internal static bool HandleNativeAction(NativeGameAction action)
        {
            if (JustGoneApi.ModesInternal.HasActiveMode && JustGoneApi.ModesInternal.TryDispatchNativeAction(action))
                return true;
            return JustGoneApi.EventsInternal.DispatchNativeAction(action);
        }
    }

    [HarmonyPatch(typeof(GameManager), "UndoStone")]
    internal static class Patch_JgUndoStone
    {
        private static bool Prefix()
        {
            return !Patch_JgUndoWrapper.HandleNativeAction(NativeGameAction.Undo);
        }
    }

    [HarmonyPatch(typeof(GameManager), "RestartGame")]
    internal static class Patch_JgRestartGame
    {
        private static bool Prefix()
        {
            return !Patch_JgUndoWrapper.HandleNativeAction(NativeGameAction.Restart);
        }
    }

    [HarmonyPatch(typeof(GameManager), "CopySGF")]
    internal static class Patch_JgCopySGF
    {
        private static bool Prefix()
        {
            return !Patch_JgUndoWrapper.HandleNativeAction(NativeGameAction.Save);
        }
    }

    [HarmonyPatch(typeof(GameManager), "UpdateButtonsInPanels")]
    internal static class Patch_JgUpdateButtonsInPanels
    {
        private static void Postfix(GameManager __instance)
        {
            if (__instance == null) return;
            if (!JustGoneApi.UiInternal.HasPracticeButtonOverrides) return;
            JustGoneApi.UiInternal.ApplyPracticeButtonOverrides(__instance);
        }
    }

    [HarmonyPatch(typeof(MouseWheel), "Update")]
    internal static class Patch_JgMouseWheelMenuFlip
    {
        private static bool Prefix()
        {
            try
            {
                if (JgMenuHost.ShouldAbsorbMenuWheelStatic()) return false;
                if (JustGoneApi.InputInternal.ShouldAbsorbMenuWheel()) return false;
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
