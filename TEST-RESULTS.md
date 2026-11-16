# TEST-RESULTS — AI-Integrated Colonists

**Date:** 2026-11-12 · **Scope:** everything verifiable without a live game (§1–7) + verification against the real 1.6 install (§8) + **live in-game session** (§9–10).
**Environments:** local Windows (.NET SDK 5.0.403, Git Bash) + a private LAN GPU node (IP omitted)
(llama.cpp `0.7.0-dev` commit `d327bf1`, Qwen2.5-1.5B-Instruct-Q4_K_M on CPU, port 8093).

---

## 1. XML well-formedness — ✅ 3/3 PASS

Checked with `xmlcheck.ps1` (PowerShell `XmlDocument.Load`):

| File | Result |
|------|--------|
| `About/About.xml` | PASS (end-tag bug found & fixed during scaffolding) |
| `Defs/ThinkTreeDefs/AIColonist_ThinkTreePatch.xml` | PASS |
| `Defs/InteractionDefs/AIColonist_Interactions.xml` | PASS |

## 2. Stub-harness unit tests — ✅ 114/114 PASS (stubs rewritten for RimWorld 1.6 fidelity)

Real `Source/**/*.cs` compiled against Verse/RimWorld/Unity/Harmony stubs (`Tests/GameStubs.cs`,
members tagged `[CONFIRMED]` / `[GUESS]`) with Newtonsoft.Json 13.0.3 mirroring RimWorld's
JsonUtility field-name serialization. Run: `dotnet run --project Tests` (from the repo root).

On 2026-11-12 the stubs were rewritten to match the **real 1.6 install** (see §8): `LocalTargetInfo`
as a Verse struct with only `(Thing)`/`(IntVec3)` ctors, `InteractionWorker`'s exact two virtuals,
`JobMaker.MakeJob` overload set, `ReachabilityUtility.CanReach` extension shape, namespace corrections
(`ThoughtDef`/`Thought_Memory` → RimWorld; `RulePackDef`/`LetterDef`/`LookTargets` → Verse). New checks:
Harmony `PatchAll()` invoked during mod construction, and the singleton AI Mind tab's visibility gating.

Test groups (24): GBNF schema generation (n=1..6, enum + required + additionalProperties),
snake_case wire-field names, `<think>`-block stripping (incl. malformed/absent), `ParseLayer1`
envelope handling, mood→temperature/top_p clamp bounds [0.50, 0.85], intent-buffer semantics
(8×5000 concurrent producers/consumers, no loss, per-pawn FIFO), memory store caps + 30-day strict-`>` prune boundary,
relationship vector clamps ±0.05/±0.02, mod entry-point wiring (real ctor with stub `ModContentPack`, AI off by default),
mind-state identity & save-safe indexing, affordance scan (food/rest/work-null-jobDef/social near-far/cap/ids/fallback),
queued-desire main-thread re-validation (destroyed/unreachable/null-jobDef rejection), sensory framing thresholds,
prefix-first prompt assembly (no unreplaced placeholders), dual-clock gate incl. pause-reset of the real-time clock,
async dispatch resilience vs closed port (in-flight flag clears, no desire deposited), JobGiver + gate node
(drafted/mental-state exclusion), draft patch (purge on draft only), interaction patch (`__result`-gated memory capture),
memory-thought patch (player pawns only), tab patch (faction-gated), area batcher (12-tile grid / Room.ID grouping, exclusions),
UI smoke (tab label, `OnBuildGUI` no-exception).

