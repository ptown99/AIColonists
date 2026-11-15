using System;
using UnityEngine;
using Verse;

namespace AIColonists.UI
{
    /// <summary>
    /// Renders the colonist's inner_voice as a thought bubble (Spec §6.2). 1.6-verified: vanilla ships
    /// RimWorld.MoteMaker.ThrowText, which spawns a Mote_Text mote (Mote_Special.xml: thingClass=MoteText,
    /// drawGUIOverlay=true, realTime=true, solidTime=2.2) — the text drifts gently upward and fades after
    /// ~3 s of REAL time (so it can never pile up while paused). No custom Mote subclass or MoteDef needed.
    /// GenMapUI.DrawText renders one Tiny-font line with NO wrapping, so bubble text is truncated to
    /// BubbleMaxChars; the full sentence stays in Player.log as the persistent record.
    /// </summary>
    public static class MoteBubbleUtility
    {
        private const int BubbleMaxChars = 48;

        public static void TryThrowThoughtMote(Pawn pawn, string text)
        {
            if (pawn == null || string.IsNullOrEmpty(text)) return;

            try
            {
                // Persistent record: the bubble fades in ~3 s, this line does not.
                Log.Message("[AIColonists] " + pawn.LabelCap + " thinks: " + text);

                if (!pawn.Spawned) return; // carried / off-map — no position to anchor a mote at
                RimWorld.MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 1.5f, 0f), pawn.MapHeld, Truncate(text));
            }
            catch (Exception e)
            {
                Log.Error("[AIColonists] Mote throw failed: " + e.Message);
            }
        }

        private static string Truncate(string s)
        {
            if (s.Length <= BubbleMaxChars) return s;
            return s.Substring(0, BubbleMaxChars - 1).TrimEnd() + "…";
        }
    }
}
