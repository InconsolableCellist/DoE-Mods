using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DoEFriendsMod.Face
{
    /// <summary>
    /// The latest value of every face-tracking parameter, written by the OSC socket thread and
    /// read by Unity's main thread.
    ///
    /// Deliberately a latest-value store rather than a queue: face tracking is a continuous
    /// signal, so a value that arrived two frames ago and was superseded is of no interest, and
    /// a queue would only let a backlog build up. Writers never block, and the reader never
    /// waits — the worst case is reading a value one tick old, which is invisible at 100 Hz.
    ///
    /// Nothing here touches a Unity API. That matters: OSC arrives on a socket thread, and
    /// calling into Unity off the main thread is an immediate crash under IL2CPP.
    /// </summary>
    public class FaceState
    {
        private readonly ConcurrentDictionary<string, float> _values =
            new ConcurrentDictionary<string, float>(StringComparer.Ordinal);

        private long _messageCount;
        private long _lastReceivedTicks;

        public long MessageCount => System.Threading.Interlocked.Read(ref _messageCount);

        /// <summary>Seconds since anything arrived, or -1 if nothing ever has.</summary>
        public double SecondsSinceLastMessage
        {
            get
            {
                var ticks = System.Threading.Interlocked.Read(ref _lastReceivedTicks);
                return ticks == 0 ? -1 : (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
            }
        }

        public void Set(string name, float value)
        {
            _values[name] = value;
            System.Threading.Interlocked.Increment(ref _messageCount);
            System.Threading.Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
        }

        public bool TryGet(string name, out float value) => _values.TryGetValue(name, out value);

        public float Get(string name, float fallback = 0f) =>
            _values.TryGetValue(name, out var value) ? value : fallback;

        public int Count => _values.Count;

        /// <summary>Snapshot of the parameter names seen so far — for diagnostics only.</summary>
        public string[] Names()
        {
            var names = new string[_values.Count];
            var i = 0;
            foreach (var kv in _values)
            {
                if (i >= names.Length) break;
                names[i++] = kv.Key;
            }
            return names;
        }

        public void Clear() => _values.Clear();

        /// <summary>Every parameter seen so far with its latest value, sorted by name.</summary>
        public List<KeyValuePair<string, float>> Snapshot()
        {
            var list = new List<KeyValuePair<string, float>>(_values.Count);
            foreach (var kv in _values) list.Add(kv);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }
}
