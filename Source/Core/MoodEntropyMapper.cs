using Verse;

namespace AIColonists.Core
{
    /// <summary>
    /// Mood → Sampling Entropy mapper (Spec §3.2). Decouples cognitive depth (Intellect) from entropy (Mood):
    /// mood drives temperature inside [0.50, 0.85] + top_p; min_p stays fixed at 0.08 to clip garbage tail tokens.
    /// Small models collapse above ~1.0 temperature — hard-clamped here no matter what the table says.
    /// </summary>
    public static class MoodEntropyMapper
    {
        public const float TempFloor = 0.50f;
        public const float TempCeiling = 0.85f;

        public struct MoodParams
        {
            public float tempOffset;
            public float topP;
        }

        // Spec §3.2 MOOD_ENTROPY_MAP (keyed by mood tier — TODO(Phase 2): derive tier from pawn.needs.mood).
        private static readonly System.Collections.Generic.Dictionary<string, MoodParams> Table =
            new System.Collections.Generic.Dictionary<string, MoodParams>
        {
            { "ecstatic",   new MoodParams { tempOffset = +0.05f, topP = 0.92f } },
            { "happy",      new MoodParams { tempOffset =  0.00f, topP = 0.90f } },
            { "content",    new MoodParams { tempOffset = -0.05f, topP = 0.85f } }, // predictable, steady routine
            { "stressed",   new MoodParams { tempOffset = +0.10f, topP = 0.92f } }, // slightly erratic
            { "furious",    new MoodParams { tempOffset = +0.20f, topP = 0.95f } }, // impulsive, sharp (max 0.85 after clamp)
            { "depressed",  new MoodParams { tempOffset = -0.15f, topP = 0.80f } }, // flat, repetitive, low-energy (min 0.50 after clamp)
        };

        /// <summary>TODO(Phase 2): map pawn.needs.mood.CurLevel (+ mental state) to a tier key.</summary>
        public static string MoodTier(Pawn pawn)
        {
            // Placeholder: treat everything as content until the real mood-tier mapping lands in Phase 2.
            return "content";
        }

        public static float MoodToTemperature(float baseTemp, Pawn pawn = null)
        {
            // Fallback is the "content" baseline (Spec §3.2); assigned up-front because C# definite-assignment
            // requires it when pawn == null skips the lookup entirely.
            MoodParams p = Table["content"];
            if (pawn != null && !Table.TryGetValue(MoodTier(pawn), out p))
                p = Table["content"];

            float t = baseTemp + p.tempOffset;
            return UnityEngine.Mathf.Clamp(t, TempFloor, TempCeiling);
        }

        public static float MoodToTopP(Pawn pawn = null)
        {
            MoodParams p = Table["content"];
            if (pawn != null && !Table.TryGetValue(MoodTier(pawn), out p))
                p = Table["content"];
            return p.topP;
        }
    }
}
