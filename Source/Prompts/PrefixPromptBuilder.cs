using System.Collections.Generic;
using System.Text;

namespace AIColonists.Prompts
{
    /// <summary>
    /// Assembles Layer 1 prompts in strict two-zone order (Spec §2.3, §7.1): static identity prefix FIRST
    /// (KV-cache shared across calls via llama-server --cache-reuse / Ollama's automatic prefix caching), dynamic
    /// experience suffix LAST. This ordering is what makes the ~70% prefill-compute reduction real (Spec §8.2).
    /// Phase 2: the identity zone is now REAL per-pawn data (name, age, colony, traits, skills, memories, drift-fed
    /// values/fears) instead of placeholder text — two colonists no longer share one generic "a colonist" prefix.
    /// </summary>
    public static class PrefixPromptBuilder
    {
        // Spec §7.1 — system message = static prefix zone. [Brackets] are placeholders.
        private const string SystemTemplate = @"# IDENTITY (STATIC PREFIX - KV CACHED)
You are [Name], age [Age], living in the settlement of [ColonyName].
You experience this world as physical reality. You have no concept of games, stats, or simulations.

## WHO YOU ARE
[BackstoryAndCoreTraitSummary]
Speech & Thought Cadence: [CadenceRuleFromIntellectAndTraits]

## WHAT YOU VALUE & FEAR
- Values: [TopValues]
- Dislikes: [TopAversions]

## LONG-TERM MEMORIES & HABITS
[MemoryLines]";

        // Spec §7.1 — user message = dynamic suffix zone (evaluated per call).
        private const string UserTemplate = @"# CURRENT EXPERIENCE (DYNAMIC SUFFIX)
## RECENT EVENTS ON YOUR MIND
[EpisodicLines]

## PEOPLE NEARBY
[NearbyPawnLines]

## YOUR BODY & SURROUNDINGS
[BodyAndSurroundingsLines]
[IntellectGatedColonyHorizon]

## POSSIBLE IMPULSES RIGHT NOW
[AffordanceList]

Express your fleeting inner thought in first person, optional spoken mutter, and the number of the impulse you act on.";

        /// <summary>Static prefix zone — changes only during Layer 3 consolidation (every ~5 min) or rare identity events
        /// (age birthday, skill name change), so it stays KV-cached between the ~30 s Layer 1 calls.</summary>
        public static string BuildSystemPrompt(SensorySnapshot snapshot)
        {
            string s = SystemTemplate;

            // Identity — real per-pawn data captured on the main thread (Spec §7.1). Fallbacks keep a half-loaded pawn
            // (save-load edge) producing a valid, if sparse, prefix instead of an exception.
            s = s.Replace("[Name]", string.IsNullOrEmpty(snapshot.PawnName) ? "a colonist" : snapshot.PawnName);
            s = s.Replace("[Age]", snapshot.AgeYears > 0 ? snapshot.AgeYears.ToString() : "unknown");
            s = s.Replace("[ColonyName]", string.IsNullOrEmpty(snapshot.ColonyName) ? "your settlement" : snapshot.ColonyName);

            var who = new StringBuilder();
            if (!string.IsNullOrEmpty(snapshot.TraitsLine)) who.Append(snapshot.TraitsLine).Append('\n');
            if (!string.IsNullOrEmpty(snapshot.SkillsLine)) who.Append(snapshot.SkillsLine);
            s = s.Replace("[BackstoryAndCoreTraitSummary]", who.Length > 0 ? who.ToString().TrimEnd() : "You are a practical, self-reliant person.");

            s = s.Replace("[CadenceRuleFromIntellectAndTraits]", CadenceRuleFor(snapshot.IntellectualLevel));
            s = s.Replace("[TopValues]", JoinOrFallback(snapshot.ValueLines, "safety, food, rest"));
            s = s.Replace("[TopAversions]", JoinOrFallback(snapshot.AversionLines, "pain, hunger"));

            var mem = new StringBuilder();
            if (snapshot.SemanticMemoryLines != null)
                foreach (string line in snapshot.SemanticMemoryLines) mem.Append("- ").Append(line).Append('\n');
            if (!string.IsNullOrEmpty(snapshot.LongTermLonging))
                mem.Append("- Deep down, you keep wanting ").Append(snapshot.LongTermLonging).Append(".\n");
            s = s.Replace("[MemoryLines]", mem.Length > 0 ? mem.ToString().TrimEnd() : "- (nothing yet — your days are still plain)");

            return s;
        }

        /// <summary>Dynamic suffix zone — everything that changes call-to-call lives here.</summary>
        public static string BuildUserPrompt(SensorySnapshot snap)
        {
            string u = UserTemplate;
            u = u.Replace("[EpisodicLines]", JoinOrDash(snap.EpisodicLines));
            u = u.Replace("[NearbyPawnLines]", JoinOrDash(snap.NearbyPawnLines));
            u = u.Replace("[BodyAndSurroundingsLines]", JoinOrDash(snap.BodyAndSurroundingsLines));
            // Colony horizon is captured at snapshot time (pure function of the Intellectual skill — Spec §3.2).
            u = u.Replace("[IntellectGatedColonyHorizon]", string.IsNullOrEmpty(snap.ColonyHorizonLine) ? "" : snap.ColonyHorizonLine);

            var sb = new StringBuilder();
            if (snap.Affordances != null)
            {
                for (int i = 0; i < snap.Affordances.Count; i++)
                    sb.Append("[").Append(i + 1).Append("] ").Append(snap.Affordances[i].sensoryDescription).Append("\n");
            }
            u = u.Replace("[AffordanceList]", sb.ToString().TrimEnd());

            return u;
        }

        // ---------------------------------------------------------------- Phase 2: Intellect-to-Horizon (Spec §3.2)

        /// <summary>Speech/thought cadence by Intellectual skill tier — the "Intellect" of Spec §3.2 is the 1.6
        /// Intellectual skill (the learning-system stat; vanilla has no separate intellect attribute).</summary>
        public static string CadenceRuleFor(int intellectLevel)
        {
            if (intellectLevel < 0) return "short, plain sentences."; // unknown — neutral default
            if (intellectLevel < 5) return "short, blunt sentences; you act on the first impulse that feels right.";
            if (intellectLevel < 10) return "plain sentences of a few words; you pause to notice details before acting.";
            return "measured, layered thoughts; you weigh consequences and other people's feelings.";
        }

        /// <summary>Colony-horizon awareness by Intellectual tier: low intellect lives in the immediate moment, high
        /// intellect keeps track of the settlement's wider state (Spec §3.2 horizon table).</summary>
        public static string ColonyHorizonFor(int intellectLevel)
        {
            if (intellectLevel < 0 || intellectLevel < 5) return ""; // no horizon — only immediate urges
            if (intellectLevel < 10) return "You keep a loose sense of how the settlement is doing today.";
            return "You track the settlement's wider state — who is hurt, what is stored, whether danger has passed.";
        }

        private static string JoinOrFallback(List<string> lines, string fallback)
        {
            if (lines == null || lines.Count == 0) return fallback;
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        private static string JoinOrDash(List<string> lines)
        {
            if (lines == null || lines.Count == 0) return "- (nothing in particular)";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
                sb.Append("- ").Append(lines[i]).Append("\n");
            return sb.ToString().TrimEnd();
        }
    }
}
