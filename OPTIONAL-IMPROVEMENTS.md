# Optional Improvements — AIColonists (Backlog)

Parked by design decision. Nothing here blocks the Spec v2.0 milestone list or Phase 3 completion.
Ordered roughly by value-to-effort. Last updated: 2026-11-15.

---

## 1. Multi-turn conversational dialogue (Layer 2 extension)

**Parked per user decision (2026-11-13): revisit after the project is done.**
Current single-line interactions are sufficient for relationship/emotional impact; a true
back-and-forth chat is not required.

- **What:** replace the per-colonist single `spoken_line` with per-pair `conversation_turns[]`
  (2–4 alternating lines, e.g. Alice/Bob/Alice) generated in the *same* batched call per room.
  Deliver turns as sequential speech bubbles (~3 s/line) via a tick-based emitter reusing
  `MoteBubbleUtility`; optional summary letter at the end.
- **Constraints (must hold):** one batched call per room per 90 s — no extra server load;
  truncation-tolerant parse (a cut turn array simply ends early); player orders/drafting still
  override; fits the ~150-token budget and 4096 context.
- **Effort:** schema + prompt change, bubble emitter in `MapComponentTick`, harness tests. ~half day.

## 2. Emotional memory writers (Spec §6 table, line 432 — real gap)

`MemoryStore.emotional` exists, is persisted and capped, but **nothing writes to it yet**.
The spec intends `Pawn_HealthTracker.PostApplyDamage` to record trauma/pain emotional memories
(and by extension comforts/fixations from positive events), fed into future prompts.

- **Effort:** one more defensive observational postfix + an emotional zone in the prompt builders. Small.

## 3. Mood bridge — AI interactions grant vanilla mood thoughts ✅ IMPLEMENTED 2026-11-13 (pending live verification)

User approved "Add it now" on 2026-11-13. Implemented **purely in defs, zero mod code**: the
decompiled 1.6 `Pawn_InteractionsTracker.TryInteractWith` grants `intDef.initiatorThought` /
`intDef.recipientThought` via `AddInteractionThought → memories.TryGainMemory`, and the
`thoughtToMake` chain (mood blips) is handled inside `MemoryThoughtHandler.TryGainMemory`.

- `Defs/MemoryThoughts/AIColonist_Memories.xml`: 5 ThoughtDefs — 3 opinion memories
  (`AIColonist_WarmChatMemory` +4 mutual, `AIColonist_VentFrustrationMemory` −3 listener-only,
  `AIColonist_SharpRemarkMemory` −6 target-only) + 2 mood blips (warm +3 / sharp −4, both 2 days).
- `Defs/InteractionDefs/AIColonist_Interactions.xml`: `<initiatorThought>`/`<recipientThought>`
  wired per def (warm chat mutual; venting/sharp remark recipient-only). Values deliberately milder
  than vanilla KindWords (+15) / Slighted (−5) / Insulted (−15, −5 mood); conventions matched
  (`showBubble` on warm chat, `Inhumanized` nullify with MayRequire Anomaly on blips).
- Harness: `Test_MemoryThoughtDefs` pins cross-file reference resolution, grant mapping, and
  calibration (19 checks; harness total 177 → **196**).
- **Status:** deployed + hash-verified (DLL `73B6F882…`, 2026-11-13). Live session 2026-11-13: all three
  layers verified dispatching/applying, but no interaction ever executed — decompiled root cause was the
  vanilla ≤6-cell + LOS gate in `TryInteractWith` (room batching ≠ proximity), fixed in build `601310A4…`
  with a pre-check on vanilla's own public `CanInteractNowWith`. Defs themselves verified correct by
  decompile (§11.3). **Live-verified 2026-11-13 (session 3):** first executed warm chat confirmed in the Mood tab +
  pawn play log (TEST-RESULTS.md §11.6); sharp-remark/venting variants await their first successful execution.

## 4. Work affordance jobDef (Phase 1 known gap)

Work affordances are scanned with a `null` jobDef and rejected at main-thread validation —
AI desires currently never produce work jobs. Needs a per-pawn WorkGiver→JobDef mapping for
the pawn's scheduled work types.

- **Effort:** medium — touchy vanilla API surface (work settings, givers), needs decompile pass.

## 5. Jealousy / dependency relationship axes (Layer 3 TODO)

`RelationshipVector` carries `jealousy`/`dependency` fields; `ApplyDeltas` intentionally skips
them and the Layer 3 schema has no deltas for them. Extending the schema + apply path would let
consolidation drift the full six-axis model.

- **Effort:** small (schema + one apply branch), but low value until social scenarios warrant it.

## 6. AI Mind tab content (Phase 4)

`ITab_Pawn_AIMind` is a placeholder panel. Should render identity prefix, continuous trait
weights, the four-part memory store, and relationship vectors live from the pawn's mind state.

