using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIColonists.Inference
{
    /// <summary>
    /// Async boundary between the mod and local inference (Spec §9 Phase 1).
    /// Implementations must NEVER block the Unity main thread — only ever be awaited from worker threads.
    /// </summary>
    public interface ILlamaBridge
    {
        Task<Layer1Result> RequestLayer1Async(Layer1Request request);

        /// <summary>Batched room social call — one decision per colonist in a shared space (Spec §7.3).</summary>
        Task<Layer2Result> RequestLayer2Async(Layer2Request request);

        /// <summary>Per-colonist memory consolidation + trait drift (Spec §7.4).</summary>
        Task<Layer3Result> RequestLayer3Async(Layer3Request request);
    }

    /// <summary>Everything a Layer 1 call needs, captured on the main thread (Spec §5.1 / §7.1).</summary>
    public class Layer1Request
    {
        public string model;         // e.g., "qwen2.5-1.5b-instruct-q8_0"
        public string systemPrompt;  // static identity prefix (KV-cache friendly, Spec §2.3)
        public string userPrompt;    // dynamic suffix + numbered sensory affordances
        public float temperature;    // mood-mapped, clamped to [0.50, 0.85] (Spec §3.2)
        public float minP = 0.08f;   // guardrail: clips garbage tail tokens even when agitated
        public float topP = 0.9f;    // mood-mapped (Spec §3.2 MOOD_ENTROPY_MAP)
        public int maxTokens = 80;   // Spec §3.1 Layer 1 budget
        public int optionCount = 0;  // number of affordances — sizes the impulse_id enum (Spec §7.2)
    }

    /// <summary>Parsed + validated Layer 1 output (matches GbnfSchemas.Layer1, Spec §7.2).</summary>
    /// FIELD NAMES ARE SNAKE_CASE ON PURPOSE: RimWorld's JsonUtility matches field names exactly and
    /// case-sensitively (no attribute support), and the server emits inner_voice / spoken_mutter / impulse_id.
    [Serializable]
    public class Layer1Result
    {
        public string inner_voice;   // first-person, zero mechanical terms ("Unaware" constraint, Spec §2.2)
        public string spoken_mutter; // optional phrase muttered aloud, may be empty
        public int impulse_id;       // 1..N — must index an AffordanceOption or the desire is discarded
    }

    /// <summary>Everything a Layer 2 call needs (Spec §7.3). One call covers every colonist in a shared space.</summary>
    public class Layer2Request
    {
        public string model;         // e.g., "qwen2.5-3b-instruct-q4_k_m" — ignored by single-model servers
        public string systemPrompt;  // static group-simulation prefix (byte-identical across calls → pure KV reuse)
        public string userPrompt;    // dynamic roster: names, moods, relationship color
        public float temperature = 0.65f;
        public float minP = 0.08f;
        public float topP = 0.9f;
        public int maxTokens = 150;  // Spec §3.1 Layer 2 budget (~120 tokens typical)
    }

    /// <summary>One decision for one colonist (Spec §7.3). FIELD NAMES ARE SNAKE_CASE ON PURPOSE — the server emits them verbatim.</summary>
    [Serializable]
    public class SocialDecision
    {
        public string colonist_name;   // must match a roster name or the decision is discarded at consume time
        public string inner_feeling;   // first-person feeling behind the impulse (episodic-memory fodder)
        public string social_impulse;  // none | warm_chat | vent_frustration | sharp_remark | quiet_nod | avoid_gaze
        public string target_name;     // roster name of the other colonist, empty for "none"
        public string spoken_line;     // what the initiator says aloud (becomes the letter text), may be empty
    }

    /// <summary>Parsed + validated Layer 2 output (matches GbnfSchemas.Layer2JsonSchema, Spec §7.3).</summary>
    [Serializable]
    public class Layer2Result
    {
        public List<SocialDecision> decisions = new List<SocialDecision>();
    }

    /// <summary>Everything a Layer 3 call needs (Spec §7.4).</summary>
    public class Layer3Request
    {
        public string model;         // e.g., "qwen2.5-14b-instruct-q4_k_m" — ignored by single-model servers
        public string systemPrompt;  // static per-colonist subconscious prefix (KV-cached per pawn)
        public string userPrompt;    // dynamic: raw episodic memories + current self-knowledge
        public float temperature = 0.65f;
        public float minP = 0.08f;
        public float topP = 0.9f;
        public int maxTokens = 350;  // Spec §3.1 Layer 3 budget (~250 tokens typical)
    }

    /// <summary>One relationship nudge (Spec §7.4). Server values are clamped to ±0.05 at apply time.</summary>
    [Serializable]
    public class RelationshipDelta
    {
        public string target_name;
        public float trust_delta;
        public float affection_delta;
        public float respect_delta;
        public float frustration_delta;
    }

    /// <summary>Continuous trait drift (Spec §7.4). Clamped to ±0.02 at apply time.</summary>
    [Serializable]
    public class TraitDrift
    {
        public string trait_name = "";
        public float delta;
    }

    /// <summary>Parsed + validated Layer 3 output (matches GbnfSchemas.Layer3JsonSchema, Spec §7.4).</summary>
    [Serializable]
    public class Layer3Result
    {
        public string self_reflection = "";
        public string new_semantic_memory = "";
        public List<RelationshipDelta> relationship_deltas = new List<RelationshipDelta>();
        public TraitDrift trait_drift;   // null when nothing usable was extracted (truncation-tolerant parse)
        public string long_term_longing = "";
    }
}
