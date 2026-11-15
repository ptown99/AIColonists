using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AIColonists.Patches
{
    /// <summary>
    /// Live crash fix (2026-11-13, H4 50-colonist stress test): when the meal an Ingest job targets is destroyed while
    /// the pawn walks to it — routine in a food war where ~50 colonists race for limited meals — `job.targetA.Thing`
    /// resolves to NULL (proven twice: vanilla's own GainingNutritionNow guards with DestroyedOrNull(), and the observed
    /// crash was an NRE on the FIRST dereference). Vanilla JobDriver_Ingest then dies in its driver-level FailOn lambda:
    ///   Exception in CheckCurrentToilEndOrFail ... at RimWorld.JobDriver_Ingest.&lt;MakeNewToils&gt;b__19_0()
    /// The decompiled 1.6 driver exposes `private Thing IngestibleSource =&gt; job.GetTarget(TargetIndex.A).Thing` with NO
    /// null guard, and that first FailOn dereferences it (`!IngestibleSource.Destroyed`).
    /// This prefix substitutes a live non-ingestible sentinel (unspawned steel) whenever the real source is dead/null.
    /// Vanilla's own first FailOn then evaluates TRUE (`!src.Destroyed &amp;&amp; !src.IngestibleNow`) and fails the job GRACEFULLY —
    /// the pawn re-thinks and vanilla AI finds other food. All other driver code paths are NRE-free against the sentinel
    /// (verified in decompile: chewing FailOn, ChewDurationMultiplier, Notify_Starting, TryMakePreToilReservations →
    /// GetMaxAmountToPickup is null-safe). Healthy targets pass through with zero behavior change.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Ingest), "get_IngestibleSource")]
    public static class IngestibleSourcePatches
    {
        private static Thing sentinel;

        [HarmonyPrefix]
        public static bool IngestibleSource_Prefix(JobDriver_Ingest __instance, ref Thing __result)
        {
            try
            {
                Job job = __instance != null ? __instance.job : null;
                if (job == null) return true; // driver not started yet — vanilla path

                Thing real = job.GetTarget(TargetIndex.A).Thing;
                if (real != null && !real.Destroyed) return true; // healthy target — zero behavior change

                if (sentinel == null)
                    sentinel = ThingMaker.MakeThing(ThingDefOf.Steel); // unspawned, non-ingestible: makes the first FailOn fail the job
                __result = sentinel;
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[AIColonists] IngestibleSource prefix: " + e.Message, 4821); // defensive rule — never break vanilla flow
                return true;
            }
        }
    }
}