- **Effort:** small–medium UI work; depends on nothing else here.

## 7. In-game AI decision chronicle (play-log integration)

Requested by user 2026-11-13 ("I haven't seen anything on my end, although I'm not sure what
to be on the look out for"). Today LLM decisions are visible only in Player.log (a dev file);
the in-game traces are vanilla side effects (interaction play-log entry, memory thoughts,
letters) — and those vanish entirely when `TryInteractWith` fails its preconditions, so to a
player the mod can look inert.

**Follow-up (same day, hard requirement):** user confirmed they can see per-colonist logs but
"there is no way to distinguish the llm activities from vanilla." Distinguishability — not just
chronicle-keeping — is the point. The dedicated section + unique phrasing below addresses it;
note Layer 1 (impulse → job) currently has NO in-game trace at all and is the biggest gap.

- **What:** custom `PlayLogSectionDef` ("AI Colonists") + `PlayLogEntryDef` + small
  `PlayLogEntry_AIColonistDecision` class; main-thread hook sites: `ConsumeSocialDecisions`
  (impulse, pair, spoken line, and whether TryInteractWith succeeded — the success/failure is
  the key missing signal), `ConsumeLayer3Results` (semantic-memory line + applied deltas),
  optionally Layer 1 at job start in `JobGiver_AIColonistDesire` (where the bubble already
  fires) behind a settings toggle since it's ~1 entry per pawn per 30 s. No new UI needed —
  the vanilla play-log window is the viewer. All hook sites are already main-thread.
- **Why parked:** code is small (~60–100 lines C# + ~20 lines def XML, zero behavior change)
  but needs a rebuild/redeploy cycle; per user instruction (2026-11-13) don't interrupt live
  verification for it. Batch with #2 and/or #6 on the next deploy.
- **Status:** partial — the Player.log observability half shipped in build `601310A4…` (proximity fix,
  2026-11-13): every Layer 2 decision now logs its outcome (`[executed]` / `[dropped: not interactable …]` /
  `[attempt failed at the last moment]`), which is exactly the success/failure signal this item calls out.
  What remains is the in-game `PlayLogSectionDef` chronicle (distinguishability for players, Layer 1 included).
- **Effort:** small; one deploy cycle.

## 8. AI-first job priority — suppress/reorder vanilla JobGivers when AI is enabled

Stated by the user as the end goal (2026-11-13): "disable much of the vanilla decisions to prioritize the llm, but
that will depend on performance." Today AI desires enter the think tree *alongside* vanilla givers and can lose the
race or fire late (TEST-RESULTS.md §11.7 — stale eat desire right after a meal; fixed by a need-freshness re-check
as a stopgap). True priority means reordering/suppressing vanilla JobGivers (`JobGiver_Eat`, `JobGiver_ForceSleepNow`,
work givers) behind `JobGiver_AIColonistDesire` while the pawn has a fresh AI desire, or gating them while an AI
decision is in flight.

- **Why parked:** performance-dependent (user's words). Every suppressed vanilla path changes colony behavior
  broadly; needs a careful decompile pass + live A/B before touching. The §11.7 freshness check removes the most
  visible symptom without changing job priority at all.
- **Effort:** medium–high — think-tree patching or per-giver `CanStartJob` postfixes, a settings toggle, extensive
  live testing.

## 9. LLM exclusion for non-colonists (prisoners/visitors) ✅ IMPLEMENTED 2026-11-15 (deploy pending)

Requested by user 2026-11-15: "there is one prisoner — add to dev plan to remove llm from prisoners."
Root cause found while tracing the "Human####" log mystery: `Pawn_Thinker.MainThinkTree => RaceProps.thinkTreeMain`
(decompiled) — the main tree is per-RACE, so prisoners share `MainColonistBehaviorCore` and reach our gate node. The old
gate (humanlike + undrafted + not-in-mental-state) passed for a colony prisoner → it got Layer 1 evaluations every 30 s
(~40k tokens/pawn-day) and could even receive AI-issued jobs. L2/L3 were already safe (`FreeColonistsSpawned` only).

- **Fix (one gate line):** `ThinkNode_ConditionalAIColonist.Satisfied` now requires
  `pawn.Faction == RimWorld.Faction.OfPlayer && !pawn.IsPrisoner`. 1.6 API note: the player faction is
  `RimWorld.Faction.OfPlayer` — `Find.FactionPlayer` does not exist in this build and `Faction` lives in the RIMWORLD
  namespace (compiler caught both; stubs updated to match). Prisoners keep their home faction, so the faction check is
  primary and IsPrisoner is belt-and-braces.
- **Open policy question (parked):** slaves are player-faction and therefore still LLM-eligible. Revisit if that ever
  looks wrong; visitors/raiders are already excluded by the faction check.
- Bundled with the JobOnThing NRE fix deploy — TEST-RESULTS.md §21.
