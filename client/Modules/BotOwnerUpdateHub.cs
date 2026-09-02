using EFT;
using System;
using System.Collections.Generic;
using System.Threading;

namespace pitTeam.Modules
{
    public static class BotOwnerUpdateHub
    {
        private static readonly Dictionary<string, Action<BotOwner>> Subscribers = new Dictionary<string, Action<BotOwner>>();
        private static readonly Dictionary<string, Action<BotOwner>> FollowerSubscribers = new Dictionary<string, Action<BotOwner>>();
        private static readonly object SyncRoot = new object();
        private static Action<BotOwner>[] _callbackSnapshot = Array.Empty<Action<BotOwner>>();
        private static Action<BotOwner>[] _followerCallbackSnapshot = Array.Empty<Action<BotOwner>>();
        private static int _subscriberCount;
        private static int _followerSubscriberCount;

        internal static bool HasSubscribers => Volatile.Read(ref _subscriberCount) > 0;
        internal static bool HasFollowerSubscribers => Volatile.Read(ref _followerSubscriberCount) > 0;

        public static void Register(string id, Action<BotOwner> callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null) return;

            lock (SyncRoot)
            {
                Subscribers[id] = callback;
                RefreshSnapshot();
            }
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (SyncRoot)
            {
                Subscribers.Remove(id);
                RefreshSnapshot();
            }
        }

        internal static void RegisterFollower(string id, Action<BotOwner> callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null) return;

            lock (SyncRoot)
            {
                FollowerSubscribers[id] = callback;
                RefreshFollowerSnapshot();
            }
        }

        internal static void UnregisterFollower(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (SyncRoot)
            {
                FollowerSubscribers.Remove(id);
                RefreshFollowerSnapshot();
            }
        }

        internal static void Invoke(BotOwner owner)
        {
            if (!HasSubscribers) return;

            Action<BotOwner>[] callbacks = Volatile.Read(ref _callbackSnapshot);
            InvokeCallbacks(owner, callbacks);
        }

        internal static void InvokeFollower(BotOwner owner)
        {
            if (!HasFollowerSubscribers) return;

            Action<BotOwner>[] callbacks = Volatile.Read(ref _followerCallbackSnapshot);
            InvokeCallbacks(owner, callbacks);
        }

        private static void InvokeCallbacks(BotOwner owner, Action<BotOwner>[] callbacks)
        {
            if (callbacks.Length == 0) return;

            foreach (Action<BotOwner> callback in callbacks)
            {
                try
                {
                    callback(owner);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Exception in BotOwnerUpdateHub callback");
                    Logger.LogError(ex);
                }
            }
        }

        private static void RefreshSnapshot()
        {
            Action<BotOwner>[] snapshot = CopyCallbacks();
            Volatile.Write(ref _callbackSnapshot, snapshot);
            Volatile.Write(ref _subscriberCount, snapshot.Length);
        }

        private static void RefreshFollowerSnapshot()
        {
            Action<BotOwner>[] snapshot = CopyCallbacks(FollowerSubscribers);
            Volatile.Write(ref _followerCallbackSnapshot, snapshot);
            Volatile.Write(ref _followerSubscriberCount, snapshot.Length);
        }

        private static Action<BotOwner>[] CopyCallbacks()
        {
            return CopyCallbacks(Subscribers);
        }

        private static Action<BotOwner>[] CopyCallbacks(Dictionary<string, Action<BotOwner>> subscribers)
        {
            if (subscribers.Count == 0)
            {
                return Array.Empty<Action<BotOwner>>();
            }

            Action<BotOwner>[] callbacks = new Action<BotOwner>[subscribers.Count];
            subscribers.Values.CopyTo(callbacks, 0);
            return callbacks;
        }
    }
}