A second pass on 2026-11-12 rewired the LLM-client group around the hand-built JSON builder + manual parser (§9.3):
+5 tests — messages regression guard (the exact fields Unity's JsonUtility dropped in-game), string-escape round-trip,
object-form `json_schema` assertion, impulse_id-first property-order check, truncation-tolerant parse case → **112/112**.

A third pass (2026-11-12) replaced the placeholder UI-smoke check with real thought-bubble behavior (−1 +3): spawn via
`RimWorld.MoteMaker.ThrowText` (the 1.6 API — §10.6), 48-char truncation for non-wrapping `GenMapUI.DrawText`, no mote
for unspawned pawns → **114/114**.

> Harness note: one wall-clock-dependent test (dual-clock real-time gate) is timing-sensitive under host load — passes on
> re-run. Not a product bug.

### Bugs the harness caught and fixed (all real, several would have bitten in-game)

| # | Bug | Fix |
|---|-----|-----|
| 1 | `MoodParams p;` unassigned when `pawn == null` (CS0170 — definite-assignment hole that also breaks at runtime) | Initialize with the `"content"` baseline before lookup (`MoodEntropyMapper`) |
| 2 | Food/rest affordances returned with default `optionId = 0`, breaking the sequential 1..N `impulse_id` contract whenever a physiological need was present | Assign `optionId = list.Count + 1` in `AddIfValid` (`AffordanceScanner`) |
| 3 | `LlamaServerClient` set `SharedHttp.Timeout` in its ctor — once any request had been sent, .NET throws on the *second* client construction ("already started one or more requests") | Per-request timeout via `CancellationTokenSource`; shared client never mutated (`LlamaServerClient`) |
| 4 | `StripThinkBlocks` regex typo `<\!think` (never matched) | `@"<think\b[\s\S]*?</think>"` |
| 5 | Extension method declared inside a struct (CS1106) | Moved to static class `AffordanceOptionExtensions` |
| 6 | `start-unified-moe.sh`: comment line inside a backslash-continued command terminated the logical line → `-ctk …` would run as its own command | Comment moved above the command block; verified by dry-run (see §5) |

## 3. llama-server flag verification — ✅ against the real binary (`--help`, commit d327bf1)

| Flag | Verdict |
|------|---------|
| `-cb` / `--cont-batching` | **Boolean** flag, takes no value (spec §8.1's `-cb 2048` was a misread) |
| `--cache-reuse N` | Exists — "**min chunk size** to attempt reusing from the cache via KV shifting" (not a max) |
| `-np` / `--parallel N` | Exists, default `-1` = auto |
| `-c` / `--ctx-size N` | Exists, default `0` = model's trained size → scripts set it explicitly (`4096`) |
| `-ts` / `--tensor-split N0,N1,…` | Exists (fraction per GPU; Option B uses `1,1`) |
| `--prompt-cache*` | **Not present** in this build — nothing to enable for KV prefix reuse |

## 4. E2E API validation — ✅ live llama-server, CPU, port 8093

Model: `qwen2.5-1.5b-instruct-q4_k_m.gguf` (1.09 GB), `-ngl 0 -c 4096 --cache-reuse 256`.
Payloads replicated byte-for-byte from the C# wire types (snake_case, `json_schema` as a **string**).

> **Superseded for the Nov 9 llama.cpp build (§9.3):** on that binary the *string* form of `json_schema` is **silently
> ignored** — HTTP 200 with unconstrained output (`"thought"`/`"impulse"` field names, out-of-enum values). Only the object
> form `{"type":"json_schema","json_schema":{"name":...,"schema":{...}}}` constrains generation. The A/B below was validated
> against an older build where the string form worked.

| Check | Result |
|-------|--------|
| `response_format: {type:"json_schema", json_schema:"<schema>"}` (object + string schema) | **HTTP 200**, output constrained to the schema — shape confirmed for this build |
| Repeated calls (4×, production schema with descriptions) | 4/4 returned non-empty `inner_voice` + in-enum `impulse_id` ∈ {1,2}; **`spoken_mutter` omitted 4/4** |
| `"required"` enforcement | **NOT enforced** — the json_schema→GBNF conversion enforces property types, the `impulse_id` enum, and `additionalProperties:false`, but omits required fields (a description-less schema even returned `{}`). Client-side guard: missing `spoken_mutter`→normalized to `""`; missing `impulse_id`→0→rejected by `IsValidImpulse`→vanilla AI fallback. No crash path |
| KV prefix reuse (`--cache-reuse 256`, prefix-first ordering, Spec §2.3) | Call 1: 302 prompt tokens, 0 cached. Call 2 (same identity prefix, new suffix): **270 cached / 33 fresh** → ~89% less prompt processing |
| `min_p` / `top_p` / `temperature` in payload | Accepted without error (`min_p` is a llama-server extension; unknown keys ignored) |
| CPU throughput (reference only) | ~26 tok/s generation, ~315 tok/s prompt — P100 sm_60 with FP16 KV will be far faster |

## 5. Shell scripts — ✅ syntax + dry-run

- `bash -n` clean on both `server/*.sh`.
- Dry-run with a stub `llama-server` that echoes its args: **every invocation receives the full argument list**
  (dual: GPU=1→3B@8091, GPU=2→14B@8092; MoE: GPU=1,2→8091 with `--tensor-split 1,1` in one command). Exit 0.

## 6. Deployment notes (this node)

- **GPU map:** 0 = M2000 4GB — permanently capped at **20W** per owner instruction (x4 slot); not used for llama.
  1 & 2 = P100-16GB, currently hosting a 27B tutor model (~13 GB each) — stop it before launching the mod servers.
- **Port map:** 8080 = Open WebUI (do not touch), 8091/8092 = mod servers (mod defaults match), 8093 = E2E test server only.

## 7. Remaining — requires a live game session

Everything checkable against the installed game (**1.6.4871 rev590**) has been verified — see §8 and
[`IN-GAME-TEST-CHECKLIST.md`](IN-GAME-TEST-CHECKLIST.md). The live session ran on 2026-11-12 — results below (§9):

- [x] Harmony patches actually weaving at load — ✅ live 2026-11-12 (§9.1): load line present, zero exceptions, zero "Patch operation … failed" lines for either patch XML (full-log sweep: 84 lines, clean)
- [x] ThinkTree patch applying cleanly — ✅ live: the patched branch demonstrably executes (first AI-driven jobs issued through it, §9.2)
- [x] AI Mind tab appearing on human pawn cards — ✅ live: present on every colonist card; `Ai mind data not yet available (Phase 4)` placeholder as designed (§9.1)
- [x] Live LLM round-trip — ✅ live: continuous `Layer 1:` diagnostics (9 decisions logged) + **two executed desires** (`thinks:` lines, §9.2); server log clean, all completions `truncated = 0`
- [ ] Custom `InteractionDef` required fields (Phase 3 — defs ship with baseChance=0, so load errors would only surface then)
- [x] Thought-bubble mechanism — resolved for Phase 1 by design: `MoteBubbleUtility` logs the inner voice to Player.log (that is the `thinks:` line in §9.2); a real mote is a later phase

## 8. Verification against the real RimWorld 1.6 install (2026-11-12)

Target: `<RimWorld install dir>` — **1.6.4871 rev590**, Unity 2022.3.35f1, single
`RimWorldWin64_Data\Managed\Assembly-CSharp.dll`. Method: reflection probes (net5.0 `Assembly.LoadFrom`) +
ilspycmd decompiles + direct inspection of Core XML. Every stub in `Tests/GameStubs.cs` is tagged `[CONFIRMED]`
when it matches a probe result; pre-1.6 assumptions below were **all wrong** and have been fixed in source + stubs.

### 8.1 Compile status

- `dotnet build Source\AIColonists.csproj -c Release` → **0 warnings, 0 errors** against the real
  `Assembly-CSharp.dll`, Unity CoreModule/IMGUIModule/JSONSerializeModule (all `<Private>false</Private>`),
  and bundled 0Harmony 2.3.3 (net472). **1.6 does NOT bundle Harmony** — the mod ships its own `Assemblies/0Harmony.dll`.
- Harness: **107/107** against the rewritten 1.6-fidelity stubs (was 102; +PatchAll assertion, tab test grew 2→7 checks).

### 8.2 Verified API corrections (pre-1.6 assumption → 1.6 reality)

| Pre-1.6 assumption | RimWorld 1.6 reality (verified) |
|---|---|
| `HediffSet.PainLevel` float | **Gone** — use cached total `PainTotal` (public float, refreshed via `DirtyPainCache()`) |
| `pawn.InnerRoom` | **Gone** — `RegionAndRoomQuery.GetRoom(Thing, RegionType)` is the canonical lookup; `RegionType` is a `[Flags]` enum with no PassableOnly member → use `Set_Passable` (Normal\|Portal\|Fence) |
| `CanReach(IntVec3, PathEndMode)` instance method | **Pawn extension on `Verse.ReachabilityUtility`**: `(Pawn, LocalTargetInfo, PathEndMode, Danger maxDanger, bool canBashDoors=false, bool canBashFences=false, TraverseMode mode=TraverseMode.ByPawn)`; `Danger {Unspecified,None,Some,Deadly}` + `TraverseMode` in Verse |
| `LocalTargetInfo` class with parameterless ctor + public Position | **Verse STRUCT** — only `(Thing)` and `(IntVec3)` ctors; no parameterless ctor, no public Position (`default(LocalTargetInfo)` for empty). `TargetInfo` is also a Verse struct |
| `JobDefOf.LieDown` / `JobDefOf.Talk` | **`JobDefOf.LayDown`** (vanilla `JobGiver_ForceSleepNow` → `MakeJob(JobDefOf.LayDown, pawn.Position)`); no Talk def — the social skeleton uses `JobDefOf.Wait` at the other pawn's cell |
| `ThingDef.labelLower` | **Gone** — lowercase via a helper built from `label` |
| `IntVec3.LengthIntApprox` | **Gone** — use `LengthHorizontal` (float, Euclidean XZ) |
| `MapComponent.MapInitialized()` hook | **Gone** — the post-gen hook is `MapGenerated()` |
| JsonUtility in Assembly-CSharp / Unity core | Unity 2022.3 module split: `JsonUtility` → **JSONSerializeModule.dll** (namespace UnityEngine); GUI/Text → IMGUIModule.dll; Rect/Vector2/Color → CoreModule.dll |
| Component registration via an explicit registry | **Reflection auto-discovery**: `Map.FillComponents()` / `World.FillComponents()` iterate AllSubclassesNonAbstract and instantiate `(map)`/`(world)` ctors — no registration API exists |
| Pawn tabs via a `PawnTabManager` patch | **ThingDef XML `<inspectorTabs>`** + shared singletons via `InspectTabManager.GetSharedInstance` (parameterless ctor required, NO per-pawn state; read `SelPawn` live at draw time) |

### 8.3 Verified namespaces (reflection probe on the real DLL)

- **Verse**: `LocalTargetInfo` (struct), `TargetInfo` (struct), `JobDef`, `RulePackDef`, `LetterDef`,
  `LookTargets`, `InspectTabBase`, `ReachabilityUtility` (CanReach extension), and abstract class `JobMaker` with
  MakeJob overloads: `()`, `(def)`, `(def,A)`, `(def,A,B)`, `(def,A,B,C)`, `(def,A,expiryInterval,checkOverrideOnExpiry)`,
  `(def,expiryInterval,checkOverrideOnExpiry)` — all returning `Job`.
- **Verse.AI**: `ThinkNode_JobGiver`, `ThinkNode_Conditional` (both abstract).
- **RimWorld**: `InteractionDef`, `ThoughtDef` (`Label` is a real string property), `Thought_Memory`,
  `MemoryThoughtHandler` (`TryGainMemory` has TWO overloads — the mod pins `(Thought_Memory, Pawn)` by parameter types),
  `Pawn_InteractionsTracker` (private `pawn` field with no accessor — patch recovers it via cached reflection on
  `TryInteractWith(Pawn, InteractionDef)`), `InteractionWorker` (exactly two virtuals:
  `RandomSelectionWeight(Pawn,Pawn)` and `Interacted(Pawn,Pawn,List<RulePackDef>, out string, out string, out LetterDef, out LookTargets)`),
  `Pawn_DraftController` (`Drafted` property setter is the patch target), all `*DefOf` static classes.
- **RimWorld.Planet**: `WorldComponent` / `World` (moved out of Verse in 1.6).

### 8.4 ThinkTree patch — xpath verified against real Core XML

- `MainColonistBehaviorCore` lives in `Data/Core/Defs/ThinkTreeDefs/SubTrees_Misc.xml` (lines 6–41):
  thinkRoot = `ThinkNode_Tagger(SatisfyingNeeds)` with exactly **one** direct child
  `li[@Class="ThinkNode_PrioritySorter"]` (GetFood/GetRest/…/GetJoy/Work) → the patch xpath matches uniquely.
- The subtree is referenced from `Humanlike.xml` lines 362–369 (`ThinkNode_ConditionalColonist`) positioned
  **after** QueuedJob (line 89) and Drafted→JobGiver_Orders (lines 92–102), before emergency work → exactly the
  Spec §6.1 position: AI branch below player orders/emergencies, above the autonomous sorters.

### 8.5 Packaging decisions forced by the real install

- **About.xml: removed `modDependencies` on brrainz.harmony** — the mod bundles its own 0Harmony.dll in Assemblies/,
  and a Workshop dependency would block enabling on this fresh install (empty Mods folder, no Workshop mods).
- **Harmony PatchAll wiring**: the `AIColonistMod` ctor now calls `new Harmony("jrhh.AIColonists").PatchAll()` —
  previously nothing applied the three `[HarmonyPatch]` classes (the mod would have loaded silently inert).
  The harness asserts the call. Verified present in the shipped DLL by decompilation.
- **Tab registration**: `Patches/AIColonistTabs.xml` appends `<inspectorTabs><li>AIColonists.UI.ITab_Pawn_AIMind</li></inspectorTabs>`
  to `/Defs/ThingDef[defName="Human"]` — child-element predicate; the attribute form `[@defName='X']` matches nothing on 1.6 (§10.2).
  BasePawn owns the inherited tab list and Human does not override it (verified in install); XmlInheritance merges our `<li>` into
  the inherited list with no vanilla tab loss (live-verified §9.1/§10.3). Label via `Languages/English/Keyed.xml` `<TabAIMind>AI Mind</TabAIMind>`.
- **Deployed**: whole folder copied to `<RimWorld install>/Mods/AIColonists/`
  (About/, Defs/, Patches/, Languages/, Assemblies/ with AIColonists.dll + 0Harmony.dll, server/).

## 9. Live in-game session — RimWorld 1.6.4871 (2026-11-12)

Environment: existing Esbium colony (**loaded-save path = primary test path**), workshop mods `brrainz.harmony` +
`void.charactereditor` enabled (no interaction observed). Server: the LAN GPU node (`<gpu-node>:8091`), llama.cpp **Nov 9 build**,
Qwen2.5-1.5B-Instruct-Q4_K_M, CPU (`-ngl 0 -c 4096 --cache-reuse 256`). Settings applied via the in-game UI (user):
`EnableAI=true`, `Layer1ServerUrl=http://<gpu-node>:8091`, `RequestTimeoutMs=30000` — cfg file confirmed on disk.

### 9.1 Checklist A–D results

| Item | Result |
|---|---|
| B3 load line, no exceptions / no-match | ✅ `[AIColonists] Loaded (Harmony patches applied)` present; zero AIColonist exceptions; zero "Patch operation … failed" lines for either patch XML (full-log sweep: 84 lines, clean) |
| D2 `Layer 1:` diagnostics | ✅ continuous while unpaused (~30 s per pawn, dual-clock gate) — e.g. `Human1003 Layer 1: impulse_id=4 of 6, inner_voice="I need to work on the slate nearby."` |
| D2 **first executed desire** (`thinks:` line) | ✅ **two executions observed** — §9.2 |
| D3 AI Mind tab | ✅ present on every human colonist card; shows `Ai mind data not yet available (Phase 4)` — designed Phase-1 placeholder, not a bug |
| D4 override test (Spec §6.1) | ✅ user-confirmed: drafting + manual orders worked fine throughout the session; AI never contested player intent |
| D5 thought bubble (Spec §6.2) | ✅ user visually confirmed — "I need food" over Aiglamene (Logger); log + server evidence in §9.6 |

### 9.2 First executed AI desires (evidence)

```log
[AIColonists] Human1006 Layer 1: impulse_id=1 of 6, inner_voice="Eat it, it'll be good for you."
[AIColonists] Cook<color=#999999FF>, Manager</color> thinks: Eat it, it'll be good for you.
...
[AIColonists] Human1000 Layer 1: impulse_id=1 of 6, inner_voice="I need food!"
[AIColonists] Aiglamene<color=#999999FF>, Logger</color> thinks: I need food!
```

Full chain verified live: re-think → `JobGiver_AIColonistDesire` pops the buffered desire → main-thread
`IsValidAndReservable` re-validation → `JobMaker.MakeJob` → job consumed by RimWorld's own tracker (the `<color>` tags are
RimWorld rich text in `LabelCap`). Work impulses (slate, ibex, torch lamp) parsed fine but were discarded at the consumer as
designed — Phase 1: work affordances carry null `jobDef`.

Server-side witness (`/tmp/aicolonists-e2e/server-8091.log`): every completion `truncated = 0`; LCP slot selection active
(`f_sim_best` 0.63–0.83 → KV cache reuse working); no errors; ~2.2–3.3 s total per call on CPU.

### 9.3 Wire serialization — root causes found in-game (supersedes §4)

JsonUtility is a **dead end** for this mod — confirmed and eliminated:

1. **Class-typed fields silently dropped by `ToJson` despite `[Serializable]`.** Adding `[Serializable]` to every wire class did
   not stop Unity 2022.3's `ToJson` from dropping `messages` (List) and `response_format` (class) in the live game process
   (llama-server answered 400 `'messages' is required`). `FromJson` shares the same native writer/reader, so it was equally
   untrustworthy — and outside the game process the ECall throws SecurityException, making it **untestable in a harness**.
   Fix: **hand-built request JSON + fully manual response parsing** in `LlamaServerClient` (`BuildChatCompletionsJson`, public
   static; escape-aware `FindKeyValueColon`/`ExtractJsonString`/`ExtractJsonInt`). Zero JsonUtility references remain —
   ilspycmd-verified in the deployed DLL. Harness guards: messages regression guard + escape round-trip (§2).
2. **The Nov 9 llama.cpp build silently ignores string-form `json_schema`.** §4's A/B was validated against an older build; on
   the Nov 9 binary, `{"type":"json_schema","json_schema":"<string>"}` returns HTTP 200 with **unconstrained** output.
   `type:"grammar"` → 400 `"response_format type must be one of \"text\" or \"json_object\""`. Only the object form works:
   `{"type":"json_schema","json_schema":{"name":"layer1","schema":{...}}}`. The request's `model` field is irrelevant
   (q8_0 vs q4_k_m names tested — same result; a single-model server ignores it).
3. **Schema property order = GBNF generation order.** With `inner_voice` first, the 1.5B model spent all 80 tokens on prose and
   was truncated before emitting `impulse_id`. The Layer-1 schema now puts `impulse_id` **first** so the decision survives any
   max_tokens cut; the parser is truncation-tolerant (throws only when BOTH `inner_voice` and `impulse_id` are missing).
4. Final wire format validated 3/3 against live 8091 before deploy: HTTP 200, `finish_reason=stop`, constrained, in-enum.

### 9.4 In-game bugs fixed this session (all verified fixed in-game)

| # | Bug | Fix |
|---|-----|-----|
| 7 | **Map-gen freeze on new maps**: vanilla calls `MemoryThoughtHandler.TryGainMemory` with `otherPawn` left at its NULL default (before `Find.World` exists); the postfix dereferenced it → NRE | Key on the **owning** pawn (`__instance.pawn`, public in 1.6), not `otherPawn`; every observational postfix null-guards all state access + try/catch with `Log.ErrorOnce` — vanilla flow must never break (`ThoughtHandlerPatches.cs`) |
| 8 | **AffordanceScanner NRE**: `PotentialWorkThingsGlobal` can return null in some map states | Null-checked before use (`AffordanceScanner.cs`) |
| 9 | **Loaded-save bridge gap**: `MapGenerated()` fires ONLY from `Verse.MapGenerator.GenerateMap` (new maps — decompile-verified); loaded colonies never call it, so the LLM bridge stayed null and every dispatch silently no-opped (zero requests ever reached the server) | Lazy bridge creation in `DispatchLayer1` (`AIColonistGameComponent.cs`) |

### 9.5 Observed model behavior notes (Qwen2.5-1.5B-Instruct, q4_k_m, CPU)

- **Prose/structure divergence is common**: e.g. `impulse_id=1` with inner_voice `"Work on the human nearby."`. Harmless by
  design — the job comes from the affordance table via `impulse_id`, never from prose; `inner_voice` is flavor only (log / future bubble).
- **`impulse_id` is a 1-based index into the current affordance list** (food/rest/social/work as present, in scan order), not a
  fixed semantic enum — so "id 4 = work" varies per pawn/scan. The schema's `enum: [1..N]` is generated per snapshot.
- No divergent or out-of-range desire ever crashed or produced an invalid job: main-thread re-validation either issued a valid
  job (§9.2) or silently discarded it and vanilla AI took over (Spec §10 fallback).

### 9.6 Thought bubble (MoteText) — implemented + live-verified (second live session)

**Implementation** (`UI/MoteBubbleUtility.cs`, deployed DLL hash `50BC34…F866AA`): the persistent `"thinks:"` log line is
kept, then `RimWorld.MoteMaker.ThrowText(pawn.DrawPos + (0, 1.5, 0), pawn.MapHeld, Truncate(text))` — the bubble anchors
1.5 m above the pawn's head. Guards: null/empty text and unspawned pawns (no anchor); try/catch →
`Log.Error("Mote throw failed")`. Call site is `JobGiver_AIColonistDesire.TryGiveJob` — it runs **only when a job is
actually granted** (after main-thread re-validation), so bubble == executed desire.

**API findings** (decompile-verified; full map in §10.6): vanilla ships exactly this primitive — `ThrowText` spawns the
`Mote_Text` def (`drawGUIOverlay=true`, `realTime=true`, `solidTime=2.2`) → ~3 s lifetime of **REAL time** (fades even
while paused, so bubbles can never pile up). No custom Mote subclass or MoteDef needed.

**Live evidence (second session, same day):**

- User visually confirmed the bubble: **"I need food"** over Aiglamene (Logger) — log line 100
  `[AIColonists] Aiglamene<color=#999999FF>, Logger</color> thinks: I need food now!`
- This launch: **28** `Layer 1:` round-trips, **3** executed desires (`thinks:` lines 78/88/100 — all food),
  **0** `Mote throw failed`, zero AIColonists exceptions.
- Server side clean: every completion `truncated = 0`, ~3.5 s per call on CPU, LCP reuse active (`f_sim_best` up to 0.79).

**Behavioral note:** the first "no sign of AI" report turned out to be Phase-1 design working as intended — of ~28
impulses in that stretch only 3 were food/rest/social (executable); the rest were work impulses silently discarded at the
consumer. The earliest bubble was just the word "hunger" (Tiny font, ~3 s) — easy to miss; a full sentence like
"I need food now!" is unmissable.

## 10. RimWorld 1.6 patch/XML + UI conventions (verified live; pending doc from earlier sessions)

### 10.1 Patch file routing

- Files under `<mod>/Defs/**` are parsed **strictly as def files** — an `<Operation>` child there fails at load with
  `Type ... is not a Def type or could not be found`.
- Patches live in a top-level `Patches/` folder with root `<Patch>` (mirrors the game's own expansions, e.g. `Data/Royalty/Patches/*`).
  Deployed: `Patches/AIColonist_ThinkTreePatch.xml`, `Patches/AIColonistTabs.xml`; genuine defs stay under `Defs/` (`InteractionDefs/`).

### 10.2 XPath convention in unified patch docs

- The unified patch document is RAW XML (no attribute mirroring), so `defName` — a **child element** in every def file — must be
  addressed with a child-element predicate: `[defName="MainColonistBehaviorCore"]`, exactly as the game's own patches do
  (e.g. `Data/Royalty/Patches/Things_Drugs.xml`: `/Defs/ThingDef[defName="WakeUp"]/description`). The attribute-axis form
  `[@defName='X']` matches nothing and fails with "Patch operation … failed" — that line comes from
  `Verse.PatchOperation.Complete()` when zero nodes matched (silent no-op, no crash).

### 10.3 Patch merge semantics (verified live)

- Appending a same-named child element (`<inspectorTabs>` on Human) does **not** replace the inherited list: XmlInheritance
  recurses into it and appends our `<li>` after the vanilla tabs — no vanilla tab loss (D3: all standard tabs present + AI Mind).

### 10.4 Settings tab rule (1.6)

- The Mod List shows a "Mod Options" button for a mod only when `Verse.Mod.SettingsCategory()` returns non-empty; the dialog
  body is `DoSettingsWindowContents(Rect)`. Implemented in `AIColonistMod.cs` — user-verified live (settings applied via UI,
  cfg written on window close via `Dialog_ModSettings.PreClose → WriteSettings()`).

### 10.5 Widgets API (1.6 / Unity 2022.3 IMGUIModule)

- `Widgets.CheckboxLabeled(Rect, string, ref bool)` returns **VOID** — no return value to check.
- `Widgets.TextField(Rect, string)` **returns** the edited string — must be reassigned to persist (the settings UI does this).
- No `IntText`/`FloatText` in 1.6 — numeric fields are `TextField` + parse (`RequestTimeoutMs`).
- Phase 4 additions (2026-11-13, decompiled from the real DLLs):
  - **ABSENT** (binary metadata search + ilspycmd, all four): `Widgets.RadioList`, `GenText.AutoWrap`, `Widgets.FillRect`,
    `Widgets.TextFieldLine`. Preset selectors therefore use `ButtonText` rows; wrapped text uses a cached Unity
    `GUIStyle { wordWrap = true }`; bars draw `Texture2D.whiteTexture` tinted via `GUI.color`.
  - `Widgets.ButtonText(Rect, string label, bool drawBackground = true, …, TextAnchor? overrideTextAnchor = null)`
    **returns bool** (clicked). The optional `TextAnchor?` parameter forces the compiler to resolve
    `UnityEngine.TextRenderingModule` even for 2-arg calls — the csproj now references it (CS0012 on first build).
  - `Verse.Widgets.Label` has NO style-taking overload. Full family: `(Rect, GUIContent)`, `(Rect, string)`,
    `(Rect, TaggedString)`, `(float x, ref float curY, float width, string, TipSignal)`, `(Rect, ref float y, string, TipSignal)`.
    Word-wrapped labels go through **`UnityEngine.GUI.Label(Rect, string, GUIStyle)`** (verified present in IMGUIModule).
  - `UnityEngine.Texture2D` exposes the built-in white texture as **`whiteTexture`** — NOT `white` (CS0117 on first build;
    decompiled CoreModule confirms `public static extern Texture2D whiteTexture`).
  - `RimWorld.ITab` lives in the **RimWorld namespace**, not Verse (the tab class needs `using RimWorld;`).
  - `Dialog_ModSettings` (namespace RimWorld): `InitialSize (900, 700)`, top area 40 px + close button — content rect is
    ~635 px tall with **no scroll**; settings UIs must budget to fit.

### 10.6 Mote API (1.6) — thought bubbles

- `MoteMaker` is a **static class in the RimWorld namespace** — `Verse.MoteMaker` does NOT exist in 1.6 (moved).
  `ThrowText(Vector3 loc, Map map, string text[, Color][, float timeBeforeStartFadeout])` spawns the vanilla `Mote_Text`
  def (`Data/Core/Defs/Effects/Mote_Special.xml`: thingClass=`MoteText`, drawGUIOverlay=true, realTime=true,
  solidTime=2.2) and gives it a gentle upward drift; ~3 s lifetime of **real time**.
- No `MoteDefOf` type — motes are plain ThingDefs (vanilla references `ThingDefOf.Mote_Text`).
- `Verse.GenMapUI.DrawText` renders **one Tiny-font line with no wrapping** (gray background box) — long strings clip
  visually; truncate client-side (mod: 48 chars + "…", full sentence kept in Player.log).
- Fake-mood-thought approach ruled out: `MakeMoodThoughtBubble` explicitly skips thoughts with `MoodOffset() == 0`.

## 11. Phase 3 live session — Layer 2 social + Layer 3 memory drift + mood bridge (2026-11-13)

Environment: fresh RimWorld launch (PID 28836, started 10:24 AM) with the transport-fix build `60DE4543…`, colony
"Onnana Nation" unpaused at 1×. All three layer URLs → the LAN GPU node (`<gpu-node>:8091`) (cfg verified on disk before launch).
Server: PID 713225, Qwen2.5-1.5B-Instruct-Q4_K_M CPU (`-ngl 0 -c 4096 --cache-reuse 256`), ~20 h uptime, never restarted.

### 11.1 All three layers live-verified (Player.log sweep, 160 lines)

| Layer | Witnesses | Server-side |
|---|---|---|
| 1 (impulse → job) | continuous `Layer 1:` diagnostics + `thinks:` executions, as in §9.2/§9.6 | ~300–340 n_tokens/call |
| 2 (room social) | **9 decisions** — lines 80/88/104/112/126/134/135/137/140: warm_chat ×6 + sharp_remark ×3 across the Hercules/Cook/Aiglamene pairs, e.g. line 80 `Hercules Layer 2: warm_chat -> Cook "Cook, I'm feeling warm toward you. Let's talk about food."` | ~525–585 n_tokens/call (Layer 2 budget), all `truncated = 0` |
| 3 (memory drift) | **11 consolidations applied** — lines 82/85/87/101/107/111/124/131/133/139/141, all three named colonists at ~5-min cadence staggered ~30 s (Aiglamene 82→101→124→139; Cook 85→107→131→141; Hercules 87→111→133), e.g. line 87 `Hercules … Layer 3: consolidation applied - "I am becoming more patient and understanding."` | up to ~975 n_tokens/call, all `truncated = 0` |

Zero transport errors, zero dispatch failures, zero AIColonists exceptions across the whole log. The Mono
single-flight gate held for the entire session (no `[AIColonists] transport error, retrying once` lines at all).
Minor observation: line 139's consolidation carried an **empty memory string** — `ApplyLayer3Result` no-oped cleanly
(truncation-tolerant parse + clamps), no crash.

### 11.2 The gap the user reported

User saw per-colonist play logs but **only vanilla interaction sentences** ("Cook commented about fashion to
Hercules") — zero mod entries, and no memory thoughts in the Mood tab (note: 1.6 has no "Thoughts" section; memory
thoughts appear under **Mood** as mood drivers and in the per-pawn **Opinion** breakdown — earlier instructions used
the wrong label).

### 11.3 Root cause — interactions never executed (decompiled 2026-11-13)

`Pawn_InteractionsTracker.TryInteractWith(Pawn, InteractionDef)` (full method decompiled from the real 1.6 DLL):

- **No `baseChance` gate on the direct path** — `baseChance=0` only suppresses random selection (plus our worker's
  `RandomSelectionWeight = 0`). The defs were fine.
- Real gates, all inside `CanInteractNowWith(recipient, intDef)`: 120-tick absolute cooldown since the pawn's last
  interaction of ANY kind; `recipient.Spawned`; **`IsGoodPositionForInteraction` — `InHorDistOf(6f)` (≤6 cells
  horizontal) AND `GenSight.LineOfSight`**; then `CanInitiateInteraction`/`CanReceiveInteraction` (Talking capacity,
  awake, not burning, `IsInteractionBlocked`). **Every gate failure returns false silently.**
- Our batcher groups by `Room.ID` with no proximity filter — the LLM picks any pair in the room; in a large room most
  pairs are >6 cells apart → every attempt died at the position check. The old consume path logged its witness line
  **unconditionally** (after the attempt, without the result), so Player.log proved dispatch but hid execution.
- The mood bridge was correct all along: on success `TryInteractWith` grants `intDef.initiatorThought`/`recipientThought`
  via `AddInteractionThought` → vanilla memory machinery — it simply never ran because no interaction ever succeeded.

### 11.4 Fix — proximity pre-check + per-decision outcome logging (deployed build `601310A4…`)

`ConsumeSocialDecisions` now pre-checks with the **same public gate vanilla itself uses**
(`initiator.interactions.CanInteractNowWith(target, def)`), drops non-interactable decisions (they re-roll next ~90 s
cycle — same as vanilla social cadence), and logs the outcome of every decision:

```log
[AIColonists] Hercules Layer 2: warm_chat -> Cook "…" [executed]                          ← play-log entry + thoughts granted
[AIColonists] Cook Layer 2: sharp_remark -> Hercules [dropped: not interactable (distance/LOS/cooldown) — re-rolls next cycle]
[AIColonists] Aiglamene Layer 2: warm_chat -> Cook [attempt failed at the last moment]     ← race between check and call (rare)
```

This also delivers the Player.log observability half of backlog #7 (the in-game play-log section remains parked).
Harness: **202/202** (+1 case — pre-check false drops before any payload stash; stub `CanInteractNowWith` tagged
`[CONFIRMED]` from today's decompile). Deployed DLL SHA256 `601310A4887C022B54BD62DADD8B5A8C4B7E30BA72E797761D443A9167F896A4`
(82,944 B) — hash + ilspycmd verified on the deployed copy.

### 11.5 Footnotes

- **URL incident (resolved):** each layer reads its own URL field live from settings; two were stale in memory while
  Layer 1 was correct (Layer 3 pointed at `:8092` — nothing listens there; Layer 2 at a refused endpoint). Fixed via the
  in-game settings UI (no restart needed for the fix itself); user restarted anyway for a deterministic state. Forensic
  oddity, left unresolved and moot after restart: cfg on disk showed all three `:8091` with a stale LastWriteTime while
  the in-memory fields differed, and window-close never rewrote it; no stray `.cfg` copies exist; user states they never
  hand-edited files (hypothesis: a pre-compaction assistant direct-file edit lost to compaction).
- **Historical server-log 400s** (`'messages' is required`, `response_format … grammar`) occur at uptime ~215–244 min —
  the Nov 12 wire-format debugging (§9.3), not today's session. Whole-log `truncated = 1` count: **0**.
- **Harness transport-retry test is flaky on this host** (Windows loopback RST/FIN race in the fake server): failed twice
  with different socket errors ("forcibly closed by the remote host" / "aborted by the software in your host machine"),
  passed on the third run. Product code untouched; consistent with the existing wall-clock-dependent-test note (§2).

### 11.6 Proximity-fix re-test — VERIFIED (2026-11-13, live session 3)

Colony "Onnana Nation", build `601310A4…`. Player.log sweep (session log grew 112 → 137+ lines during play):

| Item | Evidence |
|---|---|
| G1 — interaction executed | Line 118: `Cook Layer 2: warm_chat -> Hercules "Hey Hercules! You're doing great work. How's the pasta coming?" [executed]` — first live-executed AI social interaction; letter + play-log sentence ("…shared a warm chat.") user-confirmed in-game |
| G7 — outcome tags | `[dropped: not interactable …]` ×2 (lines 100, 119) + `[executed]` ×1 (line 118); no `[attempt failed at the last moment]` this session |
| G2 — Layer 3 | 14 `Layer 3: consolidation applied` lines — all three colonists on the ~5-min staggered cadence (e.g. Cook: "Chitchat is a tool I use to navigate my harsh reality.") |
| G3/G4 — mood bridge | User-confirmed: warm chat visible in the **Mood tab** and the pawn play log. Warm-chat side fully verified; sharp-remark / venting variants await their first successful execution (defs decompile-verified, §11.3) |
| G5 — server cadence | Whole-log `truncated = 1` count **0** across 505 completed tasks; recent L2/L3-sized tasks (n_tokens 328–1113) all `truncated = 0` |
| G6 — transport | Zero `dispatch failed` / `transport error, retrying` lines in Player.log this session |

**Timing observation (user-reported, benign):** the executed warm chat appeared "right as I unpaused." That is the
designed path, not a bug: dispatch only runs while unpaused (`MapComponentTick` early-returns when paused), but a
request dispatched in the last moment before pause completes on its background thread ~1–3 s later — possibly during
the pause — and sits in `socialBuffer` until `ConsumeSocialDecisions` runs on the first tick after unpause. The user
will note wall-clock times at the next occurrence to distinguish "response landed during pause" (<3 s after unpause)
from a fresh ~90 s batch.

**Phase 3: complete.** Checklist G closed (G4 partial — warm-chat side only, see above).

### 11.7 Stale-desire bug — "I need food" right after eating (found session 3; fixed in staged build `6CC1D36F…`)

User observed a colonist start an eat job immediately after finishing a meal ("I need food"). Root cause:
`AffordanceScanner.Scan` only offers the eat option while `food.CurLevelPercentage < 0.4f`, but
`QueuedDesire.IsValidAndReservable` (the main-thread re-check at pop time) validated target existence + reachability
only — never that the driving need still existed. A desire pushed while hungry therefore survived into a post-meal
pop and fired a second Ingest job.

**Fix:** `IsValidAndReservable` now mirrors the scanner's own gates before accepting a desire: `Ingest` requires
`food.CurLevelPercentage < 0.4f`; `LayDown` requires `rest != null && rest.CurLevelPercentage < 0.4f`. Work (null
jobDef) and Wait/social options are unaffected (already rejected or harmless).

- Harness: 3 new cases in `Test_QueuedDesireValidation` (stale eat rejected; tired pawn's rest passes; rested
  pawn's rest rejected); one existing case made hungry to stay valid → **205/205**.
- Staged build: SHA256 `6CC1D36F82BE7A86ED19174805FAF4ECED375AB812D80EFA2F5F30885D8F6953` (82,944 B) in
  `Assemblies/`, awaiting deploy — the game was running when the fix landed.
- Related parked item: the user's stated end goal — suppressing/reordering vanilla JobGivers so AI decisions take
  priority — is backlog #8 (performance-dependent). The freshness check is a stopgap within the current architecture,
  where AI desires compete with vanilla givers for the same pawn.

## 12. Phase 4 — UI Inspector, Diagnostics & Workshop Polish (2026-11-13)

Spec §9 lines 642–650: AI Mind tab content, settings-menu completion (models / dual-clock / VRAM presets),
district batching fallback for the 50-colonist stress test, production release. All product code written, built
against the real 1.6 DLL (zero warnings), harness **228/228** (+23 new tests), deployed and hash-verified:
SHA256 `E23042B6BFF0F3FEA434415AE561740024487A724DFC185E29AA0F975569315B` (92,672 B) in the live mod folder
(game closed at deploy time). In-game visual verification is pending — checklist section H.

### 12.1 API verification results (real DLLs, first-build failures as evidence)

| Claim | Verdict | Evidence |
| :--- | :--- | :--- |
| `Widgets.RadioList` / `GenText.AutoWrap` / `Widgets.FillRect` / `Widgets.TextFieldLine` exist in 1.6 | **ABSENT** | binary metadata search (ASCII scan of Assembly-CSharp.dll) + ilspycmd type dumps |
| `Verse.Widgets.Label(Rect, GUIStyle, string)` exists | **ABSENT** — full Label family dumped; no style overload | CS1620 ("Argument 2 must be passed with ref") on first build, then decompile of `Verse.Widgets` |
| `UnityEngine.Texture2D.white` exists | **ABSENT** — it is `Texture2D.whiteTexture` in Unity 2022.3 / this install | CS0117 on first build + ilspycmd of CoreModule (`public static extern Texture2D whiteTexture`) |
| `UnityEngine.GUI.Label(Rect, string, GUIStyle)` exists | **PRESENT** (IMGUIModule) — used for all wrapped tab labels | ilspycmd of `UnityEngine.GUI` from IMGUIModule.dll |
| 2-arg `Widgets.ButtonText(Rect, string)` compiles without extra refs | **NO** — optional `TextAnchor?` param forces TextRenderingModule | CS0012 naming the missing assembly; csproj reference added |
| `ITab` is in `Verse` | **NO** — it is `RimWorld.ITab` | CS0246 on first build of the rewritten tab |

Lesson recorded: every Unity-module type used by UI code must be verified against THIS install's module split, not
against generic Unity docs — 2022.3 renames (`white` → `whiteTexture`) and RimWorld's stripped module set both bite.

### 12.2 AI Mind tab (Spec §9 Phase 4, line 644)

- Architecture: pure **`AIMindViewModel.BuildFromMind(pawn, mind, nowTick)`** (caps + ordering + name resolution,
  harness-pinned) plus a thin guarded `Build(pawn, nowTick)` wrapper; the tab's `FillTab` only draws. Caps: 3 memories
  newest-first, 6 trait bars most-drifted-from-0.5-first with name tiebreak, 4 relationships most-recent-first.
  Target ThingIDs resolve to `LabelCap` among the map's free colonists, else "unknown".
- **Inner monologue write site:** mind state is not thread-safe and Layer 1 results arrive on background workers, so
  `lastInnerVoice` / `lastInnerVoiceTick` are written on the MAIN thread by `JobGiver_AIColonistDesire.TryGiveJob` at
  pop-success (the only main-thread moment where monologue text exists), guarded by `Find.World` null-checks. Both
  fields persisted via ExposeData.
- Tab size **300 × 630** fixed, no scroll (1.6 pawn-tab convention); wrapped labels via cached
  `GUIStyle { wordWrap = true, richText = false, fontSize = 13 }`; bars via `GUI.color` +
  `GUI.DrawTexture(Texture2D.whiteTexture)` (track 0.25/0.27/0.31, fill 0.55/0.75/1). Height budgeted ≤635 px against
  the decompiled `Dialog_ModSettings`-independent tab frame; user visually verifies (checklist H1/H2).

### 12.3 Settings menu completion + VRAM presets (Spec §9 Phase 4, line 646)

- New rows in `DoSettingsWindowContents`: model names ×3 (`Layer{1,2,3}Model`), dual-clock real-time ceilings ×3 in
  seconds (`Layer{1,2,3}MinRealSeconds`, parse clamped ≥5 s), and a preset button row (verified `ButtonText`, 200×30,
  stride 214). Total content budgeted to fit the 900×700 no-scroll dialog (~635 px content rect).
- **`VramPresets`** (pure data + null-safe `Apply(Preset, Settings)`): values from spec hardware table lines 589–590 +
  §10 risk line 661 — `CPU homelab`: all layers `qwen2.5-1.5b-instruct-q4_k_m`, 30000 ms (the live-verified Phase 1–3
  config); `1x P100 16GB`: L1/L2 `qwen2.5-3b-instruct-q8_0` + L3 `qwen2.5-7b-instruct-q4_k_m`, 15000 ms;
  `2x P100 32GB`: same L1/L2 + L3 `qwen2.5-14b-instruct-q4_k_m`, 15000 ms (Option A; Option B MoE is a manual choice —
  set all three model fields by hand). **Presets write model names + timeout ONLY, never URLs** (harness-pinned:
  URL-tamper check) — a dual-instance layout needs two servers the user owns.

### 12.4 District batching fallback (Spec §9 Phase 4, line 648)

- `AreaBatcher.SelectBatchesForDispatch(Map, int maxBatches)`: normal path = room/cluster batches with ≥2 pawns
  (pass-through when eligible ≤ cap). Fallback: all eligible pawns re-keyed onto a **30-tile grid**
  (`DistrictGridSize`), districts sorted by pawn count desc (biggest first), truncated to the cap — coverage instead of
  silent drops. `MaybeDispatchLayer2` now computes `headroom = MaxLayer2BatchesPerCycle − layer2InFlightCount` and
  passes it in (old manual eligible-list + min-toDispatch removed).
- Safety: cross-space pairs inside a merged district simply fail the existing 6-cell proximity pre-check at consume
  time (`[dropped: not interactable …]`, re-roll next cycle) — merging can never force an impossible interaction.
- Harness (4 new cases in `Test_AreaBatcher`): pass-through returns exactly 3 batches for 3 eligible clusters vs cap 4;
  fallback case = 6 clusters ×2 pawns → districts [4,2,2,2,2] on the 30-tile grid → result count 4, biggest (merged
  A+B) first, coverage 10/12.

### 12.5 Harness: 205 → 228 (+23)

New sections: `Test_AIMindViewModel` (empty-mind hint; monologue + tick age; episodic cap-3 newest-first; latest
semantic; trait ordering with EXACT binary-fraction values — see float lesson below; relationship cap/ordering/name
resolution incl. "unknown"), `Test_VramPresets` (all three preset tables exact, Apply writes models+timeout only, URLs
untouched, null-safe both directions), populated-mind `FillTab` draw in `Test_TabPatch`, and the 4 district cases.

**Float-precision lesson (caught by the harness on first run):** test data assumed `|0.9f − 0.5| == |0.1f − 0.5|` —
false in binary floating point (`0.399999976…` vs `0.399999988…`, ~1e-8 apart), so the "tie" sorted by value, not by
the name tiebreak. Trait-ordering cases now use exact 1/16 fractions (1.0, 0.9375, 0.875, 0.75/0.25 as a TRUE tie,
0.625, 0.5625, 0.5) so drifts are exact and the tiebreak is actually exercised. Same rule applies to any future
float-equality test data: pick exactly representable values or assert with tolerance.

### 12.6 Deploy + pending

- Deployed 2026-11-13 ~19:04 local, game CLOSED (verified via `Get-Process RimWorldWin64` → none): robocopy
  `/MIR /XF .gitignore /XD Source Tests /R:1 /W:1`, exit 1; staged == live SHA256 `E23042B6…` (92,672 B; up from
  82,944 B at Phase 3 final `6CC1D36F…`).
- Pending in-game: checklist section H — tab visual check + size tuning (H1/H2), settings walk-through incl. preset
  buttons and cfg write (H3), 50-colonist stress test with district-fallback witness lines (H4), pause-timing
  sub-case observation (H5). Production-release description polish in `About/About.xml` shipped with this build.

## 13. Phase 4 UI fix round — tab name, clipping, black text (2026-11-13)

User report after first in-game look at the deployed Phase 4 tab: (a) the tab **name** in the bar was "a mix of non
English characters"; (b) inside the tab, every word and bar had its bottom ~4–5 px cut off; (c) *Long-term desire*,
*Recent memories*, and *Self-knowledge* text rendered black. Settings dialog: preset buttons confirmed working on
the UI side ("clicking the VRAM preset changes setting text").

### 13.1 Root causes — all three decompile-verified against this install

| # | Symptom | Root cause (evidence) |
|---|---------|----------------------|
| 1 | Garbled tab name | Translation file was `Languages/English/Keyed.xml` — a **file**. RimWorld 1.6 loads keyed translations from the **`Keyed/` folder**: decompiled `Verse.LoadedLanguage` does `GetDirectory("Keyed").GetFiles("*.xml")`, so the file was never read and `labelKey.Translate()` fell through to the raw key. The garble itself: decompiled `Verse.Translator.Translate` — when a key is missing in both active and default language **and `Prefs.DevMode` is on**, it returns `PseudoTranslated(key)` (character-substitution garble for translators). User has DevMode on → "TabAIMind" rendered as pseudo-translated lookalikes. |
| 2 | ~4–5 px bottom clip on every row | Decompiled `Verse.Text`: **`SmallFontHeight = 22f`** and `TinyFontSupported` returns false when the user has "disable tiny text" enabled or the language lacks a tiny face (then Tiny silently falls back to Small). The old tab used row pitches of 16–19 px with Tiny/Small mixed → each row's opaque bar/text painted over the descenders of the previous line (22 − 17 = 5, 22 − 19 = 3 — exactly the reported "4 or 5 pixels"). |
| 3 | Black text in wrapped sections | The word-wrap style was `new GUIStyle(GUIStyle.none)` + explicit `fontSize = 13`. A bare `GUIStyle.none` does not inherit the skin's label color/font (vanilla styles derive from `GUI.skin.label` — decompiled `Verse.Text` static ctor: `fontStyles[i] = new GUIStyle(GUI.skin.label)` with white text on Arial_small). Result: wrapped blocks (monologue/longing/memories/semantic) rendered dark. |

Also verified this round: `Widgets.CloseButtonFor` draws the tab close button at top-right `(w-22, y+4, 18×18)`
**before** `FillTab()` runs — content must stay left-aligned/clear of that corner (vanilla Character card uses a 17 px
margin for exactly this). `InspectPaneUtility.PaneSizeFor` fixes the *pane* at 165 px, but open tab windows size from
the tab's own `size` (vanilla `NeedsCardUtility.GetSize` is dynamic) — so a data-driven tab height is legal.

### 13.2 Fixes (shipped in build `DB853994…`)

- **Translation**: new `Languages/English/Keyed/AIColonists_Tabs.xml` (`<TabAIMind>AI Mind</TabAIMind>`); old
  `Keyed.xml` deleted (robocopy `/MIR` removed it from live — "Extras: 1", 142 B).
- **Tab rewrite** (`Source/UI/ITab_Pawn_AIMind.cs`, vanilla conventions per §13.1):
  - 17 px content margin, first row at y=17 (close-button corner stays clear);
  - `GameFont.Small` everywhere, row pitch ≥ 24 px (no Tiny anywhere — immune to the tiny-fallback trap);
  - wrapped blocks sized with `GUIStyle.CalcHeight(new GUIContent(text), width)` — this Unity 2022.3 build exposes
    only the **GUIContent** overload on `GUIStyle` (decompiled IMGUIModule; no string variant) — so no fixed-height
    guessing and no row can overlap its neighbor;
  - wrap style now derives from `Text.CurFontStyle` (white text, Arial_small — identical to vanilla labels);
  - every `GUI.color` mutation paired with a restore to `Color.white` (vanilla idiom); gray captions via the same
    save/restore pattern;
  - **dynamic tab height**: FillTab caches its layout height; UpdateSize clamps it to `[260, UI.screenHeight − 240]`
    (tab windows grow UP from `PaneTopY − 30`) with a try/catch fallback — like vanilla's `RecentHeight`.
    C# gotcha hit on first build: inside `namespace AIColonists.UI`, bare `UI` resolves to the namespace itself, not
    `Verse.UI` (CS0234) — qualified explicitly.
- **Harness stubs** (`Tests/GameStubs.cs`, all mirroring verified real-DLL facts): `Text.SmallFontHeight = 22f`,
  `Text.CurFontStyle`, `UnityEngine.GUIContent`, `GUIStyle.CalcHeight(GUIContent, float)` (fixed 22 px return for
  deterministic layout math), `Mathf.Max`, `Verse.UI.screenHeight`.

### 13.3 Build / test / deploy record

- Product build vs real DLLs: **0 warnings, 0 errors** (confirms `Verse.UI.screenHeight`,
  `GUIStyle.CalcHeight(GUIContent, float)`, `Text.CurFontStyle` all exist in this install).
- Harness: **228/228** (no test changes needed — Test_TabPatch exercises the new FillTab paths incl. populated mind).
- Deployed 2026-11-13 ~21:14 local, game CLOSED (verified): robocopy exit **3** (copied + removed stale `Keyed.xml`);
  staged == live SHA256 **`DB8539943C2BE067DB4361756D6FE92265E7A39B711503620688706437940AAE`** (92,672 B; same size
  as `E23042B6…` — the DLL grew/shrank within the same allocation).
- Pending in-game: H1 (tab name reads "AI Mind" + content), H2 (layout re-check — clipping/overlap/dark text all
  should be gone; height adapts to data), H3 remainder (cfg write on close + server actually serving preset model),
  H4, H5. If the tab still clips anywhere, `RowH` / `SectionGap` / `MarginX` in `ITab_Pawn_AIMind.cs` are the knobs.

## 14. Live NRE fix — JobDriver_Ingest dead-target guard (2026-11-13, H4 stress test)

During the H4 50-colonist stress run (~21:5x–22:1x local), Player.log recorded
`Exception in CheckCurrentToilEndOrFail for pawn Goo driver=JobDriver_Ingest (toilIndex=1) driver.job=(Ingest (Job_19703) B = (0, 0, 0) Giver = JobGiver_AIColonistDesire)`
+ `System.NullReferenceException`, top frame `RimWorld.JobDriver_Ingest.<MakeNewToils>b__19_0`.

### 14.1 Root cause — decompile-verified against this install

- The decompiled driver exposes `private Thing IngestibleSource => job.GetTarget(TargetIndex.A).Thing` — **no null guard**.
- Its driver-level FailOn lambda (`this.FailOn(() => !IngestibleSource.Destroyed && !IngestibleSource.IngestibleNow)`) is exactly `b__19_0`; the NRE is on the first dereference.
- In 1.6 a reference to a DESTROYED thing resolves to **NULL** — proven twice: vanilla's own `GainingNutritionNow` guards with `ingestibleSource.DestroyedOrNull()` (a null check that would be pointless otherwise), and the crash died on the first dereference rather than reading `Destroyed == true`.
- Trigger: Goo was given an Ingest job for a meal (target valid at validation time). While walking to it, another of ~50 hungry colonists ate that same meal → target destroyed → next tick's `CheckCurrentToilEndOrFail` evaluated the lambda against null. A latent vanilla fragility (reservations normally protect food) exposed by the 50-colonist food war.
- The job display `B = (0, 0, 0)` is the default empty `LocalTargetInfo` our scanner sets as targetB for Ingest (documented table/chair TODO) — unrelated to the crash.

### 14.2 Fix (shipped in build `9F5263E8…`)

- New `Source/Patches/IngestibleSourcePatches.cs`: Harmony **prefix** on `get_IngestibleSource`. Healthy target → pass-through (zero behavior change). Dead/null target → hands back a live non-ingestible sentinel (`ThingMaker.MakeThing(ThingDefOf.Steel)`, unspawned; `Steel` confirmed in the decompiled `RimWorld.ThingDefOf` field listing, no `<ingestible>` def). Vanilla's own first FailOn then evaluates TRUE and **fails the job gracefully** — the pawn re-thinks and vanilla AI finds other food.
- All other driver paths verified NRE-free against the sentinel in decompile: chewing FailOn (`!src.Spawned && …` → true → fail), `ChewDurationMultiplier` (null-checks `def.ingestible`), `Notify_Starting` (`is` / `Contains` null-safe), `TryMakePreToilReservations` (`GetMaxAmountToPickup` is null-safe; an unspawned target skips the reservation manager; `ReservationManager.Reserve` rejects `!IsValid || ThingDestroyed`).
- Defensive rule kept: prefix wrapped in try/catch with `Log.ErrorOnce`; any unexpected failure falls back to vanilla behavior.

### 14.3 Also observed (not ours)

`Exception filling window for LudeonTK.EditWindow_Log: Collection was modified; enumeration operation may not execute` ×3 plus accompanying GUIClip errors — a third-party mod bug (LudeonTK enumerates the message queue while it mutates; its log-editing window was open during high-volume logging). No AIColonists frames.

### 14.4 Build / test / deploy record

- Product build vs real DLLs: **0 warnings, 0 errors** (confirms `JobDriver_Ingest`, `get_IngestibleSource` targetability, `Job.GetTarget(TargetIndex)`, `ThingMaker.MakeThing(ThingDef)`, `ThingDefOf.Steel`).
- Harness: **237/237** — new section `Test_IngestibleSourcePatch`: witnesses that the unpatched path NREs exactly like Player.log (the getter resolves to null; the first dereference dies), that the prefix intercepts, and that the sentinel is live + non-ingestible so vanilla's first-FailOn predicate becomes TRUE; unset targetA → substitution; null driver → pass-through. Stub updates: `LocalTargetInfo.Thing` now mirrors 1.6 "destroyed → resolves to null" semantics (all existing `.Thing` consumers checked compatible), `Job.targetA/targetB + GetTarget`, `Verse.AI.TargetIndex`, `Verse.AI.JobDriver.job`, `RimWorld.JobDriver_Ingest` (mirrors the unguarded getter), `RimWorld.ThingDefOf.Steel`, `Verse.ThingMaker.MakeThing`, `Thing.IngestibleNow/IsBurning`, `ThingDef.IsIngestible`, `HarmonyPrefixAttribute`.
- Deployed 2026-11-13 ~22:45 local, game CLOSED (verified): robocopy exit **1** (copy only); staged == live SHA256 **`9F5263E88673348A0CDAEBBD15D0B38FEE401734889DE19D9D962B9EE687C318`** (93,696 B).
- Pending in-game: H4 re-run – expect `Layer 2:` witness lines once 2+ undrafted colonists share an enclosed room (the gate re-checks every unpaused tick; its clock only advances on an actual dispatch), and **zero** `CheckCurrentToilEndOrFail` NREs.

## 15. Live NRE fix – LabelCap on half-loaded pawns during save load (2026-11-14)

The fresh Player.log from the H4 Layer 2 re-test run (save "New Arrivals5", 173 lines) recorded exactly one line of our defensive catch:
`[AIColonists] TryGainMemory_Postfix: Object reference not set to an instance of an object` (line 81) – fired once during **save load**; game flow continued normally.

### 15.1 Root cause – decompile-verified against this install

- The postfix itself is fully null-guarded, so the NRE was *inside* `new ColonistMindState(pawn)` → `InitializeFrom` line 42: `pawnName = pawn.LabelCap;`.
- Decompile chain (1.6): `Thing.LabelCap => Label.CapitalizeFirst(def)`; `Pawn.Label` falls back to `LabelPrefix + KindLabel` when `Name == null`; and `KindLabel => GenLabel.BestKindLabel(this)` derefs kind/race defs that are still NULL on a half-deserialized pawn – during save load, thoughts are granted while the pawn is only partially deserialized.
- The outer defensive catch (by design) saved the flow: single `ErrorOnce` line, no crash; that pawn's mind state simply wasn't created at that instant (created later by other systems).

### 15.2 Fix (build pending deploy – successor to `9F5263E8…`)

- `ColonistMindState.InitializeFrom`: LabelCap read wrapped in try/catch → leaves `pawnName` as `""` rather than storing garbage.
- Self-heal in `AIColonistWorldComponent.GetOrCreateMindState`: if `ms.pawnName` is empty, opportunistically re-resolve (guarded). Our systems touch every pawn constantly on normal ticks, so the real name lands on the next call.
- Safety: `pawnName` is consumed ONLY by `Layer3PromptBuilder.BuildSystemPrompt(mind.pawnName)` (`AIColonistGameComponent.cs:412`) – never UI or Layer 1/2 – so an empty name during load degrades at worst to a generic prompt prefix and self-heals on the next tick.

### 15.3 Build / test record

- Product build vs real DLLs: **0 warnings, 0 errors**.
- Harness: **241/241** – new section `Test_MindStateLoadTimeSelfHeal`: a `HalfLoadedPawn` fixture (stub `Pawn.LabelCap` is now a virtual property, mirroring the real overridden one) throws an NRE until its `Broken` flag clears; asserts GetOrCreateMindState survives, the mind state is created with an empty name, and the name self-heals on the next call after recovery.
- H4 re-test verdict (same log): **Layer 2 fired for the first time** – 3 dispatches (`Goo → Jackalope [vent_frustration]`, `Johs → Spike`, `Fiona → Westy`), all `[dropped: not interactable (distance/LOS/cooldown) — re-rolls next cycle]` (picked pairs were >6 cells apart / no LOS inside the room). Zero `dispatch failed`, zero Ingest NREs. An `[executed]` requires a pair within ~6 cells + clear LOS, not just same-room.
- Deployed 2026-11-14 ~01:56 local, game CLOSED (verified — process absent): robocopy exit **1** (copy only; 4 files updated: DLL + pdb + both docs); staged == live SHA256 **`4C20CEC1CA0D43E0EE4FB04DBC46CDB50BE74CC395A406B86E61F6C450BA0F04`** (93,696 B — same allocation size as `9F5263E8…`, different hash).
- Pending in-game (morning): H4 final verdict — one `[executed]` Layer 2 observation (cluster ~3–4 undrafted colonists within a few tiles of one room, unpaused a few minutes) or user acceptance of partial pass; then H3 remainder + H5. Expect the save-load `TryGainMemory_Postfix` ErrorOnce line to be gone on the next load of "New Arrivals5".

## 16. Save-load NRE round 2 – `ThoughtDef.Label` computed property (2026-11-14) + transport-test RST race

### 16.1 Symptom

With round 1 (`4C20CEC1…`) deployed and holding, the morning save load of "New Arrivals5" logged the SAME line again: `[AIColonists] TryGainMemory_Postfix: Object reference not set to an instance of an object` (Player.log line 74, right after map init — save-load scenario). Same postfix, same scenario → a second unguarded deref existed in the chain.

### 16.2 Root cause – decompile-verified against this install

- The postfix's only remaining COMPUTED deref: `newThought.def.Label`. `RimWorld.ThoughtDef.Label` is NOT a field — it is a computed property: returns `label`, else falls through to `stages[0].label` / `stages[0].labelSocial`, else defName — and the stage fallback derefs **`stages[0]` UNGUARDED**.
- During save load, memory thoughts are re-granted while a thought def's stages list can still hold a NULL first element → NRE inside the getter. Verified by elimination: `MemoryStore.AddEpisodic` is trivially safe (null/empty check + list add), every other postfix line is null-guarded, and all our ThoughtDefs carry `<label>` so the defName fallback path never ran ("Cannot get good label" absent from the log).
- Why round 1 missed it: round 1's site was a DIFFERENT line of the same postfix (`ColonistMindState.InitializeFrom` → `pawn.LabelCap`). Both fired on save load; after the round-1 guard, only the `def.Label` site remained.

### 16.3 Fix

- The postfix no longer calls the computed property: plain fields only — `newThought.def.label`, falling back to `newThought.def.defName`. Plain field reads cannot throw.
- Harness witness: stub `ThoughtDef`/`ThoughtStage` restructured to mirror the real computed getter (unguarded `stages[0]` deref). New checks in `Test_ThoughtPatch`: (a) unpatched `def.Label` NREs on a half-loaded def (list present, first element NULL — the live state); (b) the postfix survives that def and falls back to plain `defName` ("HalfLoadedThought" lands in the episodic text).

### 16.4 Transport-test RST race (harness-only — product code was never at fault)

- The retry test failed 4/4 DETERMINISTICALLY today (not a flake). A standalone instrumented repro proved the mechanism: the fake server wrote its response to connection #2 and closed within the same millisecond, BEFORE reading the client's request. Windows sends an **RST instead of a FIN when closing a socket with unread data in its receive buffer** — aborting the client's read even though the response had been delivered (sub-ms race; today's host scheduling consistently lost it).
- Fix: `DrainRequest` helper (blocking read until the request arrives, then drain queued bytes) applied to BOTH raw-TCP test servers (retry + single-flight) — fake servers now behave like real HTTP servers. Verified 3 consecutive green harness runs. No product change; in-game retry behavior was already live-verified (zero transport errors across all sessions).

### 16.5 Build / test record

- Product build vs real DLLs: **0 warnings, 0 errors**. Harness **244/244**, stable across 3 consecutive runs (241 + 3 new checks: unpatched-NRE witness, postfix survival, defName fallback).
- Deployed 2026-11-14 ~11:24 local with the game CLOSED (verified — process absent after the morning H4 attempt): robocopy exit **1** (4 files updated: DLL + pdb + both docs); staged == live SHA256 **`305A33E8308C70B4D2083962C7535AE640D1FE30F1C1563C46770F76BED3043B`** (93,696 B).
- Pending in-game: reload "New Arrivals5" → expect ZERO `TryGainMemory_Postfix` ErrorOnce lines; H4 final verdict still open.

### 16.6 Morning H4 attempt + observed self-targeting gap

- User clustered 4 pawns in a small room ~10:42, unpaused ~10:44 (log 641 lines at 11:03). Layer 2 decisions between ~10:44–10:46: `Wallis→Varvara [dropped]`, `Johs→Noble [dropped]`, `Noble→Noble [attempt failed]` ×2, `Johs→Higurashi [dropped]`. Zero `[executed]`, zero `dispatch failed`; Layer 1/3 active through line 325 (server healthy throughout).
- **Self-targeting gap**: the LLM returned the initiator's own name as target (`Noble → Noble`). The roster includes the initiator and nothing enforces "one OTHER person" — `ConsumeSocialDecisions` has no `target == initiator` exclusion; `CanInteractNowWith(self)` passes (distance 0) but `TryInteractWith` fails → safe failure, wasted dispatch. Fix options: filter self out of prompt candidates and/or defensive skip at consume (decision pending).
- **Why Layer 2 went silent after ~10:46 (by design, not a bug):** (a) after unpause the clustered pawns immediately took jobs ("work on the compacted steel", "Eat the nutrient paste!" — per their Layer 1 inner voices) and scattered; once no shared space holds 2+ undrafted colonists, `SelectBatchesForDispatch` returns empty → no dispatch, and the 90 s clock does NOT advance (it only advances on an actual dispatch), so a batch fires immediately when pawns re-cluster. (b) even when batches DO dispatch, the prompt's explicit bias ("Most moments are quiet. Prefer none") means neutral pawns mostly produce `none`/`quiet_nod`/`avoid_gaze`, which route to `RecordQuietMoment` — episodic memory + relationship nudge only, **no log line by design**. (c) the real-time gate delays the FIRST dispatch ~90 s after unpause, by which point pawns have already left the room.
- H4 `[executed]` recipe: cluster 4+ undrafted pawns within ~6 cells of each other in a small room where they will STAY (table/beds inside), then unpause — dispatch fires on the next tick because the clock is stale; give the model a reason to act (a hungry/tired pawn → "in a bad mood"/"hungry" prose) to beat the prefer-none bias.

## 17. Self-targeting guard + prompt rule (2026-11-14, bundled into build `82691858…`)

### 17.1 Symptom

Morning H4 attempt: `Noble Layer 2: vent_frustration -> Noble [attempt failed at the last moment]` ×2 — the LLM returned the initiator's OWN name as target. The roster includes the initiator and nothing enforced "one OTHER person". Safe failure (`CanInteractNowWith(self)` passes at distance 0, `TryInteractWith` then fails) but a wasted dispatch; a self-targeted QUIET decision would additionally have polluted memory ("shared a quiet moment with myself").

### 17.2 Fix (two layers, both live-deployed)

- **Consume-side guard** (`AIColonistGameComponent.ConsumeSocialDecisions`): the target is now resolved BEFORE the impulse branch; `target == initiator` drops the decision for ALL impulse types (interaction AND quiet), logged once via a main-thread `selfTargetDroppedLogged` flag, then silent. Re-rolls next ~90 s cycle.
- **Prompt-side rule** (`Layer2PromptBuilder.SystemTemplate`, stays byte-identical across calls → KV-cache reuse preserved): `target_name must be ANOTHER colonist's roster name exactly as given — NEVER the acting colonist's own name (no one can socially act toward themselves)`.

### 17.3 Build / test record

- Product build vs real DLLs: **0 warnings, 0 errors**. Harness **246/246** (244 + 2 new checks in `Test_SocialConsumePaths`: self-targeted interaction decision dropped before payload stash; self-targeted quiet decision — including the case-insensitive name match — leaves zero "with Alice" episodic memory).
- Deployed 2026-11-14 ~11:47 local with the game CLOSED (verified): robocopy exit **1**; staged == live SHA256 **`8269185866D1C6CE04B8A5332B69F778EE36E918D5D03CA81F0554CAD7371BFF`** (94,208 B).
- Pending in-game: next load of "New Arrivals5" → expect ZERO `TryGainMemory_Postfix` ErrorOnce lines; self-targeted decisions now log `[dropped: LLM targeted the initiator itself — re-rolls next cycle]` once if they recur. **CONFIRMED LIVE (2026-11-14 ~13:37 session): save load clean, zero NRE lines.**

## 18. Furniture/LOS investigation + drop-gate decomposition (2026-11-14)

### 18.1 The question (user-reported)

After a run of `[dropped: not interactable (distance/LOS/cooldown)]` lines, the user reported that furniture should NOT block line of sight and asked for it to be implemented. Full decompile of the vanilla gate chain (1.6, this install):

- `Pawn_InteractionsTracker.CanInteractNowWith` = ① `InteractedTooRecentlyToInteract()` (120-tick cooldown) → ② carrying exception (`IsCarryingPawn`, a public EXTENSION on `RimWorld.PawnUtility`) → ③ `recipient.Spawned` → ④ `SocialInteractionUtility.IsGoodPositionForInteraction` = **`IntVec3.InHorDistOf(other, 6f)` — EUCLIDEAN (dx²+dz²≤36), a radius not a square** — plus `GenSight.LineOfSight(start, end, map, skipFirstCell: true)` → ⑤ `CanInitiateInteraction` (Talking capacity + Awake + !IsBurning) / `CanReceiveInteraction` (Awake + !IsBurning).
- `GenSight.LineOfSight` walks the line cell-by-cell; a cell blocks iff it holds a **BUILDING** with `FillCategory.Full`. `GenGrid.CanBeSeenOver(Building)`: `fillPercent < 0.01 → None (see-through)`; open doors see-through, closed doors block.
- Core ThingDefs (verified in `Data\Core\Defs\ThingDefs_Buildings`): Bed / DiningChair / Table2x4c / Shelf declare **no** `fillPercent` → default 0 → `FillCategory.None` → **see-through**. Only walls and closed doors are Full-fill.

**Verdict: the user's intuition is correct AND vanilla already behaves that way — furniture does not block interaction LOS. Nothing to implement on the LOS side.** (The earlier assistant claim "furniture kills line of sight even at close range" was an unverified guess and is retracted.)

### 18.2 What WAS implemented: drop-gate decomposition (logging only)

A flat "(distance/LOS/cooldown)" tag made drops unattributable (8+ consecutive with no diagnosis). `ConsumeSocialDecisions` now appends `DescribeDropGate(initiator, target, def)` to each drop line — a diagnostic that mirrors the decompiled gate chain and reports the FIRST failing sub-check: `cooldown (120 ticks since last interaction)` | `distance>6cells` | `LOS blocked (wall/closed door between)` | `initiator state (awake/talking/burning)` | `recipient state (awake/burning)` | carrying exception skips position checks | `all sub-checks passed (vanilla gate flipped between check and consume)`. Wrapped in try/catch → a bad diagnostic can never break the consume loop. **Behavior is 100% unchanged**: `CanInteractNowWith` remains the sole gate; this only explains its verdict.

### 18.3 Build / test record

- Product build vs real DLLs: **0 warnings, 0 errors**. Harness **252/252**, stable across consecutive runs (246 + 6 new checks in `Test_SocialConsumePaths`: cooldown/distance/LOS/recipient-state attribution, carrying-skips-position witness, furniture-does-not-block-LOS regression check).
- Stub fidelity updates: `IntVec3.InHorDistOf` (real Euclidean body), `GenSight.LineOfSight` 4th param renamed to real `skipFirstCell`, new stubs `SocialInteractionUtility` + `PawnUtility.IsCarryingPawn` extension + `InteractedTooRecentlyToInteract`, `TestRig` sub-check toggles, `Log.Messages` recorder.
- Deployed 2026-11-14 ~14:05 local with the game CLOSED (user confirmed quit): robocopy exit **1**; staged == live SHA256 **`FF9BDE47DBA8A5499C9FFAC201F1F627D03A7D240E7A9A2113AA92973CA7164F`**.
- Pending in-game: next H4 cycle — each `[dropped]` line now carries the specific failing sub-check; inside a 3×3 room distance (≤~2.8 cells) and LOS (no walls between pawns) cannot fail, so any remaining drop will name `cooldown`, an `initiator/recipient state` issue, or reveal that pawns had wandered out of range by consume time.

## 19. Local Ollama compatibility verification — game PC / RX 6800 (2026-11-15)

### 19.1 Setup under test
Ollama (user-reported v0.40.x) on the game PC, `qwen2.5:3b` (2.2 GB) installed. `ollama ps`: **PROCESSOR 100% GPU** — Vulkan backend active on the RX 6800, CONTEXT 4096 (comfortably above our largest prompt: L2 ~1.3k tok in + 150 out).

### 19.2 Wire-format test (our EXACT payload shapes against `http://127.0.0.1:11434/v1/chat/completions`)
Built byte-equivalent payloads from the deployed client (`BuildChatCompletionsJson`/`BuildBody` + `GbnfSchemas`, temp 0.65 / top_p 0.9 / min_p 0.08) and POSTed via curl:

| Layer | Result | Latency | Notes |
|---|---|---|---|
| L1 (object-form `json_schema`, int enum + strings) | HTTP 200, schema-conformant | first call 8.6 s (grammar compile), **steady-state 0.52 s** | second call showed `cached_tokens: 335/336` — Ollama DOES prefix-cache across requests in this version; our static system prefix is byte-identical across pawns, so most of every L1 prompt should be cached |
| L2 (array-of-objects + string enum) | HTTP 200, conformant | 0.49 s | returned a valid empty `decisions` array ("quiet cycle") — consistent with the known quiet bias (H4), not a compat issue |
| L3 (nested objects + number min/max) | HTTP 200, structurally conformant | 2.58 s (291 tok gen ≈ 110 tok/s) | **Ollama does NOT enforce `minimum`/`maximum` on numbers** — response carried `respect_delta: 0.10` against max 0.05. Harmless by design: `ColonistMindState.ApplyLayer3Result` clamps every delta ±0.05 / trait drift ±0.02 at apply time (Spec §7.4) |

### 19.3 Verdict
**Zero code changes required.** All three layers can point at `http://127.0.0.1:11434` with model `qwen2.5:3b`; the client reads URL/model/timeout from the live settings object per request, so in-game settings edits apply on the next dispatch (no restart). Steady-state L1 latency ~0.5 s vs ~2.5–3.8 s + queue on the homelab CPU server — a ~7× improvement on the inference term of the perceived-latency budget.

### 19.4 Operational caveats
- Ollama default `keep_alive` ≈ 5 min idle → model unloads; next dispatch pays reload + first grammar compile (~8–15 s, within the 30 s timeout + one retry). Recommended: system env var `OLLAMA_KEEP_ALIVE=-1` (or `1h`) + Ollama restart — VRAM headroom is ample (2.2 GB of 16 GB, RimWorld ~4 GB).
- L3 numeric bounds are enforced client-side only (see table) — if a future server is used that DOES enforce them, behavior is unchanged either way.

## 20. Work-affordance job resolution — "pawns don't do what Layer 1 says" (2026-11-15)

### 20.1 Symptom (user-reported)
After the Ollama switch: L1 lines kept arriving with confident impulses ("let's get that caribou ready for the fire") but pawns did not act on them. Same session, the model even commented on its own prompt: `"These options are starting to feel... repetitive."`

### 20.2 Root cause (code-verified)
`AffordanceScanner.AddWorkAffordances` emitted every work option with **`jobDef = null`** (a documented Phase-1 TODO). Downstream, `QueuedDesire.IsValidAndReservable` rejects `jobDef == null` unconditionally, and `JobGiver_AIColonistDesire` then discards the desire silently ("stale target: discard silently and fall through to vanilla AI"). Net effect: the ONLY executable options were eat (hunger <0.4), sleep (rest <0.4) and Wait skeletons — every work impulse the LLM picked was unexecutable by construction, so pawns "didn't do what Layer 1 says", and the model was literally being offered a wall of never-executable "Work on the X nearby." entries (hence the "repetitive options" self-awareness line).

### 20.3 Fix (decompile-verified against this install)
Each work target is now resolved at scan time through its WorkGiver's own factory: `Job job = scanner.JobOnThing(pawn, t)`; options carry `job.def` + the job's own targets (falling back to the scanned thing so the consume-time re-check always has a Thing). Safety evidence:
- `RimWorld.WorkGiver_Scanner.JobOnThing(Pawn, Thing, bool forced = false)` is public virtual — decompiled 1.6 base + `WorkGiver_Slaughter` (`= JobMaker.MakeJob(JobDefOf.Slaughter, t)`, pure).
- `Verse.JobMaker` contains **zero reservation calls** (full class decompile) → calling it at scan time has no side effects; the constructed Job is read for def/targets and discarded.
- Schedule gating already exists upstream: `Pawn_WorkSettings.CacheWorkGiversInOrder` only keeps work types with `GetPriority(w) > 0`, so `WorkGiversInOrderNormal` never contains unscheduled work — we cannot offer work against the player's settings.
- **1.6 rename caught by the compiler:** `Verse.AI.Job` stores its def as `public JobDef def` (not pre-1.5 `jobDef`) — first build failed CS1061 on `job.jobDef`; fixed and stub updated to match.

### 20.4 Build / test record
- Product build vs real DLLs: **0 warnings, 0 errors**. Harness **255/255**, stable across consecutive runs (252 + 3 new checks: scanner offers resolved work job; null-`JobOnThing` giver contributes no option; resolved work desire passes `IsValidAndReservable`; plus the end-to-end JobGiver witness that a deposited WORK impulse is consumed as a real Job instead of silently discarded).
- Stub fidelity updates: `Job.def` field (1.6 rename), faithful `JobMaker.MakeJob` (populates def/targets), scriptable `TestRig.WorkJobOnThing` with vanilla-mimicking default.
- Deployed 2026-11-15 local with the game CLOSED (user confirmed quit): robocopy exit **1**; staged == live SHA256 **`22F6973E86CB0F0960CDCCC0376F9E787666B9E6FEBBC46D940E826B3BA2E041`**.
- Pending in-game: next session — L1 work impulses (mining/hunting/farming/etc. targets from the pawn's scheduled WorkGivers) must now become real jobs; watch for `inner_voice` lines followed by the pawn actually walking to and working the target. The "repetitive options" self-awareness line should also decrease as option lists become varied and executable.

### 20.5 Incident: vanilla `JobOnThing` NREs from our scanner (live, 2026-11-15 evening H4 run)
The first in-game session on build `22F6973E…` produced repeated dev-log exceptions:

```
Exception in AIColonists.ThinkTree.ThinkNode_ConditionalAIColonist TryIssueJobPackage: System.NullReferenceException
  at RimWorld.WorkGiver_HaulMechToCharger.JobOnThing (Verse.Pawn pawn, Verse.Thing t, System.Boolean forced)
  at AIColonists.ThinkTree.AffordanceScanner.AddWorkAffordances (...) AffordanceScanner.cs:143
  at AIColonists.ThinkTree.AffordanceScanner.Scan (Verse.Pawn pawn) ...
```

**Root cause:** a handful of vanilla `JobOnThing` implementations assume preconditions that don't hold when we call them from the scanner's context — `WorkGiver_HaulMechToCharger.JobOnThing` and `WorkGiver_Teach.JobOnThing` both NRE (e.g. null mech-battery / teaching-context derefs). Our "pure factory" evidence held for the givers we decompiled (`Slaughter`, `JobMaker`) but not universally — vanilla's own call sites never invoke these without their preconditions, so they are not defensive.

**Blast radius (decompile-verified):** uncaught, the exception propagates to `ThinkNode_Priority.TryIssueJobPackage`'s per-child try/catch, which logs `"Exception in " + GetType() + " TryIssueJobPackage"` and moves on — so **vanilla flow, H4/Layer 2, and other pawns were unaffected**. But the throw happens *inside* our `ThinkNode_JobGiver` child of `ThinkNode_ConditionalAIColonist`, so for any pawn whose scheduled work list contains a throwing giver (mech hauling / teaching in this colony), **our branch dies on every think evaluation → that pawn's Layer 1 is dead** (L1 lines kept appearing only for unaffected pawns — consistent with the log).

**Fix:** per-candidate try/catch around `scanner.JobOnThing(pawn, t)` in `AddWorkAffordances` — skip the candidate, `Log.ErrorOnce(…, 4813)` once per session. Defensive rule honored: vanilla flow never breaks, error logged once, scan continues with all other options intact.

**Build / test record:** product build vs real DLLs **0/0** (first attempt failed CS0246 — `using System;` missing in AffordanceScanner.cs). Harness **257/257** ×2 (2 new checks: throwing `JobOnThing` → candidate skipped + scan completes; unrelated options still offered).
- **Deployed 2026-11-15** (game closed): DLL SHA256 `19D345EE06F0849F9B6078D71FB2FB07760CD2E17EC9B0CE98794D1D19BCBE12` (rebuilt with `DebugType=none` — strips the PDB build-path record; functionally identical). robocopy `/MIR /XF .gitignore /XD Source Tests .git /R:1 /W:1`, live == staged hash verified.

## 21. Prisoner/visitor LLM exclusion + Layer 1 log naming (2026-11-15)

### 21.1 Trigger
User noticed "Human####" names in the dev log alongside named colonists, and asked to remove the LLM from prisoners. Two findings:
1. **Name mystery solved:** `Thing.ThingID` in 1.6 = `def.defName + thingIDNumber` (decompiled) → every human pawn's ID is "Human####". Layer 1 log lines printed `snapshot.PawnId` (= ThingID), while the thinks:/mote lines print LabelCap — so Cook appears as both "Cook" and "Human1006" depending on the line. Same pawn, not an extra colonist (proven by identical inner_voice text under both names in one session).
2. **Prisoner leak:** `Pawn_Thinker.MainThinkTree => RaceProps.thinkTreeMain` (decompiled) — the main tree is per-RACE, so prisoners share `MainColonistBehaviorCore` and reach our gate node; the old gate passed for them → full Layer 1 evaluations (~40k tokens/pawn-day) plus potential AI-issued jobs to a pawn the player doesn't control. L2/L3 already iterate `FreeColonistsSpawned` only (no leak there).

### 21.2 Fix
- `ThinkNode_ConditionalAIColonist.Satisfied`: added `if (pawn.Faction != RimWorld.Faction.OfPlayer || pawn.IsPrisoner) return false;` (replaces the Phase-1 TODO). **1.6 API note:** the player faction is `RimWorld.Faction.OfPlayer` — `Verse.Find` has no FactionPlayer member in this build and `Faction` lives in the RIMWORLD namespace (compiler caught both on first build; stubs updated to match, incl. moving the Faction stub into `namespace RimWorld`).
- Layer 1 log lines now print `LabelCap (ThingID)`: snapshot captures `PawnName` on the main thread with a try/catch fallback to the ID (LabelCap can throw on half-loaded pawns during save load — live-observed 2026-11-14; ErrorOnce 4814).
- Slaves remain LLM-eligible (player-faction laborers) — open policy question recorded in OPTIONAL-IMPROVEMENTS.md #9.

### 21.3 Build / test record
- Product build vs real DLLs: **0 warnings, 0 errors** (first attempt failed CS0117 on `Find.FactionPlayer` — does not exist in 1.6). Harness **260/260** ×2 (3 new checks: prisoner excluded from gate; non-player-faction pawn excluded; snapshot carries display name + stable ThingID key).
- **Deployed 2026-11-15** (game closed), same build as §20.5 — SHA256 `19D345EE…` verified live == staged; supersedes the previously deployed work-affordance build `22F6973E…`.

## 22. Phase 2 complete + AI-first job priority (backlog #8) (2026-11-15)

### 22.1 Trigger
User ran a live session on the §20/§21 build and reported: *"the LLM had little effect on the game — the only thing Layer 1 actually directed was getting the pawn to eat; most instances where Layer 1 showed the pawn wanting a task, the pawn did another completely different job (presumably vanilla AI is doing most of the work)."*

### 22.2 Root cause (decompile-verified against the 1.6 tree XMLs)
Our branch sits inside the `MainColonistBehaviorCore` subtree, **above** the PrioritySorter (GetFood/GetRest/joy/reload/Work) but **below** QueuedJob/Drafted/emergency — so structurally AI wins whenever a valid desire exists at think time. The observed behavior came from desires being *absent or stale* at think time:
1. `TryGiveJob` returned **null** whenever the buffer was empty → vanilla work started immediately, and the LLM response (0.5–7 s later) landed in the buffer for the *next* job transition → [vanilla]→[AI]→[vanilla] alternation.
2. `TryPopDesire` removed the desire even when validation failed — a stale work target was permanently discarded, guaranteeing another vanilla run.
Only eat/rest impulses "worked" because those needs re-fire continuously (a stale eat desire is immediately replaced by a fresh one at the next scan), while a one-shot work target goes stale fast.

### 22.3 Fix — AI-first job priority (backlog #8, no vanilla patching)
While a request is **in flight** and the pipeline is healthy, `JobGiver_AIColonistDesire` issues a short `JobDefOf.Wait` hold job (`HoldPollTicks = 450` ≈ 7.5 s at 1× — covers the full dispatch budget: 3000 ms timeout + retry) instead of returning null. The pawn re-thinks when the wait expires, by which point a healthy response has landed and is consumed as the real job.
- New setting **`AIFirstJobPriority`** (default **true**, settings checkbox). OFF = blend mode (old behavior).
- Enqueue-first ordering: the JobGiver snapshots + dispatches *before* checking the hold, so the hold waits for THIS response, not a future one.
- Circuit breakers (per-pawn, in `PawnCallState`): `consecutiveL1Failures >= 2` → no hold; `staleDrops >= 3` → no hold + counter reset (one vanilla cycle per three stale drops); physiological floor — `rest < 0.1` is never held (vanilla GetRest takes over). `NoteDesireExecuted` resets both breakers.
- Safety unchanged: player orders, drafting and emergency survival sit ABOVE us in the tree and still win natively; the gate node still excludes prisoners/non-player pawns.

### 22.4 Phase 2 completion (Spec §9: "Differentiation, Sensory Filter & Prefix Caching")
- **Real per-pawn identity prefix** (`PrefixPromptBuilder` + `SensoryFramingBuilder.CaptureIdentity`, main thread): name / age (`pawn.ageTracker.AgeBiologicalYears`) / colony name (`(Find.CurrentMap?.Parent as Planet.Settlement)?.Name`) / trait line (label + degree descriptor, ≤4) / top-2 skill **names only** (levels excluded so the cached prefix stays byte-stable) / semantic memories / longing. Byte-identical across captures for an unchanged pawn → KV-cache friendly; changes only when Layer 3 drift crosses a threshold or on rare identity events.
- **Intellect → cadence + colony horizon** via `SkillDefOf.Intellectual` (exists in 1.6 — the learning-system stat; there is NO "Intellect" field, verified against the real DLL): tiers <5 blunt/no-horizon, 5–9 detail-pausing/loose awareness, ≥10 layered/wider-state. Episodic line count also intellect-gated (1/2/4).
- **Mood → entropy wired** (`MoodEntropyMapper`): `MoodTier(float)` bands from `Need_Mood.CurLevelPercentage` (≥0.75 ecstatic / ≥0.6 happy / ≥0.45 content / ≥0.3 stressed / ≥0.15 depressed / <0.15 furious; out-of-range → content). The dispatch worker never touches the pawn — the snapshot carries `MoodPercentage`, and dispatch sets both `temperature` (base + tier offset, clamped [0.50, 0.85]) and `top_p` per Spec §3.2's MOOD_ENTROPY_MAP.
- **Trait-weight seeding** (`ColonistMindState.SeedTraitWeightsFromPawn`): key = lower-cased trait label, weight = 0.5 + 0.15·degree clamped [0.05, 0.95]; idempotent (only while the map is empty) so Layer 3 drift is never overwritten; lazy re-seed on snapshot capture self-heals pre-Phase-2 saves. Values/aversions lines = weights ≥0.55 / ≤0.35 — this is the feedback loop that makes slow personality drift visible in prompts, not just UI bars.
- **Sensory filter enrichments**: negative mood thoughts → first-person grudge prose (`Need_Mood.thoughts.memories.Memories` → `Thought_Memory.moodOffset/otherPawn/LabelCap`, ≤2 lines); nearby pawns colored by relationship vector (read-only `GetRelationship` — no side-effect creation; thresholds set ABOVE fresh-vector defaults so un-drifted relationships stay uncolored); verb-matched work phrasing in the scanner (`Mine the steel vein…` / `Haul the wooden box to storage…` etc., mapped by giver class name).
- New ErrorOnce **4815** (identity/mood-thought framing threw — degrades to empty lines, never kills the think branch).

### 22.5 Build / test record
- 1.6 API verification (ilspycmd against `Assembly-CSharp.dll` 1.6.9676): `Job(JobDef)` ctor + `expiryInterval`; `pawn.ageTracker.AgeBiologicalYears`; **`pawn.story.traits.allTraits`** (the TraitSet lives on the STORY tracker — Verse.Pawn has no direct traits field; first build failed CS1061 and caught it); `Trait.LabelCap/.Degree/.Suppressed`; `Pawn_SkillTracker.skills` + `GetSkill(SkillDef)`; `SkillRecord.def/.levelInt`; `SkillDefOf.Intellectual`; `Find.CurrentMap` → `Map.Parent` (`RimWorld.Planet.WorldObject`) → `Settlement.Name`; mood chain `Need_Mood.thoughts.memories.Memories`.
- Product build vs real DLLs: **0 warnings, 0 errors** (first attempt failed on the mapper overloads — a string tier passed where a float percentage was expected — and on the missing `traits` field; both fixed). Harness **305/305** (45 new checks: mood bands/clamps/top_p, cadence+horizon tiers, trait-seeding idempotency + clamps, identity prefix content + byte-stability + drift-threshold update + grudge prose, `ShouldHoldPawn` in-flight/failure/stale/rest-floor/setting-off, JobGiver hold-job flow with Wait def + 450-tick expiry).
- **Deployed 2026-11-15 ~22:09** (game closed): live == staged SHA256 `48049F1E3E87B4BE53FCBF977761CA9A2B33ACF38061C0F334E340F924BF58BF`; supersedes `19D345EE…`.

### 22.6 Ollama E2E probe (inference-side T4) + diagnostic log lines (2026-11-15, build `40E17A09…`)
User asked which tests could be automated. Two additions:
- **`Tools/PromptProbe/`** — standalone tool that links the real mod sources + stubs, builds the EXACT Layer 1 payload via `LlamaServerClient.BuildChatCompletionsJson`, POSTs it to a live endpoint (default `http://127.0.0.1:11434/v1/chat/completions` qwen2.5:3b), and parses with the real `ParseLayer1`. Result (6/6 runs, two personas facing identical affordances):
  - **All responses schema-valid**, every `impulse_id` in range — Ollama accepts the object-form `json_schema` payload and qwen2.5:3b complies.
  - **Latency:** cold call **8284 ms** (model load) vs warm repeats **413–668 ms** (~17× — prefix caching working, consistent with §19's ~0.5 s/call). The cold call EXCEEDS the 3000 ms dispatch budget → concrete evidence that `OLLAMA_KEEP_ALIVE=-1` (still unconfirmed by user) matters: a resident model keeps every call warm.
  - **Differentiation:** mining-focused stoic Alice (content, temp 0.60) chose mine/eat with analytic multi-clause voices ("Steel is a good material for tools or repairs"); quick-tempered Bob (furious, temp 0.85/top_p 0.95) chose eat with short impulsive mutters ("Time to eat."). Same affordances, different pawns → different decisions and tone.
  - Probe also exposed a prompt-prose wart: the skills line included the Intellectual meta-stat ("known for your mining and intellectual") — now excluded from the identity line (it drives cadence/horizon separately).
- **T4 is now grep-verifiable:** `JobGiver_AIColonistDesire` logs `[job started] "<exact sensory phrasing>"` when a desire becomes a job (`QueuedDesire` now carries the phrase) and `[hold] waiting for LLM decision (450 ticks)` when the hold engages. "Layer 1 said X" (dispatch line) correlates with "pawn did Y" ([job started] line) from Player.log alone; harness **307/307** (+2 log-line checks).
- **Deployed 2026-11-15 ~23:40** (game closed): live == staged SHA256 `40E17A09D9E9D18C0B4880DD710657DED71CBE252986520F09C9CA21CF96C275`; supersedes `48049F1E…`.

### 22.7 Ollama keep-alive: one-shot cold-start warmup (2026-11-15, build `8BC01297…`)
User requirement (replacing the §22.6 "set `OLLAMA_KEEP_ALIVE=-1`" conclusion): **do not pin the model in VRAM non-stop** — load it during gameplay, accept a one-time long startup, and let it go when done. That is exactly Ollama's DEFAULT behavior: 5-minute keep-alive, reset by every request. Layer 1 dispatches land every ≤30 s while unpaused, so continuous play keeps the model hot; it auto-unloads ~5 min after pause/quit (verified via `/api/ps`: empty `models` list after >5 min idle). No environment variable needed — and `-1` would have violated the requirement by pinning 2 GB of VRAM forever.

The catch, found empirically: **cancelling a cold call MID-LOAD makes Ollama ABORT the load.** Test (`cancel-load-test.ps1`): model unloaded → POST with a 3 s client timeout (the mod's `RequestTimeoutMs`) → client cancelled at 3082 ms → watched `/api/ps` for 20 s: **unloaded the entire window** (a surviving load would have finished by ~t+6 s). Without intervention, every short-timeout dispatch at game start would start its own model load and cancel it at 3 s — the model could never finish loading during active play.

Fix = one-shot warmup per map session:
- `ILlamaBridge.WarmUpAsync()` → `LlamaServerClient`: minimal payload (`"Warm up."`, `max_tokens=1`), **90 s timeout** (cold load measured ~8.3 s on this box — 10× margin; it runs once per session on a worker thread, so generosity is free and shortness re-triggers the abort). Serialized through the same static `SendGate` as real dispatches, so it can never collide with them at the transport level (the Mono one-flight constraint). Success logs `[AIColonists] inference backend warm — model loaded, dispatches run at full speed`; unreachable server logs one quiet skip line (normal pre-game state) and returns false.
- Trigger: `AIColonistGameComponent.MaybeRequestWarmUp()`, called from `EnqueueAsyncLayer1` BEFORE the dispatch's `Task.Run` so it wins the SendGate race; per-map-session flag (`warmupRequested`); only reachable with `EnableAI` on, so a disabled mod never burns an 8 s load.
- E2E against live Ollama mirroring the in-game flow (`warmup-e2e-test.ps1`): initial state **unloaded** → warmup call (90 s timeout) completed in **6866 ms** (cold load absorbed) → two dispatch-shaped calls immediately after: **113 ms / 61 ms** (both far inside the 3000 ms budget) → final `/api/ps`: **LOADED, 2.01 GB VRAM**, resident for the default 5-min keep-alive.
- Harness: `FakeLlamaBridge.warmups` counter + `Test_WarmUpColdStart` (no warmup before first dispatch; first Layer 1 dispatch fires it; second dispatch does NOT re-fire — one-shot per map session). The server-down resilience poll window was widened 5 s → 15 s: on this machine a connection to closed port 9 is refused after ~2.1 s (not instant RST), and the warmup now serializes ahead of the dispatch through SendGate (warmup ~2.5 s + attempt 0 ~2.1 s + 500 ms retry delay + attempt 1 ~2.1 s ≈ 7 s worst case); a true wedge never clears, so the assertion still means what it said. Harness **311/311**; build 0 errors.
- **Deployed 2026-11-15 ~23:44** (game closed): live == staged SHA256 `8BC012974B9EA12CCBE0FEBD8CF97A9F953D3BC378CE0842C3B750EB6BD4E9C5`; supersedes `40E17A09…`.

In-game expectation: once per map session, Player.log gains the warm line ~8–9 s after game start when the model was cold (or a quiet skip line if Ollama isn't up yet); pawns still briefly hold (~0.5–7.5 s) while individual decisions are in flight; the model stays resident for as long as play continues and unloads itself ~5 min after pause/quit.

## 23. First full T4 session — decision→execution grading + work-giver gate fix (2026-11-16, build `75848197…`)
User played ~10 min (Onnana Nation colony; Cook/Manager Human1006, Aiglamene/Logger Human1000, Hercules/Chief Human1003) and brought Player.log. 96 `[AIColonists]` lines total.

### 23.1 T4 verdict: PASS (with one broken option class — fixed in this build)
- **Warmup confirmed in-game:** `inference backend warm — model loaded, dispatches run at full speed` at game start (§22.7); **zero cold-call timeouts** across the whole session.
- **Decision→execution: 21/22 executed.** Every Layer 1 decision (Cook 10, Aiglamene 8, Hercules 4) was followed by a matching `[job started]` line — except Hercules's final decision, still queued when the log ended. Executed jobs: eat ×5, rest ×3, caribou work ×3, work-on-human ×10. The inner voices matched the executed jobs (hungry → eat, tired → bed, "caribou are easier to catch" → work on caribou).
- **Differentiation confirmed in-game:** Cook's voices long and reflective ("Each warm chat with Hercules is a reminder of the importance of maintaining connections…"), Aiglamene terse and practical ("Time to fill my belly."), Hercules gruff and short ("Hungry... might as well grab that meal."). Phase 2 is visibly working.
- **Layer 2:** 2 warm_chat decisions, both correctly dropped at the gate with decomposed reasons (`recipient state (awake/burning)`, `distance>6cells`) — re-roll path healthy. **Layer 3:** 4 consolidations applied (Cook ×2, Aiglamene, Hercules).

### 23.2 Bugs A+B (one root cause): work options skipped vanilla's HasJobOnThing gate
Live exceptions in the session:
- `Exception in CheckCurrentToilEndOrFail for pawn Aiglamene driver=JobDriver_Milk … driver.job=(Milk … A = Thing_Human1006 Giver = JobGiver_AIColonistDesire)` + NRE inside a `JobDriver_GatherAnimalBodyResources` toil closure — **a Milk job targeting human colonist Cook**.
- `Exception ticking Aiglamene: IndexOutOfRangeException` ×5, stack `JobDriver_Lessongiving.TryMakePreToilReservations → ReservationUtility.ReserveSittableOrSpot → GridsUtility.GetEdifice → EdificeGrid.get_Item` — Lessongiving (teach) jobs failing pre-toil reservation.

Decompiled 1.6 (source of truth):
- `WorkGiver_GatherAnimalBodyResources.PotentialWorkThingsGlobal` returns **ALL faction pawns** (`SpawnedPawnsInFaction(pawn.Faction)` — humans included); the animal check lives ONLY in `HasJobOnThing`/`ShouldSkip`. Its `JobOnThing` is a bare `MakeJob(JobDef, t)` with no animal check.
- `WorkGiver_Teach.PotentialWorkThingsGlobal` returns **every player-faction pawn**; `HasJobOnThing` requires `SchoolUtility.NeedsTeacher(target)` — Biotech school children only (user-confirmed: in vanilla only children can be taught); and its `JobOnThing` builds `Lessongiving(targetA = target.CurJob.GetTarget(A), targetB = target)` while **mutating the target pawn's current job** (`CurJob.SetTarget(B, …)`) — not a pure factory as the §20 comment assumed.
- So the §20 scanner (which called `JobOnThing` directly) offered: Milk(Cook) → vanilla NRE in the milk driver; and Lessongiving whose "desk" was whatever Cook's current job targeted (a bed, a meal, nothing) → `DeskSpotTeacher(bed)` computed an out-of-bounds sitting cell → IndexOutOfRange. Aiglamene's 5 work-on-human jobs all crashed at start (Milk ×1 + Lessongiving ×4 attributed to our jobs; one further Lessongiving exception can't be attributed from the stack alone — it doesn't show the think node). Cook's 5 work-on-human jobs started without exception (job def not attributable in this build — which is exactly what motivated the §23.5 suffix).

**Fix:** honor vanilla's two-stage contract — `AddWorkAffordances` now calls `scanner.HasJobOnThing(pawn, t)` (try/catch, ErrorOnce **4816**) BEFORE `JobOnThing`. After the fix, human-target work options only appear when genuinely valid (patient-care givers for downed/sick colonists; teach only in a real Biotech school). Harness: scriptable `TestRig.WorkHasJobOnThing` + 3 new scan checks.

### 23.3 Bug C: worker-thread logging raced the in-game log window
Live: `Exception filling window for LudeonTK.EditWindow_Log: InvalidOperationException: Collection was modified; enumeration operation may not execute` ×2 (+ cascading GUI clip error) — while the user had the in-game log window open. Decompiled `Verse.Log`: Message/Warning/Error all lock around **ENQUEUE** only; `LudeonTK.EditWindow_Log` is an INTERNAL class inside Assembly-CSharp.dll (RimWorld's own log window, not a third-party mod) and enumerates the queue WITHOUT that lock → any Log.Message from a worker thread races an open window. Our Layer 1 / warmup / transport lines logged from Task threads every ~30 s per pawn.

**Fix:** `MainThreadLog` (Core) — ConcurrentQueue drained at the TOP of `MapComponentTick` (before the pause/AI-off early returns, so lines always flush); all 9 worker-thread log sites converted (4 in AIColonistGameComponent, 5 in LlamaServerClient). Main-thread sites unchanged. Harness: `Test_MainThreadLog` (deferred until drain; ErrorOnce dedupe at drain time; empty drain no-op) + stub `Log` now records Errors with vanilla ErrorOnce semantics.

### 23.4 Prompt fix: name-vs-action confusion
User observation: the model conflated the colonist named Cook with the act of cooking in some inner voices. Root cause: social options lower-cased names ("Go talk to cook.") — reads as a verb. **Fix:** keep `LabelCap` capitalized in social phrasing (with a save-load guard per the ErrorOnce 4814 precedent) + static system-prompt line "Names in your options are people's names, never occupations or actions (Cook is a person, not cooking)." Probe re-run: 6/6 schema-valid, differentiation intact.

### 23.5 `[job started]` now carries the resolved JobDef
Two different work givers can share the generic phrasing ("Work on the human nearby" was Milk for one pawn and something else for another), so the line now reads `[job started] "<phrasing>" (<JobDef defName>)` — next session's grading is definitive.

### 23.6 Not our bug (recorded for completeness)
- `Exception in CheckCurrentToilEndOrFail for pawn Cook driver=JobDriver_TakeToBed … Giver = ThinkNode_QueuedJob` + InvalidCastException in `JobDriver_TakeToBed.get_DropBed()` — a PLAYER-QUEUED rescue job (Giver is the queue node, not ours); vanilla edge case during the Hercules downed/rescue sequence.

### 23.7 Build / test record
- Product build vs real DLLs: **0 warnings, 0 errors** (the `HasJobOnThing(Pawn, Thing, bool)` signature was confirmed by compiling against the real 1.6 API). Harness **322/322** (+11: gate refused ×2, throwing-gate skip ×1, MainThreadLog ×8).
- Probe re-run with the new system line: 6/6 schema-valid (cold call 8852 ms — model had auto-unloaded per the default keep-alive; warm 403–583 ms).
- **Deployed 2026-11-16** (game closed): live == staged SHA256 `75848197CAA71569419056EDA9AAFD65D47FDAFDA554F5BC79C79FBC29C4E184`; supersedes `8BC01297…`.

### 23.8 Post-session investigation: Cook's "work on human" jobs = warden work on the prisoner (hypothesis confirmed)
- User hypothesis (2026-11-16): Cook's clean human-work jobs were directed at the colony's prisoner, not a name-confusion artifact. **Confirmed by save + def evidence:**
  - Save `Onnana Nation.rws`: Fir "Axe" Reekad (Human63250, SpaceRefugee) is on map 0 with `<guestStatus>Prisoner</guestStatus>`, hostFaction = player faction, interactionMode AttemptRecruit. Cook's inner voice names "Axe" during exactly those decisions (Player.log L99: *"Perhaps a light conversation with Axe could be a good start"*).
  - WorkSettings decode: the save stores 21 positional priority values = Core's 20 WorkTypeDefs in file order + Biotech Childcare. Mapping **behaviorally validated**: Aiglamene has Handling=3 and Childcare=3 — exactly the work types that produced her milk/teach jobs in T4 — with Warden=0, and she never did a human-warden job. Cook decodes to **Warden 3, Doctor 3, Childcare 3** (Handling/Construction/Mining/Art off). An earlier "Cook Warden=0" note was an off-by-one miscount of the value list.
  - Core's Warden givers (1.6) are all "work on human" jobs targeting prisoners: `ChatWithPrisoner` ("chat with prisoners"), `FeedPrisoner`, `DeliverFoodToPrisoner`, `TakePrisonerToBed`, `ReleasePrisoner`, `DoExecution`.
  - Caveat: T4 predates defName attribution, so per-job giver identity (chat vs feed) is inferential; from the next session onward `[job started] (…) (<defName>)` makes this grep-provable. The name-confusion fix (§23.4) still stands as a separate real issue — it just wasn't the cause of these jobs.

### 23.9 Descriptive person-work phrasing (2026-11-16, build `047B9960.`)
- T4's log showed **thirteen** options reading "Work on the human nearby." — useless to the LLM and ungradeable. Root cause: the verb map in `AffordanceScanner.WorkPhrase` had ~5 entries (mine/haul/clean/build/slaughter) and every person-targeting giver fell through.
- Fix: new `PersonWorkPhrase(cls, what, target)` — when the scanned Thing is a Pawn, phrasing is keyed on the giver's **class name** (`GetType().Name`, no def access → half-load-safe), with the target's `LabelCap` where safe (try/catch → unnamed fallback; same rule as §23.4). Verified class inventory (1.6 Core+Biotech WorkGiverDefs + decompiles, §23.8 recon):
  - **Warden:** `WorkGiver_Warden_Chat/DeliverFood/Feed/ReleasePrisoner/TakeToBed/DoExecution`, `WorkGiver_Strip` (prisoner or corpse), `WorkGiver_ExecuteGuiltyColonist` (BasicWorker — targets a colonist, phrased as such); Biotech hemogen delivery.
  - **Doctor:** `TendOther_Humanlike`/`TendOtherUrgent`, `FeedPatient`, `RescueDowned`, `TakeToBedToOperate`, `VisitSickPawn`, `DoBill` (medical bills target the patient pawn via `billGiversAllHumanlikes` → "Operate on X."); **`WorkGiver_TendSelf` yields the actor itself** → actor-aware line ("Tend to your own injuries.") naming no one; Urgent/Emergency suffixes both detected.
  - **Childcare (Biotech):** `Teach`, `BreastfeedCarryToMom` (checked BEFORE `Breastfeed` — the class name contains both substrings), `BottleFeedBaby`, `PlayWithBaby`, `Breastfeed`.
  - **Animals** keep race-label phrasing: `Milk` ("Milk the caribou nearby."), `FeedPatient` reused by DoctorFeedAnimals, `TendOther_Animal`.
  - No entry needed for `PatientGoToBed*` / `BringBabyToSafety` — decompiled as plain `WorkGiver` (NonScan), they never reach our scanner. Unknown mod givers targeting a person → "Work with the person nearby." (still better than "work on the human").
- Tests: new `Tests/WorkGiverFakes.cs` — 20 empty subclasses carrying **verbatim vanilla class names** over the stub `WorkGiver_Scanner` (scriptable `PotentialWorkThingsGlobal`/`HasJobOnThing`/`JobOnThing` via TestRig); 26 new checks in `Test_AffordanceScan` covering every branch incl. self-tending, animal targets, half-loaded throwing-LabelCap target, and the unknown-giver fallback.
- Build / test record: product build vs real DLLs **0 warnings, 0 errors**; harness **348/348** (was 322); probe re-run PASS (all responses parsed, impulse_ids in range).
- **Deployed 2026-11-16** (game closed): live == staged SHA256 `047B99608C195D0E4E6CC47F44F96C439C0C952C4BCFEF70C151FE176AC40B0A`; supersedes `75848197…`.
- Next-session expectation: `[job started]` lines for person work now read e.g. `"Chat with prisoner Axe." (ChatWithPrisoner)`; any remaining generic "Work on the human nearby." line identifies an unmapped mod giver to add.
