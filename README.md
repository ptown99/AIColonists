# AI-Integrated Colonists (RimWorld Mod)

An opt-in LLM layer that makes each RimWorld colonist an autonomous agent. The fundamental gameplay loop is
unchanged — schedules, drafting, and combat work exactly as vanilla — but a local LLM decides *which* tasks each
colonist pursues and *how* they talk to each other. Colonists never know they're in a game: inputs are sensory-framed
prose, outputs are schema-constrained JSON, and every action becomes a pre-validated vanilla job.

Everything runs on your own hardware — nothing leaves your machine. **AI is disabled by default**; enable it in Mod
Settings once your inference endpoint is up.

Built to [`SPEC.md`](SPEC.md) (design spec v2.0). Spec sections are referenced as `Spec §x.y` throughout the code.

## What the mod does — three layers

| Layer | Cadence (1× speed, unpaused) | What happens |
|-------|------------------------------|--------------|
| **1 — Impulses** | ~every 30 s per colonist | The scanner collects 4–6 concrete, reachable options ("Eat the hot simple meal on the dining table", "Work on the steel vein nearby") and the LLM picks one as a numbered impulse with an in-character inner voice. The pick becomes a real vanilla job (work, eat, rest, social). |
| **2 — Room social** | ~every 90 s per occupied room (batched) | All eligible colonists sharing a space are sent in ONE call; the LLM may pair them up for a warm chat, venting session, or sharp remark. Executed through vanilla interaction defs — so they grant real mood thoughts and show in the play log. |
| **3 — Memory drift** | ~every 5 min per colonist (staggered) | Episodic memories are consolidated into self-knowledge, a long-term desire, and slow continuous trait drift (clamped ±0.05/±0.02 per cycle). |

The **AI Mind tab** on each colonist shows the live result: last inner monologue, long-term desire, recent memories,
self-knowledge, trait-drift bars, and relationship vectors toward the people they know.

## Requirements

