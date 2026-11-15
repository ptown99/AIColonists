using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AIColonists.Social
{
    /// <summary>One Layer 2 decision that survived validation at dispatch time (Spec §7.3).</summary>
    public class QueuedSocialDecision
    {
        public string colonistName = "";   // resolved to a pawn at consume time — names can go stale between call and consume
        public string targetName = "";     // roster name of the other colonist, empty for "none"
        public string socialImpulse = "";  // warm_chat | vent_frustration | sharp_remark | quiet_nod | avoid_gaze
        public string spokenLine = "";     // what the initiator says aloud (becomes the letter text)
        public string innerFeeling = "";   // first-person feeling behind the impulse (episodic-memory fodder)
        public int pushedGameTick;         // staleness guard: decisions older than the window are dropped at consume time
    }

    /// <summary>Thread-safe buffer between Layer 2 workers (background threads) and the main-thread consumer.</summary>
    public class SocialDecisionBuffer
    {
        private readonly ConcurrentQueue<QueuedSocialDecision> queue = new ConcurrentQueue<QueuedSocialDecision>();

        public void Enqueue(QueuedSocialDecision d) => queue.Enqueue(d);

        /// <summary>Drains every queued decision in arrival order. Main thread only.</summary>
        public List<QueuedSocialDecision> Drain()
        {
            var list = new List<QueuedSocialDecision>();
            QueuedSocialDecision d;
            while (queue.TryDequeue(out d)) list.Add(d);
            return list;
        }

        /// <summary>Drops everything pending (map unload / AI disabled).</summary>
        public void Clear()
        {
            QueuedSocialDecision d;
            while (queue.TryDequeue(out d)) { }
        }
    }

    /// <summary>
    /// One-shot payload from AIColonistGameComponent to InteractionWorker_AISocial, keyed by the initiator/target pair.
    /// Consumed on read: a stale line can never leak into a later interaction of the same pair. Main thread only.
    /// </summary>
    public static class PendingLetterPayload
    {
        private sealed class Payload
        {
            public string spokenLine = "";
            public string innerFeeling = "";
        }

        private static readonly Dictionary<string, Payload> payloads = new Dictionary<string, Payload>();

        private static string Key(string initiatorThingId, string targetThingId) => (initiatorThingId ?? "") + ">" + (targetThingId ?? "");

        public static void Set(string initiatorThingId, string targetThingId, string spokenLine, string innerFeeling)
        {
            payloads[Key(initiatorThingId, targetThingId)] = new Payload
            {
                spokenLine = spokenLine ?? "",
                innerFeeling = innerFeeling ?? ""
            };
        }

        /// <summary>Reads AND removes the payload for this pair (one-shot). False when nothing is pending.</summary>
        public static bool TryGet(string initiatorThingId, string targetThingId, out string spokenLine, out string innerFeeling)
        {
            Payload p;
            if (payloads.TryGetValue(Key(initiatorThingId, targetThingId), out p))
            {
                payloads.Remove(Key(initiatorThingId, targetThingId));
                spokenLine = p.spokenLine;
                innerFeeling = p.innerFeeling;
                return true;
            }
            spokenLine = null;
            innerFeeling = null;
            return false;
        }

        /// <summary>Drops a pending payload without consuming it (interaction preconditions failed — the line must not leak).</summary>
        public static void Remove(string initiatorThingId, string targetThingId) => payloads.Remove(Key(initiatorThingId, targetThingId));

        public static void Clear() => payloads.Clear();
    }
}
