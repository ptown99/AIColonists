using System;
using System.Collections.Generic;
using System.Text;

namespace AIColonists.Prompts
{
    /// <summary>
    /// Assembles Layer 3 prompts (Spec §7.4): the per-colonist subconscious consolidation call — raw episodic memories in,
    /// distilled self-knowledge out. The system zone is static PER COLONIST (identity + temperament), so --cache-reuse keeps
    /// that pawn's prefix hot across its ~5-minute consolidation cycles; only the memory zone changes between calls.
    /// </summary>
    public static class Layer3PromptBuilder
    {
        // Spec §7.4 — system message = static subconscious zone, parameterized by name once per colonist (KV-cached per pawn).
        private const string SystemTemplate = @"# ROLE (STATIC PREFIX - KV CACHED PER PERSON)
You are the quiet subconscious of [Name]. While they go about their day, you review what happened to them recently and let it settle into who they are.
You experience this as real inner life — no games, stats, or simulations exist in your world.

## HOW YOU WORK
- Review the recent memories below and notice what keeps recurring: people, places, fears, hopes, regrets.
- Write one short first-person self_reflection (1-3 sentences) about who they are becoming.
- Distill at most ONE new_semantic_memory: a single sentence of durable self-knowledge or belief that survives the details fading. Empty string when nothing new crystallized.
- relationship_deltas: name only people who appear in the memories, with small honest shifts (trust/affection/respect/frustration). Zero for everyone else.
- trait_drift: at most one temperament word they are drifting toward (for example ""stoic"", ""anxious"", ""warm"", ""bitter""), with a tiny shift; empty string when steady.
- long_term_longing: one sentence about what they quietly want, deep down. Empty string when nothing is stirring.";

        /// <summary>The static system zone for one colonist — byte-stable per pawn across consolidation cycles.</summary>
        public static string BuildSystemPrompt(string colonistName) =>
            SystemTemplate.Replace("[Name]", string.IsNullOrEmpty(colonistName) ? "someone" : colonistName);

        /// <summary>Dynamic zone: raw episodic memories + current self-knowledge + temperament (Spec §7.4).</summary>
        public static string BuildUserPrompt(IList<string> episodic, IList<string> semantic, string lastLonging, Dictionary<string, float> traitWeights)
        {
            var sb = new StringBuilder(1024);

            sb.Append("# RECENT MEMORIES\n");
            if (episodic != null && episodic.Count > 0)
                foreach (string m in episodic) sb.Append("- ").Append(m).Append('\n');
            else
                sb.Append("(nothing stands out yet)\n");

            sb.Append("\n# WHAT THEY BELIEVE ABOUT THEMSELVES\n");
            if (semantic != null && semantic.Count > 0)
                foreach (string m in semantic) sb.Append("- ").Append(m).Append('\n');
            else
                sb.Append("(no settled beliefs yet)\n");

            if (!string.IsNullOrEmpty(lastLonging))
                sb.Append("\n# WHAT THEY QUIETLY WANT\n- ").Append(lastLonging).Append('\n');

            string temperament = BuildTemperament(traitWeights);
            if (temperament.Length > 0)
                sb.Append("\n# TEMPERAMENT\n").Append(temperament).Append('\n');

            sb.Append("\nConsolidate.\n");
            return sb.ToString();
        }

        /// <summary>Temperament words from the continuous trait weights — sorted for byte-stable prompt output.</summary>
        private static string BuildTemperament(Dictionary<string, float> traitWeights)
        {
            if (traitWeights == null || traitWeights.Count == 0) return "";

            var parts = new List<string>();
            foreach (var kv in traitWeights)
                if (!string.IsNullOrEmpty(kv.Key))
                    parts.Add(kv.Value >= 0.6f ? "strongly " + kv.Key : kv.Key);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts);
        }
    }
}
