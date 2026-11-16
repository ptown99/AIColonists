using System;
using System.Collections.Concurrent;
using Verse;

namespace AIColonists.Core
{
    /// <summary>
    /// Marshals log lines from background worker threads onto the Unity main thread (2026-11-16, live observation —
    /// TEST-RESULTS.md §23.3). RimWorld 1.6's in-game log window (LudeonTK.EditWindow_Log — an internal class inside
    /// Assembly-CSharp.dll, decompiled) enumerates the message queue WITHOUT holding Verse.Log's internal lock, while
    /// Verse.Log.Message only locks around ENQUEUE. Any Log.Message from a worker thread therefore races with an open
    /// log window and throws InvalidOperationException ("Collection was modified; enumeration operation may not execute")
    /// inside the window — observed live twice in one 10-minute session while this mod's Layer 1 dispatch lines logged
    /// from Task threads. All worker-thread log sites go through this queue instead; AIColonistGameComponent drains it
    /// once per tick on the main thread, where every other vanilla log call already happens.
    /// </summary>
    public static class MainThreadLog
    {
        private struct Pending
        {
            public string Text;
            public bool IsError;
            public int OnceKey; // <0 = plain Error, otherwise an ErrorOnce dedupe key
        }

        private static readonly ConcurrentQueue<Pending> Queue = new ConcurrentQueue<Pending>();

        /// <summary>Worker-thread replacement for Log.Message — delivered on the next main-thread drain.</summary>
        public static void Message(string text) => Enqueue(text, false, -1);

        /// <summary>Worker-thread replacement for Log.Error — delivered on the next main-thread drain.</summary>
        public static void Error(string text) => Enqueue(text, true, -1);

        /// <summary>Worker-thread replacement for Log.ErrorOnce — dedupe happens at drain time (main thread).</summary>
        public static void ErrorOnce(string text, int key) => Enqueue(text, true, key);

        private static void Enqueue(string text, bool isError, int onceKey)
        {
            if (text == null) return; // defensive rule — a null line must never wedge the queue
            Queue.Enqueue(new Pending { Text = text, IsError = isError, OnceKey = onceKey });
        }

        /// <summary>Drains all pending worker-thread lines onto the calling (main) thread. Called once per tick from
        /// AIColonistGameComponent.MapComponentTick — BEFORE its pause/AI-off early returns, so lines always flush.</summary>
        public static void Drain()
        {
            Pending p;
            while (Queue.TryDequeue(out p))
            {
                try
                {
                    if (!p.IsError) Log.Message(p.Text);
                    else if (p.OnceKey < 0) Log.Error(p.Text);
                    else Log.ErrorOnce(p.Text, p.OnceKey);
                }
                catch { /* defensive rule — a failing log line must never break the tick */ }
            }
        }

        /// <summary>Lines still waiting for the next main-thread drain (test/diagnostic hook).</summary>
        public static int PendingCount => Queue.Count;
    }
}
