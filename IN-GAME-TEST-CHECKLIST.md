# In-game test checklist — AI-Integrated Colonists (RimWorld 1.6)

Goal: verify the mod loads cleanly, Harmony patches apply, and Layer 1 LLM calls flow end-to-end.
~30 minutes total. **AI is OFF by default** — nothing changes until you enable it in Mod Settings.

## A. Before launching RimWorld (5 min)

- [x] **A1.** Start the Layer 1 server on the LAN GPU node (`<gpu-node>`), port **8091**: ✅ Nov 9 build, Qwen2.5-1.5B-Instruct-Q4_K_M, CPU (`-ngl 0 -c 4096 --cache-reuse 256`); ~2.2–3.3 s per call with KV cache reuse (LCP slot selection).
- [x] **A2.** Verify from THIS machine (the game PC): ✅ health OK.

      ```powershell
      curl.exe http://<gpu-node>:8091/health
      ```

      Expect `{"status":"ok",...}`. If this fails, stop — the rest of the test is meaningless.

## B. First launch + log check (5 min)

- [x] **B1.** Launch RimWorld → Mod Manager: **"AI-Integrated Colonists"** appears and enables with
      no red dependency errors (Harmony is bundled in `Assemblies/` — no Workshop mod needed). ✅ Enabled alongside workshop mods `brrainz.harmony` + `void.charactereditor`, no conflict.
- [x] **B2.** Start the game (main menu is enough), then exit. ✅ Loaded existing Esbium colony (the loaded-save path was the primary test target).
- [x] **B3.** Open `<RimWorld install>/Player.log` (created on first launch): ✅ 2026-11-12 full-log sweep (84 lines) — all three conditions hold:
      - ✅ contains: `[AIColonists] Loaded (Harmony patches applied). Enable AI in Mod Settings...`
      - ❌ no line containing both "AIColonists" and "Exception"
      - ❌ no "patch no-match" / xpath errors mentioning `AIColonist_ThinkTreePatch.xml` or `AIColonistTabs.xml`

## C. Mod settings (2 min)

Mod → Settings → AI-Integrated Colonists:

- [x] **C1.** `EnableAI` = **true** ✅ applied via in-game Mod Settings UI; cfg confirmed on disk.
- [x] **C2.** `Layer1ServerUrl` = `http://<gpu-node>:8091` ✅
- [x] **C3.** `RequestTimeoutMs` = **30000** ← critical: the default 3000 is too tight for CPU inference
      (a call takes ~5 s). Layer 2/3 URLs can stay at their defaults — Phase 1 only exercises Layer 1. ✅

## D. Live behavior (10–15 min)

- [x] **D1.** New colony, 2–3 colonists, **unpaused**, speed 1×. ✅ Deviation: loaded the existing Esbium colony (not new) — the loaded-save path was the primary test target; unpaused at 1×. Dual-clock gate satisfied (`TicksGame` already huge on a loaded save).
- [x] **D2.** Within ~1–5 min, watch Player.log for: ✅ both line types observed — **9** `Layer 1:` diagnostics + **2** executed desires (`thinks:` lines) in one session; see TEST-RESULTS.md §9.2:
      - `[AIColonists] <pawnId> Layer 1: impulse_id=N of M, inner_voice="..."` — **every** Layer 1 decision (Phase 1 diagnostic)
      - `[AIColonists] <Name> thinks: ...` — a desire was validated and issued as a real job
        (most reliable when a colonist is hungry/tired or standing near another colonist — social affordance has no need threshold)
- [x] **D3.** Select a human colonist → pawn card: an **"AI Mind"** tab appears among the bottom tabs;
      clicking it shows the placeholder panel, no crash. (Animal pawns get no tab.) ✅ Present on every colonist card; shows `Ai mind data not yet available (Phase 4)` — designed Phase-1 placeholder, not a bug.
