using System;
using System.Reflection;
using AIColonists.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AIColonists.Patches
{
    /// <summary>
    /// Spec §6.3: capture social interaction outcomes — record episodic memories and update RelationshipVector
    /// trust/affection. Target verified on 1.6: the entry point is Pawn_InteractionsTracker.TryInteractWith(Pawn recipient, InteractionDef intDef)
    /// (public; vanilla calls it via pawn.interactions). The tracker's pawn field is private in 1.6, so we recover it
    /// through a cached reflection lookup — interactions are rare, the cost is negligible.
    /// Defensive by design: observational hook only — every state access null-guarded and wrapped, so vanilla flow
    /// can never break (see ThoughtHandlerPatches for the live failure that motivated this pattern).
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class InteractionTrackerPatches
    {
        private static readonly FieldInfo PawnField =
            typeof(Pawn_InteractionsTracker).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void TryInteractWith_Postfix(Pawn_InteractionsTracker __instance, Pawn recipient, InteractionDef intDef, bool __result)
        {
            try
            {
                Settings s = AIColonistMod.Settings;
                if (s == null || !s.EnableAI) return;
                if (!__result) return; // interaction didn't actually happen

                Pawn pawn = __instance != null ? (Pawn)PawnField.GetValue(__instance) : null;
                if (pawn == null || pawn.Faction == null || !pawn.Faction.IsPlayer) return;

                if (Find.World == null) return; // absent during early init

                AIColonistWorldComponent comp = Find.World.GetComponent<AIColonistWorldComponent>();
                if (comp == null) return;

                State.ColonistMindState mind = comp.GetOrCreateMindState(pawn);
                string who = recipient != null ? recipient.LabelCap : "someone";
                int ticks = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

                // Phase 3 (Spec §7.3 relationship capture): every successful interaction — vanilla OR LLM-driven —
                // leaves an episodic memory and nudges the initiator's vector toward the recipient.
                string line = "Had " + (intDef != null ? intDef.label : "an interaction") + " with " + who + ".";
                mind.memory.AddEpisodic(line, ticks);

                if (recipient != null)
                {
                    State.RelationshipVector rv = mind.GetOrCreateRelationship(recipient.ThingID);
                    rv.ApplyDeltas(0.015f, 0.01f, 0f, 0f); // trust +0.015, affection +0.01 — small on purpose (drift is Layer 3's job)
                    rv.lastInteractionTick = ticks;
                    rv.lastInteractionSummary = line;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[AIColonists] TryInteractWith_Postfix: " + e.Message, 4802);
            }
        }
    }
}
