using Verse;

namespace AIColonists.State
{
    /// <summary>
    /// 6-axis directional opinion state (Spec §4.2, differentiation layer 6). Relationships are asymmetric:
    /// Alice may depend on Bob while Bob resents Alice — each pawn stores its own vector per target.
    /// </summary>
    public class RelationshipVector : IExposable
    {
        public string targetPawnId;
        public float trust = 0.5f;        // 0-1: "I believe they mean what they say"
        public float affection = 0.5f;    // 0-1: "I enjoy their company"
        public float respect = 0.5f;      // 0-1: "I value their competence"
        public float jealousy = 0.1f;     // 0-1: "They have comfort/status I lack"
        public float frustration = 0.1f;  // 0-1: "Their habits grate on me"
        public float dependency = 0.2f;   // 0-1: "I rely on them to survive/stay sane"
        public int lastInteractionTick;
        public string lastInteractionSummary;

        /// <summary>Clamped delta application — Layer 3 drift is bounded (Spec §7.4: ±0.05 per axis per cycle).</summary>
        public void ApplyDeltas(float trustDelta, float affectionDelta, float respectDelta, float frustrationDelta)
        {
            trust = UnityEngine.Mathf.Clamp(trust + trustDelta, 0f, 1f);
            affection = UnityEngine.Mathf.Clamp(affection + affectionDelta, 0f, 1f);
            respect = UnityEngine.Mathf.Clamp(respect + respectDelta, 0f, 1f);
            frustration = UnityEngine.Mathf.Clamp(frustration + frustrationDelta, 0f, 1f);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref targetPawnId, "targetPawnId");
            Scribe_Values.Look(ref trust, "trust", 0.5f);
            Scribe_Values.Look(ref affection, "affection", 0.5f);
            Scribe_Values.Look(ref respect, "respect", 0.5f);
            Scribe_Values.Look(ref jealousy, "jealousy", 0.1f);
            Scribe_Values.Look(ref frustration, "frustration", 0.1f);
            Scribe_Values.Look(ref dependency, "dependency", 0.2f);
            Scribe_Values.Look(ref lastInteractionTick, "lastInteractionTick", 0);
            Scribe_Values.Look(ref lastInteractionSummary, "lastInteractionSummary", "");
        }
    }
}