- [x] **D4.** Override check (Spec §6.1): order a colonist to walk somewhere → the player's order always wins
      over any AI desire. Draft a colonist → pending desires purge instantly; un-draft → normal behavior resumes after cooldown. ✅ User-confirmed: drafting + manual orders worked fine throughout the session; AI never contested player intent.
- [x] **D5.** Thought bubble (Spec §6.2): when an AI desire executes, a small text bubble with the colonist's inner voice
      floats above their head for ~3 s of real time. ✅ User visually confirmed "I need food" over Aiglamene (Logger) — log `thinks: I need food now!`, 0 `Mote throw failed` across the session; see TEST-RESULTS.md §9.6.

## E. If something is off

| Symptom | First suspect |
|---|---|
| No "Layer 1:" lines at all, no errors | AI disabled (C1) · game paused (the real-time clock resets while paused — D1 must be unpaused) · wrong URL (re-run A2) |
| `Layer 1 dispatch failed ...` errors in log | Server down or too slow → raise `RequestTimeoutMs` to 60000, re-test A2 |
| "Layer 1:" lines with `impulse_id=-1` or out-of-range ids | Model returned malformed JSON / ignored the schema — check the server's model (should be an instruct model); client-side guards already prevent crashes |
| "thinks:" appears but colonists rarely act | Expected in Phase 1: work impulses carry null jobDef until the ThingDef→JobDef mapping lands; only food/rest/social impulses execute today |
| Exception with an AIColonists stack frame | Paste it into TEST-RESULTS.md §7; first suspect is a 1.6 API mismatch — cross-check against the §8 verified-API map |
| Behavior fully vanilla, AI enabled, no errors | Check B3: if "patch no-match" for the ThinkTree patch, the xpath drifted from this build's Core defs |

## F. Cleanup

- [ ] Set `EnableAI` back to false (or leave it — with no reachable server the mod is inert) and exit the game. *(pending)*
- [x] Copy any interesting log lines into TEST-RESULTS.md §7 for the record. ✅ Done → now in **TEST-RESULTS.md §9** (session results + evidence).

## G. Phase 3 live check — Layer 2/3 social + mood bridge (~15 min)

Deployed build: DLL SHA256 `601310A4887C022B54BD62DADD8B5A8C4B7E30BA72E797761D443A9167F896A4`
(Phase 3 + intDef null guard + **Mono transport fix** + **proximity pre-check** — see below), Defs include
`MemoryThoughts/AIColonist_Memories.xml` (mood bridge). All three layer URLs in cfg → homelab :8091
(Qwen2.5-1.5B CPU, ~3–7 s/call). Harness 205/205 (transport tests + proximity pre-check + desire-freshness cases).

**Proximity fix (added 2026-11-13 after live session 2):** Layer 2 decisions were dispatching fine, but
`TryInteractWith` silently returned false every time — decompiled 1.6 shows it requires the pair to be within
**6 cells horizontal + line of sight** (plus a 120-tick cooldown), and our batcher groups by Room, not proximity.
The consume path now pre-checks with vanilla's own public gate (`CanInteractNowWith`), drops out-of-range decisions
(re-roll next ~90 s cycle), and logs the outcome per decision: `[executed]`, `[dropped: not interactable …]`, or
`[attempt failed at the last moment]`. The mood bridge defs were correct all along — they simply never fired because
no interaction ever succeeded (TEST-RESULTS.md §11.3–11.4).

**Stale-desire fix (staged after session 3, build `6CC1D36F…` — awaiting deploy):** live observation "I need food"
fired right after a finished meal — `QueuedDesire.IsValidAndReservable` re-checked the target but not the driving
need. It now mirrors the scanner's own gates (eat requires `food < 0.4`; rest requires `rest != null && < 0.4`), so
post-meal stale desires self-invalidate at pop time (TEST-RESULTS.md §11.7; harness 205/205). Deploy with the game
closed: robocopy `/MIR /XF .gitignore /XD Source Tests`, then hash-verify `6CC1D36F…`.

