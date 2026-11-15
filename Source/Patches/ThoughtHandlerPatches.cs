using System;
using AIColonists.Core;
using HarmonyLib;
using RimWorld; // 1.6: MemoryThoughtHandler lives in the RimWorld namespace
using Verse;

namespace AIColonists.Patches
{
    /// <summary>
    /// Spec §6.3: detect significant mood/memory events (ate without table, witnessed death, insulted) and flag an
    /// immediate event-driven Layer 1/2 evaluation — the dual-clock cooldown must not delay trauma processing.
    /// Target verified on 1.6 (decompiled): MemoryThoughtHandler.TryGainMemory has TWO overloads in this build
    /// ((ThoughtDef def, Pawn otherPawn = null, Precept sourcePrecept = null) and (Thought_Memory newThought, Pawn otherPawn = null)) — pin the
    /// parameter types so Harmony resolves exactly one. Postfix parameter names MUST match the target's real names
    /// ("newThought"/"otherPawn"): Harmony matches injected arguments by name, not position.
    /// DEFENSIVE BY DESIGN (verified live failure on 1.6): Game.InitNewGame → PawnUtility.GiveAllStartingPlayerPawnsThought
    /// calls TryGainMemory(ThoughtDef) with otherPawn left at its NULL default — before Find.World exists. An exception
    /// thrown here cascades into an unrecoverable map-generation freeze (the game's own ErrorWhileGeneratingMap handler
    /// NREs on the half-built map). This hook is observational only: every state access is null-guarded and the whole
    /// body is wrapped so vanilla flow can never break.
    /// </summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory), new[] { typeof(Thought_Memory), typeof(Pawn) })]
    public static class ThoughtHandlerPatches
    {
        [HarmonyPostfix]
        public static void TryGainMemory_Postfix(MemoryThoughtHandler __instance, Thought_Memory newThought, Pawn otherPawn)
        {
            try
            {
                Settings s = AIColonistMod.Settings;
                if (s == null || !s.EnableAI) return;

                // Key on the OWNING pawn (__instance.pawn — public in 1.6), not otherPawn: for every non-social memory
                // (trauma events — the whole point of this hook per Spec §6.3) otherPawn is legitimately NULL, and for
                // social memories it is the OTHER party. Faction can also be null on freshly-created starting pawns, and
                // Find.World / TickManager are absent during early init (Game.InitNewGame calls this before World exists).
                if (newThought == null || newThought.def == null) return;
                Pawn owner = __instance != null ? __instance.pawn : null;
                if (owner == null || owner.Faction == null || !owner.Faction.IsPlayer) return;

                if (Find.World == null) return; // absent during early init (Game.InitNewGame runs before World exists)

                AIColonistWorldComponent comp = Find.World.GetComponent<AIColonistWorldComponent>();
                if (comp == null) return;

                State.ColonistMindState mind = comp.GetOrCreateMindState(owner);
                int ticks = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                // Live-verified NRE round 2 (2026-11-14): ThoughtDef.Label is a COMPUTED property whose getter derefs stages[0]
                // unguarded — during save load the stages list can hold a NULL first element, and this hook fires exactly then
                // (memory thoughts are re-granted mid-load). Plain fields only: they cannot throw.
                string thoughtLabel = newThought.def.label;
                if (string.IsNullOrEmpty(thoughtLabel)) thoughtLabel = newThought.def.defName;
                mind.memory.AddEpisodic("Something happened: " + thoughtLabel + ".", ticks);

                // TODO(Phase 3): classify the ThoughtDef (trauma / social / mundane) and, for significant ones,
                //   force an out-of-cycle Layer 1/2 evaluation by zeroing the pawn's cooldown in AIColonistGameComponent.
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[AIColonists] TryGainMemory_Postfix: " + e.Message, 4801);
            }
        }
    }
}
