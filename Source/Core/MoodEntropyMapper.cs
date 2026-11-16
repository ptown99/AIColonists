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

        // Spec §3.2 MOOD_ENTROPY_MAP, keyed by mood tier.
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

        /// <summary>
        /// Phase 2 (Spec §3.2): map a mood percentage (0..1, from pawn.needs.mood.CurLevelPercentage) to a tier key.
        /// Bands mirror the vanilla mood curve: comfortable above ~0.6, strained mid-range, and the two low-mood poles —
        /// flat/depressed just below break threshold, impulsive/furious near the horrible-break zone where vanilla
        /// colonists actually go berserk (so "furious" = erratic sampling there is behaviorally faithful).
        /// Values outside 0..1 (unknown mood) fall back to the neutral "content" baseline.
        /// </summary>
        public static string MoodTier(float moodPct)
        {
            if (moodPct < 0f || moodPct > 1f) return "content";
            if (moodPct >= 0.75f) return "ecstatic";
            if (moodPct >= 0.60f) return "happy";
            if (moodPct >= 0.45f) return "content";
            if (moodPct >= 0.30f) return "stressed";
            if (moodPct >= 0.15f) return "depressed";
            return "furious";
        }

        /// <summary>Main-thread convenience: tier from the pawn's mood need (null-safe — some pawns have no mood need).</summary>
        public static string MoodTier(Pawn pawn)
        {
            if (pawn == null || pawn.needs == null || pawn.needs.mood == null) return "content";
            try { return MoodTier(pawn.needs.mood.CurLevelPercentage); }
            catch { return "content"; } // defensive rule — a throwing mood read must never break the caller
        }

        private static MoodParams ParamsFor(string tier)
        {
            MoodParams p;
            if (!Table.TryGetValue(tier, out p))
                p = Table["content"]; // defensive — every tier key the mappers emit is in the table
            return p;
        }

        /// <summary>Background-thread path (Phase 2): the dispatch worker only sees the snapshot, so it passes the
        /// mood percentage captured on the main thread. Clamped to [TempFloor, TempCeiling] per Spec §3.2.</summary>
        public static float MoodToTemperature(float baseTemp, float moodPct)
        {
            float t = baseTemp + ParamsFor(MoodTier(moodPct)).tempOffset;
            return UnityEngine.Mathf.Clamp(t, TempFloor, TempCeiling);
        }

        /// <summary>Legacy main-thread overload (kept for the harness and any future pawn-side callers). A null pawn is an
        /// UNKNOWN mood — the content baseline applies, exactly as pre-Phase 2 (the "content" tier offset, then clamp).</summary>
        public static float MoodToTemperature(float baseTemp, Pawn pawn = null)
        {
            string tier = pawn == null ? "content" : MoodTier(pawn);
            return UnityEngine.Mathf.Clamp(baseTemp + ParamsFor(tier).tempOffset, TempFloor, TempCeiling);
        }

        /// <summary>Background-thread path: mood-mapped top_p (Spec §3.2 MOOD_ENTROPY_MAP).</summary>
        public static float MoodToTopP(float moodPct) => ParamsFor(MoodTier(moodPct)).topP;

        /// <summary>Legacy main-thread overload (kept for the harness and any future pawn-side callers).</summary>
        public static float MoodToTopP(Pawn pawn = null) => ParamsFor(pawn == null ? "content" : MoodTier(pawn)).topP;
    }
}
