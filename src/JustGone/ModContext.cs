using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone
{
    /// <summary>
    /// Isolated per-MOD ownership context.
    /// All event subscriptions, input locks, menu registrations, layers, and UI panels created via a ModContext
    /// are tracked and safely cleaned up when Dispose() is called.
    /// </summary>
    public sealed class ModContext : IDisposable
    {
        private readonly DisposableTracker _tracker = new DisposableTracker();
        private readonly List<UnityEngine.Object> _unityObjects = new List<UnityEngine.Object>();
        private readonly Action<string> _onUnregister;
        private bool _disposed;

        public ModInfo Info { get; }
        public IModLogger Log { get; }
        public bool IsDisposed => _disposed;

        internal ModContext(ModInfo info, Action<string> onUnregister)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Log = new JgLog.ModLogger(info.Id);
            _onUnregister = onUnregister;
        }

        public T Track<T>(T disposable) where T : IDisposable
        {
            if (_disposed) throw new ObjectDisposedException(Info.Id);
            return _tracker.Track(disposable);
        }

        public T TrackUnityObject<T>(T obj) where T : UnityEngine.Object
        {
            if (_disposed) throw new ObjectDisposedException(Info.Id);
            if (obj != null) _unityObjects.Add(obj);
            return obj;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _tracker.DisposeAll(ex => Log.Warn("resource dispose error: " + ex.Message));

            for (int i = _unityObjects.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_unityObjects[i] != null) UnityEngine.Object.Destroy(_unityObjects[i]);
                }
                catch (Exception ex)
                {
                    Log.Warn("unity object cleanup error: " + ex.Message);
                }
            }
            _unityObjects.Clear();

            try { _onUnregister?.Invoke(Info.Id); }
            catch (Exception ex) { JgLog.Warn("mod unregister callback error: " + ex.Message); }
        }
    }
}