- **RimWorld 1.6** (Core only) — compiled and verified against install 1.6.4871; the code uses 1.6-specific APIs.
- **Any OpenAI-compatible chat endpoint** (`/v1/chat/completions`): local [Ollama](https://ollama.com) is the easiest
  path (recommended), and `llama-server` / vLLM work too. The client speaks plain HTTP JSON — no SDK.
- A small model is plenty: **qwen2.5:3b-class** is the tested configuration (structured short outputs, ~400–1,200
  tokens per call). Bigger models improve prose quality but aren't required. Context ≥ 4096.

## Install (prebuilt)

The repo ships the built mod — `Assemblies/AIColonists.dll` + bundled Harmony are already in place:

1. Copy/clone this folder into `<RimWorld install>/Mods/AIColonists`.
2. Launch RimWorld → **Mod Configurator** → enable *AI-Integrated Colonists* (loads after Core; no other mods needed —
   0Harmony 2.3.3 ships inside the mod).
3. Start your LLM endpoint (Ollama quickstart below, or point at any server you already run).
4. In-game: main menu → **Mods** → *AI-Integrated Colonists* settings → set the three URLs + model names → flip
   **Enable AI (master switch)** on.

## Ollama quickstart (recommended local backend)

```powershell
# 1. install Ollama from https://ollama.com/download
# 2. pull a small instruct model (~2.2 GB):
ollama pull qwen2.5:3b
```

In Mod Settings, set **all three layer URLs** to `http://127.0.0.1:11434` and all three model names to
`qwen2.5:3b`. That's it — Ollama exposes the OpenAI-compatible endpoint the client expects, so no code changes are
needed for any other OpenAI-compatible server either (just change URL/model).

**No keep-alive environment variable is needed.** Ollama's default 5-minute keep-alive is exactly right: every Layer 1
call (≤30 s apart while unpaused) resets the timer, so the model stays resident during play and auto-unloads ~5 min
after you pause or quit. The first dispatch of a session triggers a one-time ~8 s cold load — the mod absorbs it with a
one-shot warmup request at game start (TEST-RESULTS.md §22.7), which is why Player.log shows
`[AIColonists] inference backend warm — model loaded, dispatches run at full speed` shortly after launch. Do NOT set
`OLLAMA_KEEP_ALIVE=-1`: that would pin the model in VRAM forever instead of only during gameplay.

## Remote `llama-server` backend (optional)

If you'd rather run llama.cpp on a dedicated GPU box, [`server/README.md`](server/README.md) has launch scripts and
verified flags for single/dual P100 topologies (ports 8091/8092). The mod's settings accept any URL per layer — you
can even split layers across different servers/models.

## Mod Settings reference

| Field | Meaning |
|-------|---------|
| **Enable AI** (master switch) | Off = the mod is completely inert (one null check per tick, zero requests). |
| **AI-first job priority** | On (default): while an LLM decision is in flight the colonist holds position with a short wait job instead of falling back to vanilla work first — so what Layer 1 decides is what the pawn actually does. Off = blend mode (vanilla fills the gaps between decisions). Player orders, drafting and emergency survival always win either way; an exhausted colonist is never held. |
| Layer 1/2/3 server URL | One endpoint per layer — point all three at one server, or split them across servers/models. |
| Request timeout (ms) | Per-request HTTP timeout; a failed request falls back to vanilla AI silently (one retry on transport failure). |
| Layer 1/2/3 model | Model name per layer, as loaded by your server. Must match what the server has running. |
| Dual-Clock real-time ceilings (s) | Per-layer minimum real seconds between calls — caps GPU load at 3×/4× game speed. The game-tick half of each gate is fixed in code (L1 1800 ticks, L2 5400, L3 18000). |
| VRAM preset | One-click model names + timeout for CPU / single-P100 / dual-P100 hardware. URLs stay yours. |

Settings are written when the settings window closes; changes apply to the next dispatch — no game restart needed.

## What you'll see in-game (and how to verify it's working)

All LLM activity is distinguishable in `Player.log` by the `[AIColonists]` prefix:

```
[AIColonists] Cook (Human1006) Layer 1: impulse_id=3 of 6, inner_voice="Might as well eat something before I forget."
[AIColonists] Cook thinks: Might as well eat something before I forget.
[AIColonists] Aiglamene Layer 2: warm_chat -> Hercules "How about you?" [executed]
[AIColonists] Hercules Layer 3: consolidation applied - "I am becoming more patient..."
```

- **Layer 1** lines appear ~every 30 s per colonist while unpaused; the pawn then walks off and does the job. While a
  decision is in flight (with AI-first priority on) the pawn may stand still for up to ~7.5 s — that's the hold job,
  not a bug: it keeps vanilla work from pre-empting the LLM's choice. If the server fails twice in a row the mod stops
  holding and lets vanilla take over until a dispatch succeeds again. Each AI-issued job also logs
  `[job started] "<the impulse phrasing>" (<JobDef defName>)` and each hold logs `[hold]` — those tags let you verify decision→execution
  correlation from Player.log alone (no eyeballing required).
- **Layer 2** lines carry an outcome tag: `[executed]`, `[dropped: <specific vanilla reason>]`, or
  `[attempt failed at the last moment]`. Executed interactions also grant mood thoughts (Mood tab) and play-log entries.
- **Layer 3** "consolidation applied" lines appear ~every 5 min per colonist; check the AI Mind tab for the result.

Token budget (measured from Ollama server logs, 1× speed, unpaused): roughly **60k tokens per colonist per in-game
day** (~75k per colonist-hour), dominated by Layer 1. Prefix caching keeps steady-state inference around ~0.5 s/call
on a mid-range GPU. Paused time costs exactly zero — the dual-clock gates freeze on pause.

## Design guarantees (safety)

- **Player control is absolute.** The AI branch sits *below* Drafted / QueuedJob / emergency nodes in the think tree,
  so player orders and survival always win natively; drafting purges pending desires instantly. Only player-faction,
  non-prisoner colonists are LLM-eligible at all (prisoners/visitors never get evaluated).
- **No hallucinated targets.** The LLM only picks numbered affordances that C# verified as reachable at scan time;
  every desire is re-validated on the main thread before it becomes a job, and stale/unreachable ones are discarded
  silently with vanilla fallback.
- **Vanilla flow can never break.** Every external call (HTTP, label access, job resolution) is guarded; failures log
  once (`ErrorOnce`) and degrade to vanilla AI. With no server reachable the mod is completely inert.
- **Save-safe.** All AI state lives in a `WorldComponent`/`GameComponent` pair indexed by `ThingID`; `Pawn.ExposeData`
  is never patched, so saves load cleanly with or without the mod.
- **Zero main-thread stalls.** Inference runs on background threads; the main thread only pops pre-validated decisions
  in <0.01 ms (Spec §1 fix).

## Building from source (optional)

Prerequisites: .NET SDK 5+ (the project targets `net472`), and a RimWorld 1.6 install to reference.

```powershell
dotnet build "Source\AIColonists.csproj" -c Release -p:RimWorldDir="C:\path\to\RimWorld"
# output lands in Assemblies/AIColonists.dll — copy the folder into your Mods/ directory
```

No-game test harness (compiles every real source file against 1.6-fidelity stubs; no RimWorld install needed):

```powershell
dotnet run --project Tests     # → 305 checks, all must pass
```

## Project structure

```
AIColonists/
├── About/About.xml                        # mod metadata (self-contained — Harmony bundled)
├── Assemblies/                            # build output — RimWorld loads AIColonists.dll + 0Harmony.dll from here
├── Defs/
│   ├── InteractionDefs/AIColonist_Interactions.xml   # LLM-driven social interactions, baseChance=0 (Spec §7.3)
│   └── MemoryThoughts/AIColonist_Memories.xml        # mood thoughts granted by AI interactions
├── Languages/English/Keyed/               # UI strings
├── Patches/
│   ├── AIColonist_ThinkTreePatch.xml      # injects the desire branch into MainColonistBehaviorCore (Spec §6.1)
│   └── AIColonistTabs.xml                 # AI Mind tab registration
├── 0Harmony/0Harmony.dll                  # Lib.Harmony 2.3.3 (copied to Assemblies/ at build time)
├── Source/                                # mod source (net472, references the game DLLs)
│   ├── AIColonists.csproj
│   ├── Core/                              # entry point, settings UI, dual-clock scheduler, intent buffer, area batcher
│   ├── Inference/                         # ILlamaBridge, LlamaServerClient (OpenAI-compatible HTTP), GBNF schemas
│   ├── ThinkTree/                         # gate node, non-blocking JobGiver, AffordanceScanner
│   ├── Prompts/                           # prompt builders for all three layers, sensory framing (Appendix A)
│   ├── State/                             # ColonistMindState, RelationshipVector, MemoryStore (IExposable)
│   ├── Patches/                           # minimal Harmony postfixes (draft purge, memory grant, ingest guard, ...)
│   ├── Social/                            # InteractionWorker_AISocial
│   └── UI/                                # ITab_Pawn_AIMind, MoteBubbleUtility
├── Tests/                                 # no-game harness: real sources compiled against 1.6-fidelity stubs (307 checks)
├── Tools/PromptProbe/                     # Ollama E2E probe: sends the mod's exact Layer 1 payload to a live endpoint (TEST-RESULTS.md §22.6)
├── server/                                # optional: llama-server launch scripts for a GPU box + README
└── docs                                   # SPEC.md · TEST-RESULTS.md · IN-GAME-TEST-CHECKLIST.md · OPTIONAL-IMPROVEMENTS.md
```

## Documentation

| File | What it is |
|------|-----------|
| [`SPEC.md`](SPEC.md) | Design spec v2.0 — the source of truth for architecture, wire format, and phases. |
| [`TEST-RESULTS.md`](TEST-RESULTS.md) | Full verification record (§1–§22): decompile findings, E2E protocol tests, live-session results, incident reports. |
| [`IN-GAME-TEST-CHECKLIST.md`](IN-GAME-TEST-CHECKLIST.md) | In-game test protocol (H1–H5) + deployed-build hash history. |
| [`OPTIONAL-IMPROVEMENTS.md`](OPTIONAL-IMPROVEMENTS.md) | Backlog — parked ideas with effort estimates and status. |

## Implementation status (Spec §9 phases)

| Phase | Scope | Status |
|-------|-------|--------|
| 1 | Async plumbing, ThinkTree foundation, AffordanceScanner, work-job resolution | **Complete + live-verified** |
| 2 | Differentiation, sensory filter, prefix caching | **Complete** (2026-11-15) — real per-pawn identity prefix (KV-cache byte-stable), Intellect→cadence/horizon via the `Intellectual` skill, Mood→entropy wired at dispatch, trait-weight seeding + drift-fed values/fears; plus AI-first job priority (backlog #8). Build + 305/305 harness; live re-test of work impulses is T4 on the checklist |
| 3 | Layer 2 social engine, Layer 3 memory drift | **Complete + live-verified** (executed warm chats grant mood thoughts; consolidations apply with clamped deltas) |
| 4 | UI inspector, settings polish, stress test | **Live** — AI Mind tab + full settings surface in use; final H4 `[executed]` observation pending on the test checklist |

Known open items are tracked in [`OPTIONAL-IMPROVEMENTS.md`](OPTIONAL-IMPROVEMENTS.md) (play-log chronicle,
multi-turn dialogue, emotional memory writers; AI-first job priority shipped with Phase 2 on 2026-11-15).
