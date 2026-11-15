using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AIColonists.Social
{
    /// <summary>
    /// Worker for the three LLM-driven interaction defs (Spec §7.3). Natural triggering is suppressed: RandomSelectionWeight
    /// returns 0f, so the vanilla random roll can never pick these defs — only AIColonistGameComponent fires them via
    /// pawn.interactions.TryInteractWith, and it stashes the LLM's line in PendingLetterPayload for this exact pair first.
    /// Interacted reads that one-shot payload and hands it to VANILLA letter composition: TryInteractWith composes
    /// &lt;play-log sentence&gt; + "\n\n" + our text, spawns the def symbol mote, and writes the play-log entry — all for free.
    /// </summary>
    public class InteractionWorker_AISocial : InteractionWorker
    {
        private const int ErrorTag = 4810;

        // 0f: these defs are never picked by the vanilla random interaction roll (Pawn_InteractionsTracker).
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient) => 0f;

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks, out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            // Vanilla-style default: no letter. The LLM-driven path fills these in below.
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            try
            {
                if (initiator == null || recipient == null || interaction == null) return;

                string spokenLine, innerFeeling;
                if (!PendingLetterPayload.TryGet(initiator.ThingID, recipient.ThingID, out spokenLine, out innerFeeling))
                    return; // no pending LLM decision for this pair — nothing to say (shouldn't happen: only we call TryInteractWith)

                string line = !string.IsNullOrEmpty(spokenLine) ? spokenLine : innerFeeling;
                if (string.IsNullOrEmpty(line)) return;

                letterText = line;
                letterLabel = interaction.LabelCap; // def label, e.g. "Warm chat" — TaggedString converts to string
                letterDef = LetterDefOf.PositiveEvent;

                switch (interaction.defName)
                {
                    case "AIColonist_WarmChat":
                        letterDef = LetterDefOf.PositiveEvent;
                        break;
                    case "AIColonist_VentFrustration":
                        letterDef = LetterDefOf.NeutralEvent;
                        break;
                    case "AIColonist_SharpRemark":
                        letterDef = LetterDefOf.NegativeEvent;
                        break;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[AIColonists] InteractionWorker_AISocial: " + e.Message, ErrorTag);
            }
        }
    }
}
