using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Exception-isolated event bus supporting multiple independent MOD subscribers.
    /// Every subscription returns an IDisposable and is automatically tracked by the owning ModContext.
    /// </summary>
    internal sealed class EventService : IEventService
    {
        private sealed class Subscription<T>
        {
            public string OwnerModId;
            public Action<T> Handler;
            public bool Active;
        }

        private sealed class KeySubscription
        {
            public string OwnerModId;
            public KeyCode Key;
            public Action<KeyPressEventArgs> Handler;
            public bool Active;
        }

        private readonly List<Subscription<SceneLoadedEventArgs>> _sceneLoaded = new List<Subscription<SceneLoadedEventArgs>>();
        private readonly List<Subscription<GameReadyEventArgs>> _gameReady = new List<Subscription<GameReadyEventArgs>>();
        private readonly List<Subscription<SceneLoadedEventArgs>> _gameLeaving = new List<Subscription<SceneLoadedEventArgs>>();
        private readonly List<Subscription<BoardHoverEventArgs>> _boardHover = new List<Subscription<BoardHoverEventArgs>>();
        private readonly List<Subscription<BoardClickEventArgs>> _boardClick = new List<Subscription<BoardClickEventArgs>>();
        private readonly List<KeySubscription> _keyPressed = new List<KeySubscription>();
        private readonly List<Subscription<MouseWheelEventArgs>> _mouseWheel = new List<Subscription<MouseWheelEventArgs>>();
        private readonly List<Subscription<PauseChangedEventArgs>> _pauseChanged = new List<Subscription<PauseChangedEventArgs>>();
        private readonly List<Subscription<NativeActionEventArgs>> _nativeAction = new List<Subscription<NativeActionEventArgs>>();

        public IDisposable OnSceneLoaded(ModContext owner, Action<SceneLoadedEventArgs> handler) => Subscribe(owner, _sceneLoaded, handler);
        public IDisposable OnGameReady(ModContext owner, Action<GameReadyEventArgs> handler) => Subscribe(owner, _gameReady, handler);
        public IDisposable OnGameLeaving(ModContext owner, Action<SceneLoadedEventArgs> handler) => Subscribe(owner, _gameLeaving, handler);
        public IDisposable OnBoardHoverChanged(ModContext owner, Action<BoardHoverEventArgs> handler) => Subscribe(owner, _boardHover, handler);
        public IDisposable OnBoardClicked(ModContext owner, Action<BoardClickEventArgs> handler) => Subscribe(owner, _boardClick, handler);
        public IDisposable OnMouseWheel(ModContext owner, Action<MouseWheelEventArgs> handler) => Subscribe(owner, _mouseWheel, handler);
        public IDisposable OnPauseChanged(ModContext owner, Action<PauseChangedEventArgs> handler) => Subscribe(owner, _pauseChanged, handler);
        public IDisposable OnNativeAction(ModContext owner, Action<NativeActionEventArgs> handler) => Subscribe(owner, _nativeAction, handler);

        public IDisposable OnKeyPressed(ModContext owner, KeyCode key, Action<KeyPressEventArgs> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string modId = owner == null ? "anonymous" : owner.Info.Id;
            var sub = new KeySubscription { OwnerModId = modId, Key = key, Handler = handler, Active = true };
            _keyPressed.Add(sub);
            IDisposable token = new ActionDisposable(() =>
            {
                sub.Active = false;
                _keyPressed.Remove(sub);
            });
            return owner != null ? owner.Track(token) : token;
        }

        private static IDisposable Subscribe<T>(ModContext owner, List<Subscription<T>> list, Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string modId = owner == null ? "anonymous" : owner.Info.Id;
            var sub = new Subscription<T> { OwnerModId = modId, Handler = handler, Active = true };
            list.Add(sub);
            IDisposable token = new ActionDisposable(() =>
            {
                sub.Active = false;
                list.Remove(sub);
            });
            return owner != null ? owner.Track(token) : token;
        }

        internal void DispatchSceneLoaded(SceneLoadedEventArgs args) => Dispatch(_sceneLoaded, args, "SceneLoaded");
        internal void DispatchGameReady(GameReadyEventArgs args) => Dispatch(_gameReady, args, "GameReady");
        internal void DispatchGameLeaving(SceneLoadedEventArgs args) => Dispatch(_gameLeaving, args, "GameLeaving");
        internal void DispatchBoardHover(BoardHoverEventArgs args) => Dispatch(_boardHover, args, "BoardHover");
        internal void DispatchBoardClick(BoardClickEventArgs args) => Dispatch(_boardClick, args, "BoardClick");
        internal void DispatchMouseWheel(MouseWheelEventArgs args) => Dispatch(_mouseWheel, args, "MouseWheel");
        internal void DispatchPauseChanged(PauseChangedEventArgs args) => Dispatch(_pauseChanged, args, "PauseChanged");

        internal bool DispatchNativeAction(NativeGameAction action)
        {
            var args = new NativeActionEventArgs(action);
            Dispatch(_nativeAction, args, "NativeAction:" + action);
            return args.Consumed;
        }

        internal void PollKeySubscriptions(bool isTextInputFocused)
        {
            if (_keyPressed.Count == 0) return;
            var snapshot = _keyPressed.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                KeySubscription sub = snapshot[i];
                if (!sub.Active) continue;
                bool pressed = false;
                try { pressed = Input.GetKeyDown(sub.Key); } catch { }
                if (!pressed) continue;

                var args = new KeyPressEventArgs((int)sub.Key, sub.Key.ToString(), isTextInputFocused);
                try
                {
                    sub.Handler(args);
                }
                catch (Exception ex)
                {
                    JgLog.Error("Event OnKeyPressed(" + sub.Key + ") subscriber [" + sub.OwnerModId + "] threw", ex);
                }
            }
        }

        private static void Dispatch<T>(List<Subscription<T>> list, T args, string eventName)
        {
            if (list.Count == 0) return;
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Subscription<T> sub = snapshot[i];
                if (!sub.Active) continue;
                try
                {
                    sub.Handler(args);
                }
                catch (Exception ex)
                {
                    JgLog.Error("Event " + eventName + " subscriber [" + sub.OwnerModId + "] threw", ex);
                }
            }
        }
    }
}
