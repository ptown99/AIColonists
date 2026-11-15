using System.Collections.Generic;
using Verse;

namespace AIColonists.State
{
    /// <summary>Four-part memory store (Spec §4.1 differentiation layer 5).</summary>
    public class MemoryStore : IExposable
    {
        // ~30 in-game days at 1x speed (verify tick rate against Find.TickManager on your build).
        public const int TicksPerDay = 86400;

        public List<MemoryEntry> episodic = new List<MemoryEntry>();   // specific events with tick stamps ("Alice pulled me out of the fire")
        public List<string> semantic = new List<string>();             // condensed facts — Layer 3 consolidation output (Spec §7.4)
        public List<string> procedural = new List<string>();           // habits & routines ("I always check the stove before bed")
        public List<string> emotional = new List<string>();            // traumas, comforts, fixations

        private const int MaxEpisodic = 40;   // TODO(Phase 2): tune per intellect horizon tier (Spec §3.2)
        private const int MaxSemantic = 25;
        private const int MaxProcedural = 15;
        private const int MaxEmotional = 25;

        public void AddEpisodic(string text, int tick)
        {
            if (string.IsNullOrEmpty(text)) return;
            episodic.Add(new MemoryEntry(text, tick));
            if (episodic.Count > MaxEpisodic) episodic.RemoveAt(0);
        }

        public void AddSemantic(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            semantic.Add(text);
            if (semantic.Count > MaxSemantic) semantic.RemoveAt(0);
        }

        public void AddProcedural(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            procedural.Add(text);
            if (procedural.Count > MaxProcedural) procedural.RemoveAt(0);
        }

        public void AddEmotional(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            emotional.Add(text);
            if (emotional.Count > MaxEmotional) emotional.RemoveAt(0);
        }

        /// <summary>Episodic memory texts in age order — Layer 3 prompt assembly (Spec §7.4).</summary>
        public List<string> EpisodicTexts()
        {
            var list = new List<string>();
            foreach (MemoryEntry e in episodic) list.Add(e.text);
            return list;
        }

        /// <summary>Layer 3 consolidation: prune episodic entries older than ~30 in-game days (Spec §9 Phase 3). Returns count pruned.</summary>
        public int PruneEpisodicOlderThan(int nowTick, int maxAgeTicks = 30 * TicksPerDay)
        {
            int removed = 0;
            while (episodic.Count > 0 && nowTick - episodic[0].tick > maxAgeTicks)
            {
                // TODO(Phase 3): before pruning, hand the batch to Layer 3 for semantic summarization.
                episodic.RemoveAt(0);
                removed++;
            }
            return removed;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref episodic, "episodic", LookMode.Deep);
            Scribe_Collections.Look(ref semantic, "semantic", LookMode.Value);
            Scribe_Collections.Look(ref procedural, "procedural", LookMode.Value);
            Scribe_Collections.Look(ref emotional, "emotional", LookMode.Value);
        }
    }

    /// <summary>A single timestamped episodic memory.</summary>
    public class MemoryEntry : IExposable
    {
        public string text = "";
        public int tick;

        public MemoryEntry() { }

        public MemoryEntry(string t, int tk)
        {
            text = t;
            tick = tk;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref tick, "tick", 0);
        }
    }
}
