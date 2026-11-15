using System.Collections.Generic;
using System.Text;

namespace AIColonists.Prompts
{
    /// <summary>
    /// Assembles Layer 1 prompts in strict two-zone order (Spec §2.3, §7.1): static identity prefix FIRST
    /// (KV-cache shared across calls via llama-server --cache-reuse), dynamic experience suffix LAST.
    /// This ordering is what makes the ~70% prefill-compute reduction real (Spec §8.2).
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
- [SemanticMemory1]
- [SemanticMemory2]
- [ProceduralHabit1]";

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

        /// <summary>Static prefix zone — changes only during Layer 3 consolidation (every ~5 min), so it stays KV-cached.</summary>
        public static string BuildSystemPrompt(SensorySnapshot snapshot)
        {
            // TODO(Phase 2): pull identity from ColonistMindState (backstory, trait weights, semantic memories).
            //   Until then the prefix is minimal but still stable per-pawn — enough to exercise --cache-reuse.
            string s = SystemTemplate;
            s = s.Replace("[Name]", "a colonist");          // TODO: snapshot.PawnName
            s = s.Replace("[Age]", "?");                     // TODO: pawn.ageTracker.AgeBiological
            s = s.Replace("[ColonyName]", "your settlement"); // TODO: Find.CurrentMap.Parent.SettlementName
            s = s.Replace("[BackstoryAndCoreTraitSummary]", "You are a practical, self-reliant person.");
            s = s.Replace("[CadenceRuleFromIntellectAndTraits]", "short, blunt sentences."); // TODO: intellect-tier cadence (Spec §3.2)
            s = s.Replace("[TopValues]", "safety, food, rest");
            s = s.Replace("[TopAversions]", "pain, hunger");
            s = s.Replace("[SemanticMemory1]", "You keep to yourself most of the day.");
            s = s.Replace("[SemanticMemory2]", "");
            s = s.Replace("[ProceduralHabit1]", "You check on things before you leave them.");
            return s;
        }

        /// <summary>Dynamic suffix zone — everything that changes call-to-call lives here.</summary>
        public static string BuildUserPrompt(SensorySnapshot snap)
        {
            string u = UserTemplate;
            u = u.Replace("[EpisodicLines]", JoinOrDash(snap.EpisodicLines));
            u = u.Replace("[NearbyPawnLines]", JoinOrDash(snap.NearbyPawnLines));
            u = u.Replace("[BodyAndSurroundingsLines]", JoinOrDash(snap.BodyAndSurroundingsLines));
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
