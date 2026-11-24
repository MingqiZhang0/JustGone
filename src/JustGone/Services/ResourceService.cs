using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using JustGone.Internal;
using TMPro;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Native font/CJK glyph discovery and mod config directory service.
    /// Generalized from WeiXiangQi v0.10.10.0 (WxqGlyphProvider.cs) to support arbitrary character sets.
    /// </summary>
    internal sealed class ResourceService : IResourceService
    {
        private const string DefaultCjkSample = "\u56f4\u68cb\u8c61\u68cb\u5c06\u5e05\u8bbe\u7f6e"; // 围棋象棋将帅设置

        private static readonly string[] OsFontCandidates =
        {
            "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "NSimSun",
            "Noto Sans CJK SC", "Source Han Sans SC", "Arial Unicode MS"
        };

        private readonly Dictionary<string, TMP_FontAsset> _fontCache = new Dictionary<string, TMP_FontAsset>(StringComparer.Ordinal);
        private TMP_FontAsset _cjkFont;
        private bool _cjkScanned;

        public bool HasNativeStonePrefabs
        {
            get
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                return gm != null && gm.BlackStonePrefab != null && gm.WhiteStonePrefab != null;
            }
        }

        public bool HasNativeOwnershipPrefab
        {
            get
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                return gm != null && gm.OwnershipPrefab != null;
            }
        }

        public bool HasCjkFont => GetCjkFontInternal() != null;

        public bool EnsureFontForCharacters(string requiredCharacters)
        {
            return FindOrCreateFontInternal(requiredCharacters) != null;
        }

        public string GetModConfigDirectory(ModContext owner)
        {
            string folderName = owner == null ? JustGoneApi.FrameworkGuid : SanitizeFolderName(owner.Info.Id);
            string dir = Path.Combine(Paths.ConfigPath, folderName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal TMP_FontAsset GetCjkFontInternal()
        {
            if (_cjkFont != null) return _cjkFont;
            if (_cjkScanned) return null;
            _cjkScanned = true;
            _cjkFont = FindOrCreateFontInternal(DefaultCjkSample);
            return _cjkFont;
        }

        internal TMP_FontAsset FindOrCreateFontInternal(string requiredCharacters)
        {
            string key = string.IsNullOrEmpty(requiredCharacters) ? DefaultCjkSample : requiredCharacters;
            if (_fontCache.TryGetValue(key, out TMP_FontAsset cached) && cached != null)
                return cached;

            // 1) Scan already-loaded TMP_FontAsset objects first
            try
            {
                TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        TMP_FontAsset candidate = loaded[i];
                        if (candidate != null && CanRenderAll(candidate, key))
                        {
                            _fontCache[key] = candidate;
                            JgLog.Info("Font selected from loaded assets: " + candidate.name);
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                JgLog.Warn("Loaded TMP font scan failed: " + ex.Message);
            }

            // 2) Fallback: create dynamic TMP_FontAsset from OS font
            for (int i = 0; i < OsFontCandidates.Length; i++)
            {
                try
                {
                    Font os = Font.CreateDynamicFontFromOSFont(OsFontCandidates[i], 64);
                    if (os == null) continue;
                    TMP_FontAsset dyn = TMP_FontAsset.CreateFontAsset(os);
                    if (dyn == null) continue;
                    dyn.name = "JG_DYN_" + OsFontCandidates[i];
                    if (CanRenderAll(dyn, key))
                    {
                        _fontCache[key] = dyn;
                        JgLog.Info("Font created from OS font: " + OsFontCandidates[i]);
                        return dyn;
                    }
                }
                catch (Exception ex)
                {
                    JgLog.Debug("OS font \"" + OsFontCandidates[i] + "\" unavailable: " + ex.Message);
                }
            }

            JgLog.Warn("No TMP font found capable of rendering requested character set.");
            return null;
        }

        private static bool CanRenderAll(TMP_FontAsset font, string chars)
        {
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsWhiteSpace(c)) continue;
                if (!CanRenderChar(font, c)) return false;
            }
            return true;
        }

        private static bool CanRenderChar(TMP_FontAsset font, char c)
        {
            try
            {
                if (font.HasCharacter(c, false, false)) return true;
                if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                {
                    string missing;
                    if (font.TryAddCharacters(c.ToString(), out missing))
                        return string.IsNullOrEmpty(missing);
                }
            }
            catch { }
            return false;
        }

        private static string SanitizeFolderName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "unknown-mod";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = raw.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j]) { chars[i] = '_'; break; }
                }
            }
            return new string(chars);
        }
    }
}
