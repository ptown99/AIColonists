#!/usr/bin/env bash
# Spec §8.1 Option B — Unified MoE: one A3B-class MoE model (Q4_K_M, ~20GB) across BOTH P100s for ALL layers.
# Only 3B params active per token -> 35B-class roleplay depth at 3B speed, no model swapping (Spec §3.1).
# GPU layout for THIS node: GPU 1 + GPU 2 = Tesla P100-PCIE-16GB (GPU 0 is the 20W-capped M2000 — excluded).
# NOTE: spec name "Qwen3.6-35B-A3B" is a placeholder — swap in your actual A3B-class MoE GGUF (e.g., Qwen3-30B-A3B).
set -euo pipefail

MODELS_DIR="${MODELS_DIR:-/opt/llama-models}"          # TODO: point at your GGUF directory
LLAMA_BIN="${LLAMA_BIN:-/opt/llama.cpp/build/bin/llama-server}"

# Equal fraction of the model offloaded to each GPU (flag verified: -ts/--tensor-split N0,N1,...).
# NOTE: a standalone comment line inside a backslash-continued command would terminate it — keep comments above.
echo "[AIColonists] Starting unified MoE server (GPU 1+2, port 8091)..."
CUDA_VISIBLE_DEVICES=1,2 "$LLAMA_BIN" \
  -m "$MODELS_DIR/qwen3-30b-a3b-q4_k_m.gguf" \
  --host 0.0.0.0 --port 8091 \
  -c 4096 -np 8 -cb --cache-reuse 256 \
  --tensor-split 1,1 \
  -ctk f16 -ctv f16 \
  -ngl 999