- [x] **G1.** ✅ VERIFIED (session 3): Player.log line 118 — `Cook Layer 2: warm_chat -> Hercules "Hey Hercules! You're doing great work. How's the pasta coming?" [executed]`; letter + play-log sentence user-confirmed in-game. Load the colony, unpause at 1×, cluster 2+ colonists in one room for ~10 min.
      Expect a letter (top-right) + play-log entry when an AI social impulse succeeds (one batched call covers
      the whole room every ~90 s; a decision only executes when the pair is within 6 cells + LOS — expect some
      `[dropped: not interactable …]` lines in larger rooms, that is correct behavior). Player.log witness lines:
      `[AIColonists] {name} Layer 2: {impulse} -> {target} "{line}" [executed]`.
- [x] **G2.** ✅ VERIFIED (session 3): 14 `Layer 3: consolidation applied` lines — all three colonists on the staggered cadence. After ~5 min, expect Layer 3 consolidation witness lines in Player.log (staggered 30 s per pawn):
      `[AIColonists] {name} Layer 3: consolidation applied` — relationship deltas clamped ±0.05 / traits ±0.02.
- [x] **G3.** ✅ VERIFIED (session 3): user-confirmed — warm chat visible in the Mood tab and the pawn play log. Mood bridge — opinion: after a warm chat, select either colonist → pawn card → **Mood tab**:
      expect a "warm chat" memory entry (opinion +4 per instance, max 4 per pair, lasts 20 days). A small mood
      bubble icon pops over the head when it's granted (`showBubble`).
- [ ] **G4.** ⏳ PARTIAL (session 3): warm-chat side confirmed via the Mood tab (mood blip + opinion memory); sharp-remark / venting variants await their first successful execution (defs decompile-verified, TEST-RESULTS.md §11.3). Mood bridge — mood bar: warm chat also grants a 2-day "warm chat" mood blip (+3); sharp remark
      grants a 2-day "sharp remark" blip (−4) to the target only; venting is neutral (opinion −3 to the listener,
      no mood change).
- [x] **G5.** ✅ VERIFIED (session 3): whole-log `truncated = 1` count **0** across 505 completed tasks; recent L2/L3-sized tasks (n_tokens 328–1113) all `truncated = 0`. Server cadence: on the homelab, `tail -n 50 /tmp/aicolonists-e2e/server-8091.log` — expect requests at
      ~90 s cadence per shared room with `truncated = 0`.
