using System;
using System.Collections.Generic;

namespace JustGone.Core
{
    public sealed class ModInfo
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Version { get; }

        public ModInfo(string id, string displayName, string version)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Mod id is required.", nameof(id));
            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            Version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        }

        public override string ToString() => DisplayName + " (" + Id + " v" + Version + ")";
    }

    public sealed class ActionDisposable : IDisposable
    {
        private Action _onDispose;

        public ActionDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            Action action = _onDispose;
            _onDispose = null;
            if (action != null) action();
        }
    }

    /// <summary>
    /// Tracks disposable resources owned by a MOD and disposes them in reverse LIFO order without throwing.
    /// </summary>
    public sealed class DisposableTracker : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _disposed;

        public int Count => _items.Count;
        public bool IsDisposed => _disposed;

        public T Track<T>(T item) where T : IDisposable
        {
            if (item == null) return default;
            if (_disposed)
            {
                try { item.Dispose(); } catch { }
                throw new ObjectDisposedException(nameof(DisposableTracker));
            }
            _items.Add(item);
            return item;
        }

        public void DisposeAll(Action<Exception> onError = null)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                try { _items[i]?.Dispose(); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
            _items.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeAll();
        }
    }

    /// <summary>
    /// Reference-counted multi-MOD input lock registry.
    /// Multiple MODs may independently acquire and release locks without clobbering each other.
    /// </summary>
    public sealed class InputLockRegistry
    {
        private sealed class Token
        {
            public int Id;
            public string OwnerModId;
            public string Reason;
        }

        private readonly List<Token> _locks = new List<Token>();
        private int _nextId;

        public bool IsLocked => _locks.Count > 0;
        public int LockCount => _locks.Count;

        public IDisposable Acquire(string ownerModId, string reason)
        {
            int id = ++_nextId;
            _locks.Add(new Token
            {
                Id = id,
                OwnerModId = ownerModId ?? "anonymous",
                Reason = reason ?? string.Empty
            });
            return new ActionDisposable(() => ReleaseById(id));
        }

        public int ReleaseAllByOwner(string ownerModId)
        {
            int removed = 0;
            for (int i = _locks.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_locks[i].OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase))
                {
                    _locks.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        public void ClearAll()
        {
            _locks.Clear();
        }

        private void ReleaseById(int id)
        {
            for (int i = _locks.Count - 1; i >= 0; i--)
            {
                if (_locks[i].Id == id)
                {
                    _locks.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
