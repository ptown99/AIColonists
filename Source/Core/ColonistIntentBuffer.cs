using System.Collections.Concurrent;
using AIColonists.ThinkTree;
using RimWorld;
using Verse;
using Verse.AI; // 1.6: PathEndMode lives in Verse.AI

namespace AIColonists.Core
{
    /// <summary>
    /// Thread-safe producer-consumer intent buffer (Spec §1 fix, §6.2). Background LLM workers push pre-validated
    /// desires; the main-thread JobGiver pops them in &lt;0.01ms. Latest desire per pawn wins (stale ones are replaced).
    /// </summary>
    public class ColonistIntentBuffer
    {
        private readonly ConcurrentDictionary<string, QueuedDesire> desires =
            new ConcurrentDictionary<string, QueuedDesire>();

        public void Push(string pawnThingId, QueuedDesire desire) => desires[pawnThingId] = desire;

        public bool TryPopDesire(string pawnThingId, out QueuedDesire desire) =>
            desires.TryRemove(pawnThingId, out desire);

        /// <summary>Spec §6.3: non-combat desires are void the instant a pawn is drafted.</summary>
        public void Purge(string pawnThingId) { desires.TryRemove(pawnThingId, out _); }
    }

    /// <summary>A validated impulse: JobDef + concrete target resolved from an AffordanceOption at decision time.</summary>
    public class QueuedDesire
    {
        public JobDef jobDef;
        public LocalTargetInfo targetA;
        public LocalTargetInfo targetB;
        public string innerVoiceShort; // rendered as a thought bubble (Spec §6.2)

        public QueuedDesire(AffordanceOption option, string innerVoice)
        {
            jobDef = option.jobDef;
            targetA = option.targetA;
            targetB = option.targetB;
            innerVoiceShort = innerVoice;
        }

        /// <summary>
        /// Re-verify on the MAIN THREAD that the world hasn't moved while the LLM was thinking (Spec §10:
        /// "Target Desync" — the meal got eaten, the vein got mined out). Never call from a worker thread.
        /// </summary>
        public bool IsValidAndReservable(Pawn pawn)
        {
            if (jobDef == null || !targetA.HasThing) return false;

            // Freshness re-check (2026-11-13, live observation: "I need food" fired right after a finished meal):
            // the scanner only OFFERED this option while the driving need was below 0.4 — if the pawn has since
            // satisfied it through vanilla (ate / rested), the desire is stale and must not start a second job.
            // Mirrors AffordanceScanner.Scan's own gates exactly (food < 0.4; rest != null && < 0.4).
            if (jobDef == JobDefOf.Ingest && pawn.needs.food.CurLevelPercentage >= 0.4f) return false;
            if (jobDef == JobDefOf.LayDown && (pawn.needs.rest == null || pawn.needs.rest.CurLevelPercentage >= 0.4f)) return false;

            Thing t = targetA.Thing; // resolves the ThingReference — main thread only!
            if (t == null || t.Destroyed) return false;
            // 1.6 verified: CanReach is an extension method on Pawn (Verse.ReachabilityUtility) taking a
            // LocalTargetInfo + Danger — there is no (IntVec3, PathEndMode) overload anymore.
            if (!pawn.CanReach(new LocalTargetInfo(t), PathEndMode.Touch, Danger.Deadly)) return false;

            // TODO(Phase 1): full reservation check via pawn.ReservationManager before JobMaker.MakeJob.
            return true;
        }
    }
}
