#!/usr/bin/env bash
# Spec §8.1 Option A — Dedicated Dual-Instance on dual Tesla P100 (sm_60, native FP16).
# GPU layout for THIS node (verified via nvidia-smi):
#   GPU 0 = Quadro M2000 4GB — permanently power-limited to 20W (x4 PCIe slot); NOT used for llama.
#   GPU 1 -> Layer 1+2: Qwen2.5-3B-Instruct          (port 8091)
#   GPU 2 -> Layer 3:   Qwen2.5-14B-Instruct Q4_K_M  (port 8092)
# Ports avoid 8080/8081 — Open WebUI occupies 8080 on this node.
set -euo pipefail

MODELS_DIR="${MODELS_DIR:-/opt/llama-models}"          # TODO: point at your GGUF directory
LLAMA_BIN="${LLAMA_BIN:-/opt/llama.cpp/build/bin/llama-server}"

# Flag notes (verified against llama.cpp 0.7.0-dev, commit d327bf1 — see TEST-RESULTS.md):
#   -cb              cont-batching is a BOOLEAN flag, takes no value. Spec §8.1's "-cb 2048" was a misread.
#   --cache-reuse N  MIN chunk size to attempt reusing from the KV cache via KV shifting (E2E-verified:
#                    a 270-token shared prefix was reused with this set to 256).
#   -c               explicit context; default 0 = model's trained size. Set explicitly for determinism.

echo "[AIColonists] Starting Layer 1+2 server (GPU 1, port 8091)..."
CUDA_VISIBLE_DEVICES=1 "$LLAMA_BIN" \
  -m "$MODELS_DIR/qwen2.5-3b-instruct-q4_k_m.gguf" \
  --host 0.0.0.0 --port 8091 \
  -c 4096 -np 8 -cb --cache-reuse 256 \
  -ctk f16 -ctv f16 \
  -ngl 999 &

echo "[AIColonists] Starting Layer 3 server (GPU 2, port 8092)..."
CUDA_VISIBLE_DEVICES=2 "$LLAMA_BIN" \
  -m "$MODELS_DIR/qwen2.5-14b-instruct-q4_k_m.gguf" \
  --host 0.0.0.0 --port 8092 \
  -c 4096 -np 4 -cb --cache-reuse 256 \
  -ctk f16 -ctv f16 \
  -ngl 999 &

wait
