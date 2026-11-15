using AIColonists.Core;
using HarmonyLib;
using RimWorld; // 1.6: Pawn_DraftController lives in the RimWorld namespace
using Verse;
using Verse.AI;

namespace AIColonists.Patches
{
    /// <summary>
    /// Spec §6.3: purge any pending non-combat desires from the IntentBuffer the instant a pawn is drafted —
    /// the Drafting Override Protocol is enforced natively by ThinkTree order, and this patch makes sure a stale
    /// desire can't be picked up on the very next tick after un-drafting.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class DraftControllerPatches
    {
        [HarmonyPostfix]
        public static void Drafted_Postfix(Pawn_DraftController __instance, bool __0)
        {
            if (!__0) return; // un-drafted: nothing to purge

            Pawn pawn = __instance.pawn;
            if (pawn == null || pawn.Map == null) return;

            AIColonistGameComponent manager = pawn.Map.GetComponent<AIColonistGameComponent>();
            if (manager != null)
                manager.PurgeDesire(pawn.ThingID);
        }
    }
}
