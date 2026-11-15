using System;
using System.Collections.Generic;
using AIColonists.ThinkTree;
using Verse;

namespace AIColonists.Prompts
{
    /// <summary>Value-safe snapshot of a pawn's current experience, captured on the main thread (Spec §5.1 / §7.1).</summary>
    public struct SensorySnapshot
    {
        public string PawnId;
        // 2026-11-15: display label for log lines — L1 logs used to print the raw ThingID ("Human1006"), which reads like a
        // different pawn than the LabelCap-based thinks:/mote lines. PawnId stays the buffer/call-state key (stable across renames).
        public string PawnName;
        public int CaptureTick;
        public List<string> EpisodicLines;            // recent events on the mind
        public List<string> NearbyPawnLines;          // relational framing of people nearby
        public List<string> BodyAndSurroundingsLines; // needs, pain, room state — zero mechanical terms (Appendix A)
        public string ColonyHorizonLine;              // intellect-gated colony awareness (Spec §3.2)
        public List<AffordanceOption> Affordances;    // numbered sensory options (optionId = index + 1)
    }

    /// <summary>
    /// Translates vanilla state into sensory prose (Spec §4.1 layer 7, Appendix A). Mechanical terms NEVER reach the
    /// prompt — this is what keeps the "Unaware" constraint intact on the input side (Spec §2.2). Main thread only.
    /// </summary>
    public static class SensoryFramingBuilder
    {
        public static SensorySnapshot CaptureMainThreadSnapshot(Pawn pawn, List<AffordanceOption> affordances)
        {
            var snap = new SensorySnapshot
            {
                PawnId = pawn.ThingID,
                PawnName = SafeLabel(pawn), // may be null (save-load) — log line falls back to the ID
                CaptureTick = Find.TickManager.TicksGame,
                Affordances = affordances,
                BodyAndSurroundingsLines = FrameBody(pawn),
            };

            // TODO(Phase 2): episodic lines from ColonistMindState.memory (most recent 3–5, per intellect horizon §3.2).
            // TODO(Phase 2): nearby-pawn relational framing from RelationshipVectors within 15 tiles (Spec §4.2).
            // TODO(Phase 2): intellect-gated colony horizon — low intellect sees only immediate urges; Genius tier
            //   receives weather, stockpile warnings, raid-recovery state (Spec §3.2 table).

            return snap;
        }

        /// <summary>Appendix A translation rules: game state → first-person sensory prose.</summary>
        private static List<string> FrameBody(Pawn pawn)
        {
            var lines = new List<string>();

            if (pawn.needs.food.CurLevelPercentage < 0.25f)
                lines.Add("Your stomach cramps sharply; you haven't eaten in hours.");
            else if (pawn.needs.food.CurLevelPercentage < 0.4f)
                lines.Add("You're getting hungry.");

            if (pawn.needs.rest != null && pawn.needs.rest.CurLevelPercentage < 0.2f)
                lines.Add("Your eyelids feel like lead and your legs ache with every step.");

            // 1.6 verified: HediffSet.PainTotal (public float, cached with DirtyPainCache()).
            // TODO(Phase 2): add wound-location phrasing from hediffSet.
            if (pawn.health.hediffSet.PainTotal > 0.3f)
                lines.Add("A dull, throbbing heat radiates from your worst wound.");

            // TODO(Phase 2): negative mood thoughts → prose ("still annoyed about eating off the cold floor", Appendix A).
            // TODO(Phase 2): room stats → prose (cleanliness, light, warmth — "the floor is crusted with dried grime").

            return lines;
        }

        /// <summary>Taste/temperature flavor for food affordances. TODO(Phase 2): derive from CompQuality + recipe.</summary>
        public static string FoodAdjective(Thing meal) => "hot";

        /// <summary>LabelCap with a save-load guard: on a half-loaded pawn the computed label can throw (live-observed 2026-11-14);
        /// an uncaught throw here would kill the pawn's think branch for every evaluation.</summary>
        private static string SafeLabel(Pawn pawn)
        {
            try { return pawn.LabelCap; }
            catch (Exception e) { Log.ErrorOnce("[AIColonists] LabelCap threw during snapshot capture (save load?): " + e.Message, 4814); return null; }
        }
    }
}
