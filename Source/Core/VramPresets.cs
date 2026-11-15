using System.Collections.Generic;

namespace AIColonists.Core
{
    /// <summary>
    /// Phase 4 (Spec §9): VRAM presets for the settings UI. Values come from the spec's hardware layout table
    /// (Appendix, lines 589–590) and the §10 risk-table mitigation. Presets set MODEL NAMES + TIMEOUT ONLY — server
    /// URLs stay user-owned (a dual-instance layout needs two servers; a CPU homelab one). Pure data + Apply() so
    /// the no-game harness can pin exactly what each preset writes.
    /// </summary>
    public static class VramPresets
    {
        public class Preset
        {
            public string name = "";
            public string layer1Model = "";
            public string layer2Model = "";
            public string layer3Model = "";
            public int requestTimeoutMs;
        }

        // Display order in the settings UI (button row, left → right).
        public static readonly List<Preset> All = new List<Preset>
        {
            // CPU inference (homelab node, -ngl 0): one small model for all three layers, generous timeout —
            // a Layer 1 call takes ~5 s on CPU. This is the live-verified Phase 1–3 configuration.
            new Preset
            {
                name = "CPU homelab",
                layer1Model = "qwen2.5-1.5b-instruct-q4_k_m",
                layer2Model = "qwen2.5-1.5b-instruct-q4_k_m",
                layer3Model = "qwen2.5-1.5b-instruct-q4_k_m",
                requestTimeoutMs = 30000,
            },

            // Single Tesla P100 (16 GB), Spec §10: Qwen2.5-3B Q8_0 (~3.5 GB) serves Layers 1+2, Qwen2.5-7B Q4_K_M
            // (~4.8 GB) serves Layer 3 — ~10.5 GB total VRAM with FP16 KV cache, ~160–240 ms per room batch.
            new Preset
            {
                name = "1x P100 16GB",
                layer1Model = "qwen2.5-3b-instruct-q8_0",
                layer2Model = "qwen2.5-3b-instruct-q8_0",
                layer3Model = "qwen2.5-7b-instruct-q4_k_m",
                requestTimeoutMs = 15000,
            },

            // Dual Tesla P100 (2x 16 GB), Spec hardware table Option A (dedicated dual-instance): GPU 0 runs
            // Layers 1+2 (Qwen2.5-3B Q8_0), GPU 1 runs Layer 3 (Qwen2.5-14B Q4_K_M) — ~22/32 GB, two servers
            // (point the layer URLs at each). Option B (unified Qwen3.6-35B-A3B MoE across both GPUs) is a
            // manual choice — set all three model fields to it by hand.
            new Preset
            {
                name = "2x P100 32GB",
                layer1Model = "qwen2.5-3b-instruct-q8_0",
                layer2Model = "qwen2.5-3b-instruct-q8_0",
                layer3Model = "qwen2.5-14b-instruct-q4_k_m",
                requestTimeoutMs = 15000,
            },
        };

        /// <summary>Applies a preset's model names + timeout to the settings. Null-safe (harness-pinned).</summary>
        public static void Apply(Preset p, Settings s)
        {
            if (p == null || s == null) return;
            s.Layer1Model = p.layer1Model;
            s.Layer2Model = p.layer2Model;
            s.Layer3Model = p.layer3Model;
            s.RequestTimeoutMs = p.requestTimeoutMs;
        }
    }
}
