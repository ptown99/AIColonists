# llama-server deployment (Spec §8.1) — OPTIONAL

**You probably don't need this folder.** If you're running the LLM locally on the same machine as RimWorld,
[Ollama](https://ollama.com) is the easiest path (`ollama pull qwen2.5:3b`, point the mod at `http://127.0.0.1:11434` —
see the main README). This folder is only for running `llama.cpp llama-server` on a dedicated GPU box.

The scripts below target the **Dual Tesla P100** node this project was developed on. GPU layout on that node
(verified via `nvidia-smi`):

| Index | GPU | Status |
|-------|-----|--------|
| 0     | Quadro M2000 4GB | Permanently power-limited to **20W** (x4 PCIe slot) — do not use for llama |
| 1     | Tesla P100-PCIE-16GB (sm_60, native FP16) | Available when the resident workload is stopped |
| 2     | Tesla P100-PCIE-16GB (sm_60, native FP16) | Available when the resident workload is stopped |

> ⚠️ The P100s currently run a 27B tutor model (~13 GB VRAM each). Stop that server before
> launching these scripts, or nothing will fit.

| Script | Topology | What runs | Ports |
|--------|----------|-----------|-------|
| `start-dual-p100.sh` | Option A — Dedicated Dual-Instance | GPU 1: Qwen2.5-3B (Layers 1+2) · GPU 2: Qwen2.5-14B Q4_K_M (Layer 3) | 8091 / 8092 |
| `start-unified-moe.sh` | Option B — Unified MoE | One ~35B-A3B-class MoE Q4_K_M (~20GB) across GPUs 1+2 for ALL layers, `-np 8` parallel slots | 8091 |

Ports are **8091/8092** on purpose: Open WebUI occupies 8080 on this node (and 8081 is reserved).
The mod's Mod-Settings defaults already point at `http://<node>:8091` / `:8092`.

## Flags (verified against llama.cpp 0.7.0-dev, commit d327bf1 — see TEST-RESULTS.md)

- `-cb` — continuous batching is a **boolean** flag and takes no value. Spec §8.1's `-cb 2048` was a misread.
- `--cache-reuse N` — **minimum chunk size** to attempt reusing from the KV cache via KV shifting (not a max).
  E2E-verified: with `--cache-reuse 256`, a 270-token shared prefix was reused on the second call.
- `-c N` — context size; default `0` = model's trained size. Scripts set it explicitly (`4096`) for determinism.
- `-np N` / `--parallel N` — number of server slots (default `-1` = auto). Exists in this build.
- `-ctk f16 -ctv f16` — FP16 K/V cache (P100 sm_60 has native FP16; no TensorRT needed).
- `-ts N0,N1,...` / `--tensor-split` — fraction of the model offloaded to each GPU (Option B uses `1,1`).

## Pointing RimWorld at it

In Mod Settings, set the layer URLs to the node:

- Layer 1 / Layer 2 → `http://<node>:8091`
- Layer 3 → `http://<node>:8092` (Option A only; Option B uses 8091 for all three)

## Notes

- Swap the GGUF paths in each script (`MODELS_DIR`) for your actual model files.
- The spec's "Qwen3.6-35B-A3B" name is a placeholder — use whatever A3B-class MoE GGUF you actually have (e.g., Qwen3-30B-A3B).
- Single-P100 preset (Spec §8.1): one server, Qwen2.5-3B Q8_0 (Layers 1/2) + Qwen2.5-7B Q4_K_M (Layer 3), `~10.5 GB` VRAM — copy Option A and drop to single-GPU flags (`CUDA_VISIBLE_DEVICES=1`).
- Mac M4 Max preset: same scripts under a Metal build of llama.cpp, add `-fa`, no CUDA_VISIBLE_DEVICES.
