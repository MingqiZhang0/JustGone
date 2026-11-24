using System;
using System.Reflection;
using HarmonyLib;
using JustGone.Core;
using UnityEngine;

namespace JustGone.Internal
{
    /// <summary>
    /// Internal bridge isolating all Assembly-CSharp private fields, enums, and methods.
    /// No public Just Gone API signature exposes Assembly-CSharp types.
    /// </summary>
    internal static class JgGameBridge
    {
        internal static GameManager TryGetGameManager()
        {
            try
            {
                GameManager gm = GameManager.m_Instance;
                return (gm != null && gm.gameObject != null) ? gm : null;
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsGameStarted(GameManager gm)
        {
            if (gm == null) return false;
            try
            {
                return Traverse.Create(gm).Field("isGameStarted").GetValue<bool>();
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsGameOver(GameManager gm)
        {
            if (gm == null) return false;
            try
            {
                return Traverse.Create(gm).Field("isGameOver").GetValue<bool>();
            }
            catch
            {
                return false;
            }
        }

        internal static void SetGameOverFlag(GameManager gm, bool value)
        {
            if (gm == null) return;
            try
            {
                Traverse.Create(gm).Field("isGameOver").SetValue(value);
                gm.CurrentGameState = value ? GoEnum.GameState.GameOver : GoEnum.GameState.UserInput;
            }
            catch (Exception ex)
            {
                JgLog.Warn("SetGameOverFlag failed: " + ex.Message);
            }
        }

        internal static VanillaGameMode GetVanillaMode(GameManager gm)
        {
            if (gm == null) return VanillaGameMode.LocalPractice;
            try
            {
                GoEnum.GameMode raw = Traverse.Create(gm).Field("gameMode").GetValue<GoEnum.GameMode>();
                return (VanillaGameMode)(int)raw;
            }
            catch
            {
                return VanillaGameMode.LocalPractice;
            }
        }

        internal static GameEnvironmentKind GetEnvironment(GameManager gm)
        {
            if (gm == null) return GameEnvironmentKind.ChineseGarden;
            try
            {
                GoEnum.GameEnvironment raw = Traverse.Create(gm).Field("gameEnvironment").GetValue<GoEnum.GameEnvironment>();
                return (GameEnvironmentKind)(int)raw;
            }
            catch
            {
                return GameEnvironmentKind.ChineseGarden;
            }
        }

        internal static int GetBoardSize(GameManager gm)
        {
            if (gm == null) return 19;
            try
            {
                int size = Traverse.Create(gm).Field("boardSize").GetValue<int>();
                return size > 0 ? size : 19;
            }
            catch
            {
                return 19;
            }
        }

        internal static float GetSoundEffectVolume(GameManager gm)
        {
            if (gm == null) return 0.8f;
            try
            {
                float v = Traverse.Create(gm).Field("soundEffectOriginalVolume").GetValue<float>();
                return v > 0.0001f ? v : 0.8f;
            }
            catch
            {
                return 0.8f;
            }
        }

        internal static bool IsTitleLoadingEnabled(TitleManager tm)
        {
            if (tm == null) return true;
            try
            {
                object raw = Traverse.Create(tm).Field("isLoadingNewSceneEnabled").GetValue();
                return raw == null || Convert.ToBoolean(raw);
            }
            catch
            {
                return true;
            }
        }

        internal static bool TrySetCamera(GameManager gm, VirtualCameraKind camera, float transitionSeconds)
        {
            if (gm == null) return false;
            try
            {
                MethodInfo setCam = gm.GetType().GetMethod("SetCurrentCamera", BindingFlags.Instance | BindingFlags.NonPublic);
                if (setCam == null) return false;
                setCam.Invoke(gm, new object[] { (GoEnum.VirtualCameraType)(int)camera });

                MethodInfo setTime = gm.GetType().GetMethod("SetCameraMovementTime", BindingFlags.Instance | BindingFlags.NonPublic);
                if (setTime != null && transitionSeconds >= 0f)
                    setTime.Invoke(gm, new object[] { transitionSeconds });
                return true;
            }
            catch (Exception ex)
            {
                JgLog.Debug("TrySetCamera skipped: " + ex.Message);
                return false;
            }
        }

        internal static Vector3 ConvertCellToLocal(GameEnvironmentKind env, BoardCoord coord, int boardSize)
        {
            return GoUtils.ConvertPointToBoardPosition(
                (GoEnum.GameEnvironment)(int)env,
                new Point(coord.X, coord.Y),
                boardSize);
        }

        internal static BoardCoord ConvertWorldToCell(GameEnvironmentKind env, Vector3 worldPos, int boardSize)
        {
            Point p = GoUtils.ConvertBoardPositionToPoint(
                (GoEnum.GameEnvironment)(int)env,
                worldPos,
                boardSize);
            var coord = new BoardCoord(p.x, p.y);
            return coord.InBounds(boardSize) ? coord : BoardCoord.Invalid;
        }

        internal static int GetBoardLayerMask()
        {
            return LayerMask.GetMask(Constants.GO_BOARD_LAYER);
        }

        internal static string PathOf(Transform t)
        {
            if (t == null) return "<null>";
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }
}
