using System;
using System.Collections.Generic;
using AIColonists.Core;
using AIColonists.State;
using RimWorld;
using Verse;

namespace AIColonists.UI
{
    /// <summary>
    /// Phase 4 (Spec §9): pure view-model for ITab_Pawn_AIMind — converts live mind state into capped display
    /// lines so the tab's FillTab stays a thin drawer. The mapping (caps, ordering, name resolution) is pinned by
    /// the no-game harness; the drawing itself needs Unity and is verified in-game.
    /// </summary>
    public static class AIMindViewModel
    {
        // Display caps — pawn tabs are fixed-size with no scroll, so every section is bounded (Spec §9 Phase 4).
        public const int MaxRecentMemories = 3;   // newest first
        public const int MaxTraitBars = 6;        // most drifted from neutral (0.5) first
        public const int MaxRelationships = 4;    // most recently interacted first

        public class TraitBar
        {
            public string name = "";
            public float value; // 0..1 continuous weight (Spec §4.1)
        }

        public class RelationshipLine
        {
            public string targetName = "";
            public int lastInteractionTick; // ordering only — the UI shows the four main axes
            public float trust, affection, respect, frustration;
        }

        public class View
        {
            public bool hasAnyData;              // false → tab shows a "no AI data yet" hint
            public string innerMonologue = "";   // last monologue that actually turned into a job
            public int monologueAgeTicks;        // 0 when there is no monologue
            public string longing = "";          // long-term desire (Layer 3 output)
            public List<string> recentMemories = new List<string>();
            public string latestSemantic = "";   // newest distilled self-knowledge
            public List<TraitBar> traits = new List<TraitBar>();
            public List<RelationshipLine> relationships = new List<RelationshipLine>();
        }

        /// <summary>Main thread. Thin wrapper — resolves the pawn's mind state from the world component.</summary>
        public static View Build(Pawn pawn, int nowTick)
        {
            try
            {
                if (pawn == null || Find.World == null) return new View();
                AIColonistWorldComponent world = Find.World.GetComponent<AIColonistWorldComponent>();
                if (world == null) return new View();
                return BuildFromMind(pawn, world.GetOrCreateMindState(pawn), nowTick);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[AIColonists] AIMindViewModel.Build: " + e.Message, 4820); // observational — the tab must never break pawn inspection
                return new View();
            }
        }

        /// <summary>Pure mind-state → display mapping (harness-pinned). No Find/World access.</summary>
        public static View BuildFromMind(Pawn pawn, ColonistMindState mind, int nowTick)
        {
            var v = new View();
            if (pawn == null || mind == null) return v;

            if (!string.IsNullOrEmpty(mind.lastInnerVoice))
            {
                v.innerMonologue = mind.lastInnerVoice;
                v.monologueAgeTicks = nowTick > mind.lastInnerVoiceTick ? nowTick - mind.lastInnerVoiceTick : 0;
            }

            v.longing = mind.longTermLonging ?? "";

            // Episodic memories, newest first.
            List<MemoryEntry> epi = mind.memory.episodic;
            for (int i = epi.Count - 1; i >= 0 && v.recentMemories.Count < MaxRecentMemories; i--)
                if (epi[i] != null && !string.IsNullOrEmpty(epi[i].text))
                    v.recentMemories.Add(epi[i].text);

            List<string> sem = mind.memory.semantic;
            if (sem.Count > 0) v.latestSemantic = sem[sem.Count - 1];

            // Trait drift, most drifted from neutral first; name breaks ties for a stable order.
            var traits = new List<TraitBar>(mind.traitWeights.Count);
            foreach (KeyValuePair<string, float> kv in mind.traitWeights)
                if (!string.IsNullOrEmpty(kv.Key))
                    traits.Add(new TraitBar { name = kv.Key, value = kv.Value });
            traits.Sort(delegate (TraitBar a, TraitBar b)
            {
                int c = Math.Abs(b.value - 0.5f).CompareTo(Math.Abs(a.value - 0.5f));
                return c != 0 ? c : a.name.CompareTo(b.name);
            });
            for (int i = 0; i < traits.Count && v.traits.Count < MaxTraitBars; i++)
                v.traits.Add(traits[i]);

            // Relationships, most recently interacted first; target ThingIDs resolved to names on the map.
            var rels = new List<RelationshipLine>(mind.relationships.Count);
            foreach (RelationshipVector rv in mind.relationships)
            {
                if (rv == null || string.IsNullOrEmpty(rv.targetPawnId)) continue;
                rels.Add(new RelationshipLine
                {
                    targetName = ResolveTargetName(pawn, rv.targetPawnId),
                    lastInteractionTick = rv.lastInteractionTick,
                    trust = rv.trust,
                    affection = rv.affection,
                    respect = rv.respect,
                    frustration = rv.frustration,
                });
            }
            rels.Sort(delegate (RelationshipLine a, RelationshipLine b) { return b.lastInteractionTick.CompareTo(a.lastInteractionTick); });
            for (int i = 0; i < rels.Count && v.relationships.Count < MaxRelationships; i++)
                v.relationships.Add(rels[i]);

            v.hasAnyData = !string.IsNullOrEmpty(v.innerMonologue) || v.recentMemories.Count > 0
                           || !string.IsNullOrEmpty(v.latestSemantic) || !string.IsNullOrEmpty(v.longing)
                           || v.traits.Count > 0 || v.relationships.Count > 0;
            return v;
        }

        /// <summary>ThingID → LabelCap among this map's free colonists; "unknown" when the target is gone.</summary>
        private static string ResolveTargetName(Pawn pawn, string thingId)
        {
            if (pawn.Map != null)
            {
                foreach (Pawn other in pawn.Map.mapPawns.FreeColonistsSpawned)
                    if (other.ThingID == thingId && !string.IsNullOrEmpty(other.LabelCap))
                        return other.LabelCap;
            }
            return "unknown";
        }
    }
}
