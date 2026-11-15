using System.Collections.Generic;
using AIColonists.Core;
using AIColonists.Prompts;
using AIColonists.State;
using AIColonists.UI;
using Verse;
using Verse.AI;

namespace AIColonists.ThinkTree
{
    /// <summary>
    /// Non-blocking desire consumer (Spec §6.2). Pops a pre-validated impulse from the async intent buffer in
    /// &lt;0.01ms, or returns null immediately so vanilla AI handles the pawn until the next LLM response lands.
    /// Zero recursion: we never start jobs inside job-start hooks — RimWorld's own job tracker consumes our Job
    /// like any other ThinkTree result (Spec §1 fix).
    /// </summary>
    public class JobGiver_AIColonistDesire : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Settings settings = AIColonistMod.Settings;
            if (settings == null || !settings.EnableAI || pawn.Drafted || pawn.InMentalState)
                return null;

            AIColonistGameComponent manager = pawn.Map.GetComponent<AIColonistGameComponent>();
            if (manager == null) return null;

            // 1. Did the background LLM worker deposit a validated desire?
            if (manager.IntentBuffer.TryPopDesire(pawn.ThingID, out QueuedDesire desire))
            {
                // Re-verify on the main thread — the world may have moved while the LLM was thinking (Spec §10).
                if (desire.IsValidAndReservable(pawn))
                {
                    Job job = JobMaker.MakeJob(desire.jobDef, desire.targetA, desire.targetB);
                    job.expiryInterval = 1800; // re-evaluate after ~30s of game time if long-running (Spec §6.2)
                    MoteBubbleUtility.TryThrowThoughtMote(pawn, desire.innerVoiceShort);

                    // Phase 4: record the monologue for the AI Mind tab (Spec §9). Main thread — safe to touch
                    // mind state here; guarded so a missing world component can never break job giving.
                    if (Find.World != null)
                    {
                        AIColonistWorldComponent world = Find.World.GetComponent<AIColonistWorldComponent>();
                        if (world != null)
                        {
                            ColonistMindState mind = world.GetOrCreateMindState(pawn);
                            mind.lastInnerVoice = desire.innerVoiceShort ?? "";
                            mind.lastInnerVoiceTick = Find.TickManager.TicksGame;
                        }
                    }

                    return job;
                }
                // Stale target: discard silently and fall through to vanilla AI.
            }

            // 2. Cooldown expired and no request in flight? Snapshot affordances & queue the async call.
            if (manager.ShouldQueueEvaluation(pawn))
            {
                List<AffordanceOption> affordances = AffordanceScanner.Scan(pawn);
                SensorySnapshot snapshot = SensoryFramingBuilder.CaptureMainThreadSnapshot(pawn, affordances);
                manager.EnqueueAsyncLayer1(snapshot);
            }

            // 3. Return null immediately (<0.01ms) — vanilla ThinkTree takes over (Spec §6.2).
            return null;
        }
    }
}