- [x] **G6.** ✅ VERIFIED (session 3): zero `dispatch failed` / transport-retry lines in Player.log. Transport fix witness: Player.log shows NO recurring `Layer 2 dispatch failed` / `Layer 3 dispatch
      failed` lines. An occasional `[AIColonists] transport error, retrying once: ...` line is expected behavior —
      the following dispatch should complete normally (and the same layer's next cycle succeeds).
- [x] **G7.** ✅ VERIFIED (session 3): `[dropped: not interactable …]` ×2 + `[executed]` ×1 observed; no `[attempt failed at the last moment]`. Proximity fix witness (new build): every Layer 2 decision line now ends with an outcome tag —
      `[executed]` (interaction really happened → play-log entry + mood thoughts granted),
      `[dropped: not interactable (distance/LOS/cooldown) — re-rolls next cycle]`, or
      `[attempt failed at the last moment]`. At least one `[executed]` per ~10 min with 2+ colonists clustered
      close together in one room.

## H. Phase 4 check — AI Mind tab, settings menu, district fallback (~20 min)

Deployed build: DLL SHA256 `75848197CAA71569419056EDA9AAFD65D47FDAFDA554F5BC79C79FBC29C4E184` (deployed 2026-11-16
with the game closed, robocopy + live==staged hash verified — **supersedes `8BC01297…`** and carries everything in it:
work-affordance job resolution (§20), JobOnThing NRE guard (§20.5, ErrorOnce 4813), prisoner/visitor LLM gate + Layer 1
log naming as `Name (Human####)` (§21, ErrorOnce 4814), **Phase 2 completion + AI-first job priority** (§22, ErrorOnce
4815), the §22.6 additions (`[hold]` / `[job started]` diagnostic log lines + skills-line prose fix), the one-shot
cold-start warmup (TEST-RESULTS.md §22.7) — plus the **T4 post-session fixes** (§23): `HasJobOnThing` two-stage contract
gate before every work-affordance `JobOnThing` call (ErrorOnce 4816; kills the Milk NRE + Lessongiving crashes), worker-
thread logging moved to a main-thread drain queue (`MainThreadLog`; fixes the in-game log window race), social option
names kept capitalized + system prompt line "names are people, never occupations", and `[job started]` now logs the
resolved JobDef: `[job started] "<impulse>" (<defName>)`. Supersedes `48049F1E…`, `19D345EE…`, `FF9BDE47…` (drop-gate
decomposition + self-targeting guard + NRE fix rounds 1–2), which superseded `82691858…`, `305A33E8…`, `4C20CEC1…`,
`9F5263E8…`; also bundles the Ollama-ready state verified in TEST-RESULTS.md §19 (all three layers live on
`http://127.0.0.1:11434` qwen2.5:3b, 100% GPU Vulkan). Full records in TEST-RESULTS.md §14–§23. Harness **322/322**.
Everything in this section is visual/in-game — the harness pins the data mapping, not the pixels.

**Ollama keep-alive (no setup needed, §22.7):** Ollama's DEFAULT 5-minute keep-alive is exactly the right behavior —
every Layer 1 call (≤30 s apart while unpaused) resets the timer, so the model stays resident during play and auto-unloads
~5 min after you pause/quit. **Do NOT set `OLLAMA_KEEP_ALIVE=-1`** (that would pin ~2 GB of VRAM forever). The only cost:
the first dispatch of a session triggers a one-time ~8 s model load, which the mod now absorbs with its warmup request —
expect `[AIColonists] inference backend warm — model loaded, dispatches run at full speed` in Player.log shortly after
game start (or a quiet `warmup skipped, inference server unreachable` line if Ollama isn't running yet — normal pre-game).

**What changed for your eyes (Phase 2 + AI-first, §22):** with *AI-first job priority* ON (default), a colonist whose
LLM decision is still in flight will **stand still for up to ~7.5 s** instead of immediately grabbing vanilla work — that
hold is the fix for "Layer 1 says X, pawn does Y"; expect work impulses (mine/haul/build/tend) to actually execute now
(re-test T4). If the server fails twice in a row per pawn, holds stop and vanilla takes over until a dispatch succeeds.
Prompts also changed: each colonist now has a real identity prefix (name/age/colony/traits/skills/memories) and mood
shifts sampling temperature — visible as more/less varied inner voices at low mood.

**T4 is now grep-verifiable (§22.6, defName suffix added §23):** every AI-issued job logs
`[job started] "<the exact impulse phrasing>" (<JobDef defName>)` — the defName makes per-job giver attribution
definitive (e.g. `ChatWithPrisoner` vs `FeedPrisoner`, TEST-RESULTS.md §23.8) — and every hold logs `[hold] waiting for
LLM decision (450 ticks)`. To grade a session:
```powershell
Select-String -Path "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log" `
  -Pattern "AIColonists" | Where-Object { $_.Line -match "Layer 1:|job started|\[hold\]" }
```
Pass = most `Layer 1:` work decisions are followed (seconds later, same pawn) by a matching `[job started]` line and the
pawn visibly does that job — with occasional `[hold]` lines in between. Bring me Player.log after the session and I'll
produce the decision→execution match table myself.

- [ ] **H1.** **AI Mind tab name + content.** First: the tab button in the bar should read **"AI Mind"** (plain English,
      no garbled characters). Select a colonist who has already acted on an LLM decision (any pawn from a
      session 3+ save qualifies — mind states persist). The **AI Mind** tab should now show real sections instead of
      the old placeholder: *Inner monologue* (quoted text + age like `(12 min ago)`), *Long-term desire*,
      *Recent memories* (≤3, newest first), *Self-knowledge* (only after a Layer 3 consolidation), *Trait drift*
      (≤6 bars, most-drifted-from-neutral first), *Relationships* (≤4 targets, T/A/R/F mini-bars; gone pawns show
      "unknown"). A brand-new pawn with no AI history shows the "No AI mind data yet" hint — that is correct.
- [ ] **H2.** **Tab layout re-check (you are the verifier).** The tab was rewritten to vanilla conventions: 17 px
      margins, small font everywhere (no tiny captions), row heights ≥ 24 px, word-wrapped blocks sized by real text
      measurement so nothing overlaps, and the height now **grows/shrinks with the pawn's data** (clamped to your
      screen). Check: any section clipped at the bottom edge? any two rows overlapping? any text still dark/black?
      Does the tab feel too tall/short for its content — tell me how many pixels and I'll adjust the constants in
      `ITab_Pawn_AIMind.cs` (`RowH`, `SectionGap`, `MarginX`).
- [x] **H3.** **Settings menu walk-through — PARTIAL (user, 2026-11-13): "clicking the VRAM preset changes setting text"
      confirmed on the UI side.** Remaining: after clicking a preset, close the window and confirm
      `Config\AIColonists.Core.Settings.cfg` shows the new model names + `requestTimeoutMs`, then watch Player.log for
      the next LLM call to confirm the server actually serves that model. The dialog is 900 × 700 with NO scroll, so
      also check: does everything fit without clipping at the bottom? New rows below the URLs: three *model name*
      fields (Layer 1/2/3), three *dual-clock real-time ceiling* fields in seconds (clamped ≥5 s on entry), and a
      **VRAM preset** button row — `CPU homelab` · `1x P100 16GB` · `2x P100 32GB`. Click **CPU homelab**: all three
      model fields should become `qwen2.5-1.5b-instruct-q4_k_m` and the timeout field `30000`; click **1x P100 16GB**:
      L1/L2 → `qwen2.5-3b-instruct-q8_0`, L3 → `qwen2.5-7b-instruct-q4_k_m`, timeout `15000`. Presets never touch the
      URL fields (those stay yours).
- [ ] **H4.** **50-colonist stress test — district batching fallback.** Goal: prove Layer 2 coverage holds when a
      colony has MORE eligible shared spaces than the per-cycle cap (4). Setup: get ~50 colonists on one map
      (Character Editor or successive drop pods), spread across 6+ distinct rooms/areas with 2+ pawns each, unpause at
      1×. Witness in Player.log: Layer 2 dispatch lines whose **roster exceeds the room** — a merged district shows up
      as one `Layer 2:` decision line mentioning pawns from adjacent spaces (districts are 30-tile grid cells; biggest
      district dispatched first). Expect some `[dropped: not interactable …]` lines for cross-space pairs inside a
      merged district — that is the safe path (they re-roll next cycle), NOT a failure. Pass criteria: no `dispatch
      failed` lines, no exceptions with AIColonists frames, and frame rate stable at 1×/3× with 50 pawns (the whole
      point of batching — one call per district, not per pawn). Note the wall clock when you start; a full Layer 2
      cycle is ~90 s real time. **Layer 2 eligibility (why it can be silent):** only UNDRAFTED, non-mental-state
      colonists count, and they must share a fully enclosed Room (or the same outdoor 12-tile cell) — drafted pawns on
      an open platform produce no batch at all. The gate re-checks every unpaused tick and its 90 s clock only advances
      on an actual dispatch, so a batch fires immediately once two eligible colonists share a room. First run (this
      session): Layer 1 + Layer 3 active across the new roster; one vanilla NRE in `JobDriver_Ingest` (meal eaten
      mid-walk by another of ~50 hungry colonists → null target deref, `CheckCurrentToilEndOrFail … b__19_0`) —
      root-caused and fixed in THIS build (§14); the re-test must show zero such exceptions.
- [ ] **H5.** **Pause-timing sub-case (carried from Phase 3).** At your next warm chat, note the wall-clock time of
      the letter and whether it arrived while paused or right after unpause — this settles the dispatch pause-gating
      question (current design: pre-pause requests complete on the background thread and sit in the buffer until the
      first post-unpause tick; that is benign by design, but we want your observation on hand).

---
## Status: Phase 1 — PASS (2026-11-12) · Phase 3 + mood bridge — COMPLETE (2026-11-13): interaction execution live-verified (G7), mood bridge confirmed in Mood tab + pawn log (G3; G4 partial — warm-chat side only) · **Phase 4 — DEPLOYED (2026-11-14 ~14:05, build `FF9BDE47…`, + drop-gate decomposition logging): harness 252/252; H1/H2 PASS (user); H3 partial (preset buttons confirmed on UI side by user; cfg-write + served-model proof still pending); H4 re-test done twice — night: Layer 2 fired for the first time, all dispatches `[dropped]` on proximity; morning (clustered 4 pawns in a small room ~10:42, unpaused ~10:44): 5 Layer 2 decisions in ~2 min (3 `[dropped]`, 2 self-targeting `[attempt failed]` — the LLM returned the initiator's own name; safe failure, fix pending), then silence BY DESIGN: pawns scattered to jobs → no shared space with 2+ undrafted colonists → no dispatch (the 90 s clock only advances on actual dispatch), and quiet impulses (`none`/`quiet_nod`/`avoid_gaze`) log nothing by design; zero Ingest NREs, zero `dispatch failed`; awaiting one `[executed]` or acceptance of partial pass; save-load NRE: round 1 (LabelCap) deployed ~01:56, **round 2 (`ThoughtDef.Label` computed-property NRE — `stages[0]` NULL mid-load) found + fixed + bundled into `82691858…` with the self-targeting guard (consume-side drop for ALL impulse types + "never yourself" prompt rule) — CONFIRMED LIVE 13:37 session: save load clean, zero NRE lines**; H4 afternoon re-test: 8 `[dropped]` pre-cluster, then 3×3-room cluster test ongoing; drop-gate decomposition (`FF9BDE47…`) will attribute each remaining drop; H5 open · **Phase 2 + AI-first job priority — DEPLOYED (2026-11-15 ~22:09, build `48049F1E…`, harness 305/305, TEST-RESULTS.md §22): real per-pawn identity prefix (KV-cache byte-stable), Intellect→cadence/horizon, Mood→entropy at dispatch, trait-weight seeding + drift-fed values/fears, hold-job AI-first priority with circuit breakers — open item: live re-test that work impulses now execute (T4) — **T4 GRADED PASS 2026-11-16 (§23, build `75848197…`): 21/22 decisions executed with eat/rest/caribou all correct; the work-giver gate fix + main-thread logging + defName attribution shipped in the same build (harness 322/322); next session re-verifies zero Milk/Lessongiving exceptions and gives definitive job attribution via the defName suffix**

All A–D items verified live against RimWorld 1.6.4871 with the Nov 9 llama.cpp build on CPU, including visual
confirmation of the thought bubble (D5). Full findings, root causes (JsonUtility dead end, object-form `json_schema`,
impulse_id-first ordering), in-game bug fixes and the MoteText API map are recorded in
[TEST-RESULTS.md §9–10](TEST-RESULTS.md). Phase 3 live session (2026-11-13): all three layers verified dispatching
and applying (9 Layer 2 decisions, 11 Layer 3 consolidations, zero transport errors) — but the interactions never
executed: decompiled 1.6 revealed `TryInteractWith` requires ≤6-cell + LOS proximity and our room-based batching
never checked it (TEST-RESULTS.md §11.3). The proximity pre-check + per-decision outcome logging shipped in build
`601310A4…`; section G above is the re-test (G7 is new). The Mono transport fix (single-flight gate + one retry on a
fresh pool) was added 2026-11-13 after the first live session showed Layer 2/3 dispatches failing at the transport
level; see the checklist header for build hashes.
