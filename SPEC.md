Your **v1.0 specification** has an exceptional conceptual foundation: the **"Goal vs. Execution" split**, the **"Unaware" sensory framing**, **area-based batching**, and the **4-tier memory \+ personality drift stack** are the exact right design patterns to make RimWorld colonists feel alive without breaking pathfinding or combat.

However, from a RimWorld C\# engine and local LLM systems perspective, v1.0 contains **six critical architectural flaws** that will either fail to compile, freeze the Unity main thread, or cause token collapse during inference.

## **Part 1: Critical Engineering Review of Spec v1.0**

### **1\. Synchronous Main-Thread Blocking & Infinite Recursion in Harmony Patches**

* **The Flaw (Section 6.3):** OnTryAcceptJob calls LLMBridge.CallLayer1() synchronously inside a Harmony Postfix on Pawn\_JobTracker, then calls TryGiveOrder(newJob).

* **Why It Breaks:**

  1. **Frame Stalls:** RimWorld runs on a single-threaded Unity main loop (16.6ms budget per tick at 1x speed; 5.5ms at 3x speed). Even a fast 200ms synchronous LLM call on the main thread freezes the entire game for 12 to 36 frames every time a colonist finishes a job.

  2. **Stack Overflow Recursion:** Starting a new job inside a job-acceptance hook immediately triggers the job-acceptance hook again, causing a StackOverflowException.

* **The v2.0 Fix:** **Asynchronous Producer-Consumer Intent Buffer \+ Custom ThinkNode\_JobGiver**. All LLM calls run asynchronously on background Task worker threads and write pre-validated QueuedDesire structs into a thread-safe ConcurrentDictionary. When a pawn needs a job on the main Unity thread, a custom JobGiver\_AIColonistDesire reads from that dictionary in **less than 0.01ms**—zero frame drops, zero recursion.

### **2\. Hallucinated RimWorld C\# Classes & Save APIs**

* **The Flaw (Sections 5.2 & 6.1):** The spec references several classes and methods that do not exist in RimWorld's Verse or RimWorld assemblies:

  * Pawn\_JobTracker.TryAcceptJob and TryGiveOrder → Do not exist. Actual methods are StartJob, TryTakeOrderedJob, and DetermineNextJob.

  * MapComponent\_Pawns.Tick → Does not exist (map.mapPawns is MapPawns, not a MapComponent).

  * PlayerSettings\_Drafting → Does not exist. Drafting lives on pawn.drafter.Drafted (Pawn\_DraftController).

  * Pawn\_Thinker\_Comms → Does not exist. Social interactions live on Pawn\_InteractionsTracker (pawn.interactions.TryInteractWith).

  * SaveSystem \+ XmlWriter → RimWorld uses IExposable (ExposeData()) with Scribe\_Values, Scribe\_Collections, and Scribe\_Deep.

* **The v2.0 Fix:** Instead of fighting Pawn\_JobTracker with fragile postfixes, v2.0 injects a custom ThinkNode\_JobGiver directly into RimWorld's native **MainColonistBehaviorCore ThinkTree** via XML patching—placed **below** Drafted, QueuedJob (player-forced right-click orders), MentalState, and emergency survival nodes, and **above** autonomous JobGiver\_Work and JobGiver\_Joy. This gives you the **Drafting Override Protocol** and **Player Manual Assignment Override** natively via the game engine.

### **3\. The "Target Resolution Gap" & Unaware Roleplay Paradox**

* **The Flaw (Sections 2.2 & 7.1):** The spec states colonists must never know their output is parsed as JSON or use mechanical terms, yet the prompt ends with Respond with JSON: {"action": "eat\|rest\|mining...", "target": "..."}. Worse, if the LLM outputs "action": "mining", "target": "stone wall", JobMaker.MakeJob(JobDefOf.Mine, target) will crash in C\# because RimWorld requires a concrete LocalTargetInfo (Thing or IntVec3) that passes ReservationManager.CanReserveAndReach.

* **The v2.0 Fix:** **Sensory Affordance Indexing \+ GBNF Constrained Decoding**.

  1. Before sending a prompt, a C\# AffordanceScanner checks the pawn's surroundings and valid WorkGiver targets to build 4 to 6 concrete, reachable options mapped to sensory descriptions (e.g., 1: Eat the hot simple meal on the wooden table, 2: Chip away at the compacted steel vein in the northern tunnel, 3: Sit near Alice by the hearth and talk).

  2. The inference server enforces a strict **GBNF JSON Schema** at the token-sampler level ({"inner\_voice": "...", "impulse\_id": 1}). The model writes its first-person "inner\_voice" completely in character without meta-gaming, and picks an integer impulse\_id that maps back to a pre-validated Job and LocalTargetInfo in C\#.

### **4\. Inference Engine & Hardware Mismatch (TensorRT-LLM vs. llama-server)**

* **The Flaw (Metadata & Section 3.5):** The spec targets **TensorRT-LLM** for **Mac M4 Max** and **Tesla P100**.

  * TensorRT-LLM is NVIDIA-only (it cannot run on macOS / Apple Silicon at all).

  * On NVIDIA Tesla P100 (sm\_60 Pascal architecture), TensorRT-LLM lacks Tensor Core kernels (sm\_70+) and struggles with quantized formats.

* **The v2.0 Fix:** Standardize on **llama.cpp (llama-server)** over local HTTP/SSE (compatible with both M4 Max via Metal and single/dual Tesla P100s via native sm\_60 FP16 CUDA kernels):

  * **Prompt Prefix Caching (\--cache-reuse 256):** By ordering the prompt so static colonist identity, values, and semantic memories come *first* and dynamic sensory state comes *last*, llama-server reuses the KV cache prefix across calls—cutting prefill compute by over 70%.

  * **Dual-P100 (2x 16GB \= 32GB) & Single-P100 (16GB) Layouts:** Documented explicitly so Layer 1/2 and Layer 3 (Qwen3.6-35B-A3B at Q4\_K\_M or Qwen2.5-14B) can run without VRAM thrashing.

### **5\. Intellect-to-Temperature Inversion**

* **The Flaw (Section 3.2):** High/Genius intellect is mapped to Temperature 0.80–1.10, plus \+0.25 when angry \= **1.35 temperature** on a 1.5B/3B Q4 model. At temperature 1.35, a 1.5B model does not become "more strategic and deliberative"—it suffers probability distribution collapse and outputs incoherent tokens.

* **The v2.0 Fix:** Decouple **Entropy (Mood/Stress)** from **Cognitive Depth (Intellect)**:

  * **Mood** controls temperature (0.50 to 0.85) \+ min\_p (0.08, which clips garbage tail tokens even when agitated).

  * **Intellect** controls **Sensory Horizon** (low intellect only sees immediate physical urges; high intellect receives colony-wide context like incoming weather, low medicine stockpiles, and multi-step consequences) and **Inner Voice Token Budget**.

### **6\. Wall-Clock (DateTime.Now) vs. Game-Tick Desynchronization**

* **The Flaw (Section 6.3):** Periodic pulses check DateTime.Now. If the player pauses RimWorld for 10 minutes to plan a base layout, the mod continues firing 50+ LLM calls against a frozen game state. Conversely, if you only use game ticks at 3x/4x speed, LLM calls fire 4x faster and overwhelm the GPU.

* **The v2.0 Fix:** **Dual-Clock Gate**. Calls require *both* elapsed game ticks (Find.TickManager.TicksGame, which stops when paused) *and* elapsed real-world unpaused seconds (Stopwatch), keeping GPU load constant at 1x, 2x, and 3x game speeds.

## **Part 2: Updated Specification (AI-Integrated-Colonists-Spec.md v2.0)**

Copy-paste the updated specification below directly into AI-Integrated-Colonists-Spec.md:

Markdown

````
# AI-Integrated Colonists: Complete Implementation Specification (v2.0)

## Document Metadata

| Field | Value |
|-------|-------|
| **Title** | AI-Integrated Colonists — Full Implementation Spec |
| **Version** | 2.0 (Engine-Verified & Async Architecture) |
| **Target Game** | RimWorld (1.5 / 1.6+ compatible) |
| **Hardware Targets** | Dual Tesla P100 (2x16GB sm_60), Single P100 (16GB), Mac M4 Max (32GB) |
| **Model Targets** | Qwen2.5-1.5B-Instruct (Layer 1), Qwen2.5-3B / DeepSeek-R1-Distill-3B (Layer 2), Qwen3.6-35B-A3B / Qwen2.5-14B (Layer 3) |
| **LLM Framework** | `llama.cpp` (`llama-server` with Continuous Batching, Prefix Caching, & GBNF) / Ollama fallback |
| **Mod Framework** | Harmony 2.x + Native `Verse.AI.ThinkNode` XML Injection |
| **Last Updated** | November 2026 |

---

## Table of Contents

1. [Project Overview & Goals](#1-project-overview--goals)
2. [Core Design Philosophy](#2-core-design-philosophy)
3. [Architecture: Async Layered LLM System](#3-architecture-async-layered-llm-system)
4. [Per-Colonist Differentiation System](#4-per-colonist-differentiation-system)
5. [State Management, Affordance Grounding & Save Compatibility](#5-state-management-affordance-grounding--save-compatibility)
6. [ThinkTree Integration & Harmony Patch Targets](#6-thinktree-integration--harmony-patch-targets)
7. [Prompt Engineering: Prefix-Cached Templates & GBNF Schemas](#7-prompt-engineering-prefix-cached-templates--gbnf-schemas)
8. [Performance Budget & Hardware Mapping](#8-performance-budget--hardware-mapping)
9. [Implementation Phases & Milestones](#9-implementation-phases--milestones)
10. [Risk Analysis & Mitigation](#10-risk-analysis--mitigation)
11. [Testing Strategy](#11-testing-strategy)
12. [Appendix: Reference Tables & File Structure](#appendix-a-reference-tables)

---

## 1. Project Overview & Goals

### 1.1 Vision Statement

Each colonist in RimWorld is an autonomous agent driven by a local LLM. Their behavior is governed by their traits, memories, relationships, and current sensory state. The fundamental gameplay loop remains unchanged — schedules work, drafting works, combat works — but the LLM determines *which* tasks each colonist pursues and *how* they interact with others. The colonist does not know it is in a game; it experiences sensory inputs and forms natural desires that the mod translates into pre-validated vanilla jobs.

### 1.2 Goals (Ranked by Priority)

| Priority | Goal | Description |
|----------|------|-------------|
| **P0** | Zero-stall asynchronous inference | LLM calls never block the Unity main thread; all inference runs asynchronously via a background producer-consumer queue |
| **P0** | Trait-driven autonomous action selection | Each colonist chooses its next task from pre-validated sensory affordances based on traits, memories, and current state |
| **P0** | "Unaware" roleplay integrity | Colonists never reference game mechanics in their reasoning; inputs are sensory-framed, and outputs use GBNF grammar enforcement |
| **P0** | Native ThinkTree drafting & player override | Player commands (drafting, right-click forced jobs) and life-safety emergencies always supersede AI desires natively via RimWorld's ThinkTree priority |
| **P1** | Per-colonist differentiation | 10 colonists sharing one base model must behave like 10 distinct minds (personality, memory, perception horizon, relational vectors) |
| **P1** | Area-batched Dual-Clock scheduling | LLM calls batch by room/district and gate on both game ticks (respecting pause) and real-time seconds (preventing 3x speed GPU floods) |
| **P1** | Long-term personality evolution | Colonists' continuous trait weights drift over time based on lived experiences; personalities evolve across multi-year colonies |
| **P2** | Layered model hierarchy (3 layers) | Layer 1 (1.5B) for immediate action, Layer 2 (3B) for social/emotional state, Layer 3 (MoE A3B / 14B) for memory consolidation and drift |
| **P2** | Intellect-to-Horizon & Mood-to-Entropy mapping | Intellect controls context horizon and inner-monologue depth; Mood controls sampling temperature and `min_p` volatility |
| **P3** | Autonomous inter-colonist social initiation | Colonists initiate custom conversations/arguments based on relationship vectors via `Pawn_InteractionsTracker` |
| **P4** | Large colony scaling (50+ colonists) | Graceful degradation using district batching and dynamic cooldown scaling |

### 1.3 Non-Goals (Explicitly Out of Scope)

- ❌ Synchronous per-tick or on-thread LLM evaluation
- ❌ Replacing vanilla combat AI (drafted or fleeing colonists use vanilla AI entirely)
- ❌ Replacing A* pathfinding, reservation mechanics, or job driver execution (mining speed, cooking ticks)
- ❌ Cloud-based API dependencies (100% self-hosted local inference)

---

## 2. Core Design Philosophy

### 2.1 The "Desire vs. Execution" Split & Affordance Grounding

~~~text
┌─────────────────────────────────────────────────────────────────────────┐
│                       ASYNC COLONIST ARCHITECTURE                       │
│                                                                         │
│  [MAIN THREAD: Affordance Scanner]                                      │
│   Scans pawn's room & valid WorkGivers → builds 4-6 reachable options   │
│   ([1] Eat meal on table, [2] Mine steel vein, [3] Talk to Alice)       │
│                             │                                           │
│                             ▼ (Async HTTP Request + GBNF Schema)        │
│  [BACKGROUND THREAD: llama-server]                                      │
│   Colonist thinks in-character: "My back aches and Alice looks upset,   │
│   I'll grab a bowl of stew and sit by her." → Selects Impulse [1]       │
│                             │                                           │
│                             ▼ (Writes to Thread-Safe Intent Buffer)     │
│  [MAIN THREAD: ThinkNode_JobGiver]                                      │
│   Reads Impulse [1] in <0.01ms → Returns pre-validated Job & Target     │
│   Vanilla Pawn_JobTracker reserves target, pathfinds, and executes!     │
└─────────────────────────────────────────────────────────────────────────┘
~~~

**Principle**: The colonist is a *desire engine*, not a *pathfinding engine*. Crucially, the LLM never invents raw strings for targets that might not exist on the map; instead, C# translates reachable `Thing` and `WorkGiver` targets into sensory options, and the colonist chooses among what it can physically perceive.

### 2.2 The "Unaware" Constraint

The colonist must never:
- Reference being in a game, simulation, or AI system
- Use mechanical language (`hunger: 0.8`, `mood_buff`, `work_priority`)
- Be asked to write raw mechanical syntax without an in-character monologue

It must only:
- Experience the world through trait-filtered sensory descriptions
- Express an internal monologue (`inner_voice`) in first person, followed by selecting a numbered physical impulse enforced by server-side GBNF grammar

### 2.3 Prefix-Cached Differentiation Principle

To make 10 colonists feel distinct on one shared model while keeping prefill latency under 15ms, prompts are strictly ordered into two zones:
1. **Static Prefix (KV-Cached per slot via `--cache-reuse`):** Core identity, values, speech style, and condensed semantic memories (changes only every 5 minutes during Layer 3 updates).
2. **Dynamic Suffix (Evaluated per call):** Current relational tensions with nearby pawns, immediate sensory state, and numbered sensory affordances.

---

## 3. Architecture: Async Layered LLM System

### 3.1 Three-Layer Model Hierarchy

| Layer | Target Model | Quantization & VRAM | Frequency (Dual-Clock) | Purpose | Max Output |
|-------|--------------|---------------------|------------------------|---------|------------|
| **Layer 1** | `Qwen2.5-1.5B-Instruct` | Q8_0 or FP16 (~1.8–3.0 GB) | Every 30s real-time (min 1800 game ticks) per active room | Immediate impulse selection from sensory affordances + brief inner thought | ~80 tokens (GBNF JSON) |
| **Layer 2** | `Qwen2.5-3B-Instruct` or `DeepSeek-R1-Distill-3B` | Q4_K_M or FP16 (~2.2–6.0 GB) | Every 90s real-time (min 5400 game ticks) per multi-pawn room | Social dynamics, spontaneous interaction initiation, emotional shift | ~150 tokens (GBNF JSON) |
| **Layer 3** | `Qwen3.6-35B-A3B` (MoE) or `Qwen2.5-14B-Instruct` | Q4_K_M (~20 GB across 2x P100, or ~8.5 GB for 14B on 1x P100) | Every 5 min real-time (min 18000 game ticks) per colonist (staggered) | Episodic-to-semantic memory consolidation, relationship vector updates, trait drift | ~350 tokens (GBNF JSON) |

> **Unified Server Alternative (Dual P100 / M4 Max Optimization):** Because `Qwen3.6-35B-A3B` only activates **3B parameters per token**, a Dual-P100 (32GB) or M4 Max (32GB) setup can optionally run `Qwen3.6-35B-A3B` (Q4_K_M, ~20GB) across *all three layers* using a single `llama-server` instance with 8 parallel slots (`-np 8`), eliminating model-swapping overhead while achieving 35B-class roleplay depth at 3B speed.

### 3.2 Cognitive Style Mapping: Intellect → Horizon, Mood → Entropy

Small models degrade rapidly if temperature exceeds `1.0`. Therefore, **Intellect** and **Mood** control orthogonal parameters:

| Intellect Skill (RimWorld 0–20) | Tier | Context Horizon Injected by C# | Inner Voice Token Limit | Layer Routing |
|---------------------------------|------|--------------------------------|-------------------------|---------------|
| **0–5** | **Simple** | Immediate bodily needs + objects within 8 tiles only | 25 tokens (blunt, literal) | Layer 1 only (Layer 3 every 10m) |
| **6–11** | **Average** | Room state + current schedule + recent 3 episodic memories | 50 tokens (practical) | Standard Layer 1/2/3 |
| **12–16** | **Sharp** | Colony resource warnings (low food/meds), weather forecast, 5 memories | 80 tokens (deliberative, weighs trade-offs) | Layer 1 uses Layer 2 model if idle |
| **17–20** | **Genius** | Full strategic awareness (raid recovery state, long-term project bottlenecks, subtle social dynamics) | 120 tokens (nuanced, strategic, foresight-driven) | Prioritized for Layer 2/3 compute |

**Mood → Sampling Entropy (Temperature + `min_p` Guardrail):**

~~~python
# Always enforce min_p = 0.08 to prevent low-probability token gibberish
BASE_TEMP = 0.65

MOOD_ENTROPY_MAP = {
    'ecstatic':  {'temp_offset': +0.05, 'top_p': 0.92},
    'happy':     {'temp_offset':  0.00, 'top_p': 0.90},
    'content':   {'temp_offset': -0.05, 'top_p': 0.85}, # Predictable, steady routine
    'stressed':  {'temp_offset': +0.10, 'top_p': 0.92}, # Slightly erratic
    ' furious':  {'temp_offset': +0.20, 'top_p': 0.95}, # Max 0.85 temp: impulsive, sharp
    'depressed': {'temp_offset': -0.15, 'top_p': 0.80}, # Max 0.50 temp: flat, repetitive, low-energy
}
~~~

### 3.3 Dual-Clock Hybrid Scheduling

To prevent LLM calls from firing while the game is paused *and* prevent 3x/4x game speed from multiplying GPU load by 4x, a trigger only fires when **both** conditions are met:
1. **Game-Tick Delta:** `Find.TickManager.TicksGame - lastCallTick >= minGameTicks` (pauses automatically when game is paused).
2. **Real-Time Unpaused Delta:** `unpausedStopwatch.ElapsedSeconds - lastCallRealSeconds >= minRealSeconds` (caps real-world GPU requests regardless of 1x/2x/3x speed).

### 3.4 Area-Based Batching Protocol

When multiple colonists occupy the same enclosed `Room` (or within a 12-tile cluster outdoors), `AreaBatcher` combines their dynamic states into a single multi-agent prompt:
- Only triggers when at least one colonist in the room needs a decision and is not drafted or in a mental break.
- Outputs a GBNF-constrained JSON array with one decision object per colonist in the room, enabling natural synchronized actions (e.g., Alice decides to tell a joke at the table while Bob decides to pause eating and listen).

---

## 4. Per-Colonist Differentiation System

### 4.1 The 8-Layer Differentiation Stack

| Layer | Mechanism | Effect |
|-------|-----------|--------|
| **1. Core Identity Prefix** | Backstory (Childhood + Adulthood) + Vanilla Traits | Distinct worldview, vocabulary, and moral baseline |
| **2. Continuous Trait Weights** | Floating-point trait map (`0.0` to `1.0`) initialized from vanilla traits | Allows subtle personality gradations between two pawns with the same vanilla trait |
| **3. Intellect Horizon** | Skill-gated perception radius and colony awareness | Low-intellect pawns live in the moment; high-intellect pawns worry about winter food stores |
| **4. Mood Entropy** | Mood-driven temperature (`0.50–0.85`) + speech cadence rules | Depressed pawns think in short, flat sentences; angry pawns fixate on grievances |
| **5. Four-Part Memory Store** | Episodic, Semantic, Procedural, and Emotional memories | Pawns remember specific rescues, betrayals, favorite spots, and traumas |
| **6. Relational Vectors** | 6-axis directional opinion state (`trust`, `affection`, `respect`, `jealousy`, `frustration`, `dependency`) | Asymmetric relationships (Alice may depend on Bob while Bob resents Alice) |
| **7. Sensory Perception Filter** | Trait-injected sensory hooks before the prompt is built | A Bloodlust pawn notices the weight of a knife; a Squeamish pawn fixates on dirt and blood |
| **8. Experiential Drift** | Layer 3 periodic weight updates (`±0.02` max per cycle) | Hardships harden pawns; prolonged safety softens paranoia |

### 4.2 Relational Context System

```csharp
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
````

## **5\. State Management, Affordance Grounding & Save Compatibility**

### **5.1 Affordance Scanner (Bridging Sensory Desires to Verse.AI.Job)**

Instead of letting the LLM hallucinate invalid job targets, AffordanceScanner.Scan(Pawn pawn) runs on the main thread prior to queuing an async LLM request and extracts up to 6 concrete AffordanceOption structs:

C\#

```
public struct AffordanceOption
{
    public int optionId;                 // 1 to 6
    public string sensoryDescription;    // Injected into prompt: "Eat the hot simple meal on the dining table"
    public JobDef jobDef;                // e.g., JobDefOf.Ingest
    public LocalTargetInfo targetA;      // Concrete Thing or IntVec3 reference
    public LocalTargetInfo targetB;      // Optional secondary target (e.g., chair/table)
    public WorkTypeDef workType;         // Optional origin work type
}
```

**How Affordances are Populated:**

1. **Physiological Needs:** If pawn.needs.food.CurLevelPercentage \< 0.4f, runs FoodUtility.BestFoodSourceOnMap to find the exact meal Thing and formats it with sensory taste/temperature descriptions.

2. **Rest & Comfort:** Checks RestUtility.FindBedFor(pawn) if tired or in pain.

3. **Top WorkGivers:** Queries the pawn's active WorkGiver list (pawn.mindState.priorityWork and top 2 scheduled work types) using WorkGiver\_Scanner.PotentialWorkThingsGlobal(pawn) to grab the nearest reachable, reservable work target.

4. **Social Opportunities:** Scans pawns within 15 tiles with line-of-sight (GenSight.LineOfSight) for approach/chat/avoid impulses.

5. **Environmental Wander/Idle:** Always includes one trait-flavored idle/relax option as a safe fallback.

### **5.2 Save Compatibility via WorldComponent & IExposable**

All AI state is stored in a custom AIColonistWorldComponent : WorldComponent indexed by pawn.ThingID. This avoids patching Pawn.ExposeData directly and guarantees 100% safe mod removal or mid-save addition:

C\#

```
public class AIColonistWorldComponent : WorldComponent
{
    private Dictionary<string, ColonistMindState> mindStates = new Dictionary<string, ColonistMindState>();

    public AIColonistWorldComponent(World world) : base(world) { }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref mindStates, "colonistMindStates", LookMode.Value, LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && mindStates == null)
        {
            mindStates = new Dictionary<string, ColonistMindState>();
        }
    }
}
```

## **6\. ThinkTree Integration & Harmony Patch Targets**

### **6.1 Native ThinkTree Injection (Defs/ThinkTreeDefs/AIColonist\_ThinkTreePatch.xml)**

Rather than intercepting every job in Pawn\_JobTracker (which breaks forced jobs and causes recursion), we insert ThinkNode\_ConditionalAIColonist directly into MainColonistBehaviorCore right above ThinkNode\_PrioritySorter (where vanilla autonomous work/joy happens) and **below** all emergency, drafted, and player-queued nodes:

XML

```
<?xml version="1.0" encoding="utf-8" ?>
<Patch>
  <Operation Class="PatchOperationInsert">
    <!-- Insert right before the standard autonomous Work/Joy PrioritySorter -->
    <xpath>/Defs/ThinkTreeDef[defName="MainColonistBehaviorCore"]/thinkRoot/subNodes/li[@Class="ThinkNode_PrioritySorter"]</xpath>
    <order>Prepend</order>
    <value>
      <li Class="AIColonists.ThinkTree.ThinkNode_ConditionalAIColonist">
        <subNodes>
          <li Class="AIColonists.ThinkTree.JobGiver_AIColonistDesire" />
        </subNodes>
      </li>
    </value>
  </Operation>
</Patch>
```

**Why this architecture is bulletproof:**

1. **Drafting (ThinkNode\_ConditionalColonistDrafted)** sits higher in MainColonistBehaviorCore. When drafted, execution never reaches JobGiver\_AIColonistDesire.

2. **Player Right-Click Orders (ThinkNode\_QueuedJob)** sit higher in MainColonistBehaviorCore. Player-forced jobs always execute immediately without AI interference.

3. **Downed, Burning, Mental State, and Medical Rest** sit higher in MainColonistBehaviorCore. Colonists will never ignore being on fire to finish daydreaming.

4. **Zero Main-Thread Waiting:** If JobGiver\_AIColonistDesire checks the async ColonistIntentBuffer and no fresh LLM desire is ready (or the target became unreservable), it returns null in 0.001ms and falls through seamlessly to vanilla ThinkNode\_PrioritySorter while queueing a background LLM evaluation.

### **6.2 Non-Blocking JobGiver\_AIColonistDesire Implementation**

C\#

```
public class JobGiver_AIColonistDesire : ThinkNode_JobGiver
{
    protected override Job TryGiveJob(Pawn pawn)
    {
        if (!AIColonistMod.Settings.EnableAI || pawn.Drafted || pawn.InMentalState)
            return null;

        var manager = Current.Game.GetComponent<AIColonistGameComponent>();
        
        // 1. Check if the background LLM worker has deposited a validated desire
        if (manager.IntentBuffer.TryPopDesire(pawn.ThingID, out QueuedDesire desire))
        {
            // Verify target is still valid and reservable on the main thread
            if (desire.IsValidAndReservable(pawn))
            {
                Job job = JobMaker.MakeJob(desire.JobDef, desire.TargetA, desire.TargetB);
                job.expiryInterval = 1800; // Re-evaluate after ~30s if long-running
                MoteBubbleUtility.TryThrowThoughtMote(pawn, desire.InnerVoiceShort);
                return job;
            }
        }

        // 2. If cooldown expired and no request is in-flight, snapshot affordances & queue async call
        if (manager.ShouldQueueEvaluation(pawn))
        {
            List<AffordanceOption> affordances = AffordanceScanner.Scan(pawn);
            SensorySnapshot snapshot = SensoryFramingBuilder.CaptureMainThreadSnapshot(pawn, affordances);
            manager.EnqueueAsyncLayer1(snapshot);
        }

        // 3. Return null immediately (<0.01ms) so vanilla AI handles the pawn until async response arrives
        return null;
    }
}
```

### **6.3 Minimal Harmony Patch Targets**

Because job execution is handled cleanly by the ThinkTree, Harmony patches are kept minimal and strictly event-driven:

| Target Class & Method | Patch Type | Purpose |
| :---- | :---- | :---- |
| Pawn\_InteractionsTracker.TryInteractWith | Postfix | Captures social interaction outcomes (InteractionDef), records episodic memories, and updates RelationshipVector trust/affection |
| MemoryThoughtHandler.TryGainMemory | Postfix | Detects significant mood/thought events (ate without table, witnessed death, insulted) to flag an immediate event-driven Layer 1/2 evaluation |
| Pawn\_HealthTracker.PostApplyDamage | Postfix | Records trauma/pain emotional memories and clears non-urgent AI desires |
| Pawn\_DraftController.Drafted (Setter) | Postfix | Purges any pending non-combat desires from IntentBuffer the instant a pawn is drafted |

## **7\. Prompt Engineering: Prefix-Cached Templates & GBNF Schemas**

### **7.1 Layer 1 Prompt Template (Ordered for \--cache-reuse KV Prefix Sharing)**

Plaintext

```
<|im_start|>system
# IDENTITY (STATIC PREFIX - KV CACHED)
You are [Name], age [Age], living in the settlement of [ColonyName].
You experience this world as physical reality. You have no concept of games, stats, or simulations.

## WHO YOU ARE
[BackstoryAndCoreTraitSummary]
Speech & Thought Cadence: [CadenceRuleFromIntellectAndTraits]

## WHAT YOU VALUE & FEAR
- Values: [TopValues]
- Dislikes: [TopAversions]

## LONG-TERM MEMORIES & HABITS
- [SemanticMemory1]
- [SemanticMemory2]
- [ProceduralHabit1]
<|im_end|>
<|im_start|>user
# CURRENT EXPERIENCE (DYNAMIC SUFFIX)
## RECENT EVENTS ON YOUR MIND
- [EpisodicMemory1]
- [EpisodicMemory2]

## PEOPLE NEARBY
- [NearbyPawn1_RelationalFraming]
- [NearbyPawn2_RelationalFraming]

## YOUR BODY & SURROUNDINGS
- [SensoryNeed1_HungerOrRest]
- [SensoryNeed2_EnvironmentOrPain]
- [TraitFilteredPerceptionHook]
- [IntellectGatedColonyHorizon]

## POSSIBLE IMPULSES RIGHT NOW
[1] [Affordance1_SensoryDescription]
[2] [Affordance2_SensoryDescription]
[3] [Affordance3_SensoryDescription]
[4] [Affordance4_SensoryDescription]

Express your fleeting inner thought in first person, optional spoken mutter, and the number of the impulse you act on.
<|im_end|>
```

### **7.2 GBNF / JSON Schema Enforcement (Layer 1\)**

Passed via response\_format: { "type": "json\_schema", "json\_schema": ... } to llama-server:

JSON

```
{
  "type": "object",
  "properties": {
    "inner_voice": {
      "type": "string",
      "description": "First-person internal sensation and thought, 1-2 sentences, zero mechanical terms."
    },
    "spoken_mutter": {
      "type": "string",
      "description": "Optional short phrase muttered aloud (or empty string if silent)."
    },
    "impulse_id": {
      "type": "integer",
      "enum": [1, 2, 3, 4, 5, 6]
    }
  },
  "required": ["inner_voice", "spoken_mutter", "impulse_id"],
  "additionalProperties": false
}
```

### **7.3 Layer 2 Prompt & Schema (Room Social & Emotional Shift)**

Triggered every 90s for rooms with 2+ colonists. Evaluates whether a colonist initiates a custom interaction (chitchat, deep\_talk, slight, insult, kind\_words, avoid) and updates emotional tension (anxiety, warmth, irritation).

JSON

```
{
  "type": "object",
  "properties": {
    "decisions": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "colonist_name": { "type": "string" },
          "inner_feeling": { "type": "string" },
          "social_impulse": { "type": "string", "enum": ["none", "warm_chat", "vent_frustration", "sharp_remark", "quiet_nod", "avoid_gaze"] },
          "target_name": { "type": "string" },
          "spoken_line": { "type": "string" }
        },
        "required": ["colonist_name", "inner_feeling", "social_impulse", "target_name", "spoken_line"]
      }
    }
  },
  "required": ["decisions"]
}
```

### **7.4 Layer 3 Schema (Memory Consolidation & Trait Drift)**

Triggered every 5 minutes per colonist (staggered so no two Layer 3 calls overlap). Summarizes raw episodic events into condensed semantic memories and shifts continuous trait weights within \[-0.02, \+0.02\].

JSON

```
{
  "type": "object",
  "properties": {
    "self_reflection": { "type": "string" },
    "new_semantic_memory": { "type": "string" },
    "relationship_deltas": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "target_name": { "type": "string" },
          "trust_delta": { "type": "number", "minimum": -0.05, "maximum": 0.05 },
          "affection_delta": { "type": "number", "minimum": -0.05, "maximum": 0.05 },
          "respect_delta": { "type": "number", "minimum": -0.05, "maximum": 0.05 },
          "frustration_delta": { "type": "number", "minimum": -0.05, "maximum": 0.05 }
        },
        "required": ["target_name", "trust_delta", "affection_delta", "respect_delta", "frustration_delta"]
      }
    },
    "trait_drift": {
      "type": "object",
      "properties": {
        "trait_name": { "type": "string" },
        "delta": { "type": "number", "minimum": -0.02, "maximum": 0.02 }
      },
      "required": ["trait_name", "delta"]
    },
    "long_term_longing": { "type": "string" }
  },
  "required": ["self_reflection", "new_semantic_memory", "relationship_deltas", "trait_drift", "long_term_longing"]
}
```

## **8\. Performance Budget & Hardware Mapping**

### **8.1 Hardware Topologies & llama-server Configurations**

| Hardware Target | Deployment Topology | Models Loaded | Parallel Slots & Flags | Peak VRAM | Expected Latency (Layer 1\) |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **Dual Tesla P100 (2x 16GB \= 32GB)** | **Option A (Dedicated Dual-Instance):** GPU 0 runs Layer 1+2 (Qwen2.5-3B FP16/Q8); GPU 1 runs Layer 3 (Qwen2.5-14B Q4\_K\_M). **Option B (Unified MoE):** Split Qwen3.6-35B-A3B Q4\_K\_M across GPU 0+1 for all layers.  | Qwen2.5-3B \+ Qwen2.5-14B OR unified Qwen3.6-35B-A3B | \-np 8 \-cb \--cache-reuse 256 \-ctk f16 \-ctv f16 | \~22 GB / 32 GB | \~120–180ms per room batch |
| **Single Tesla P100 (1x 16GB)** | Single llama-server running Qwen2.5-3B-Instruct (Q8\_0, \~3.5GB) handling Layers 1 & 2, \+ Qwen2.5-7B-Instruct (Q4\_K\_M, \~4.8GB) for Layer 3 | Qwen2.5-3B \+ Qwen2.5-7B | \-np 4 \-cb \--cache-reuse 256 | \~10.5 GB / 16 GB | \~160–240ms per room batch |
| **Mac M4 Max (32GB Unified)** | Unified Metal llama-server running Qwen3.6-35B-A3B (Q4\_K\_M) or Qwen2.5-1.5B \+ Qwen3.6-35B-A3B | Qwen2.5-1.5B \+ Qwen3.6-35B-A3B | \-np 8 \-cb \--cache-reuse 256 \-fa | \~21 GB / 32 GB | \~90–140ms per room batch |

### **8.2 Call Schedule & Token Budget (10 Colonists in 3 Rooms)**

| Layer | Frequency (Dual-Clock) | Calls / Hour | Avg Prompt (Cached / New) | Output Tokens | Active GPU Time / Min |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **Layer 1** | Every 30s per active room | \~180 | 350 cached \+ 120 new | \~60 tok | \~0.55s / min |
| **Layer 2** | Every 90s per shared room | \~80 | 350 cached \+ 180 new | \~120 tok | \~0.35s / min |
| **Layer 3** | Every 5m per colonist (staggered by 30s) | \~120 | 500 cached \+ 300 new | \~250 tok | \~0.80s / min (background GPU) |
| **Total** |  | **380 calls/hr** | **\~75% KV Cache Hit Rate** | **\~50k out tok/hr** | **\~1.7s / min (0ms on Unity main thread)** |

## **9\. Implementation Phases & Milestones**

### **Phase 1: Async Plumbing & ThinkTree Foundation (Weeks 1–3)**

* \[ \] Set up AIColonists.csproj targeting RimWorld 1.5/1.6 (Assembly-CSharp.dll, 0Harmony.dll)

* \[ \] Implement LlamaServerClient using System.Net.Http.HttpClient with async connection pooling, timeout cancellation (3000ms), and GBNF json\_schema payload support

* \[ \] Implement ColonistIntentBuffer (ConcurrentDictionary\<string, QueuedDesire\>)

* \[ \] Write AffordanceScanner to extract top 5 reachable JobDef \+ LocalTargetInfo options on the main thread

* \[ \] Create XML patch inserting JobGiver\_AIColonistDesire into MainColonistBehaviorCore

* **Deliverable:** 3 colonists autonomously picking real tasks via async llama-server calls with 0.0ms frame-time impact and instant drafting override.

### **Phase 2: Differentiation, Sensory Filter & Prefix Caching (Weeks 4–6)**

* \[ \] Build AIColonistWorldComponent with IExposable save/load persistence

* \[ \] Implement SensoryFramingBuilder (translating Pawn\_NeedsTracker, HediffSet pain/scars, and room stats into zero-mechanic prose)

* \[ \] Structure prompt builder into Static Prefix (\# IDENTITY) \+ Dynamic Suffix (\# CURRENT EXPERIENCE) to activate llama-server \--cache-reuse

* \[ \] Implement Intellect-to-Horizon and Mood-to-Entropy (temperature \+ min\_p) mappers

* **Deliverable:** 10 colonists with distinct inner voices, speech bubbles, and priorities across save/load cycles.

### **Phase 3: Layer 2 Social Engine & Layer 3 Memory Drift (Weeks 7–9)**

* \[ \] Implement AreaBatcher grouping colonists by Room or 12-tile outdoor radius

* \[ \] Add Harmony Postfix on Pawn\_InteractionsTracker.TryInteractWith and MemoryThoughtHandler.TryGainMemory

* \[ \] Implement Layer 2 batched social calls and custom InteractionWorker triggers

* \[ \] Implement staggered Layer 3 background consolidation (pruning episodic logs older than 30 days into semantic summaries and applying ±0.02 trait drift)

* **Deliverable:** Full 3-layer cognitive architecture with evolving relationships and personalities.

### **Phase 4: UI Inspector, Diagnostics & Workshop Polish (Weeks 10–12)**

* \[ \] Build a custom ITab\_Pawn\_AIMind inspection tab on colonists showing their live Inner Monologue, Long-Term Desire, Continuous Trait Drift bars, and Relationship Vectors

* \[ \] Add Mod Settings menu (server URL, model names, Dual-Clock intervals, VRAM preset selector)

* \[ \] Stress-test 50-colonist colony with dynamic district batching fallback

* **Deliverable:** Production release.

## **10\. Risk Analysis & Mitigation**

| Risk | Severity | Likelihood | Mitigation in v2.0 |
| :---- | :---- | :---- | :---- |
| **Unity Main-Thread Frame Drops** | Critical | Eliminated | Zero synchronous IO; JobGiver\_AIColonistDesire only performs a non-blocking ConcurrentDictionary.TryRemove check (\<0.01ms) |
| **Target Desync (Item consumed while LLM is thinking)** | High | Medium | When async response arrives (150ms later), QueuedDesire.IsValidAndReservable(pawn) re-verifies \!target.ThingDestroyed && pawn.CanReserveAndReach(target) before making the job |
| **Game Paused / 3x Speed Call Spam** | High | Eliminated | Dual-Clock Gate requires both TicksGame delta and real-time Stopwatch delta |
| **Invalid / Hallucinated JSON** | High | Eliminated | Enforced at token sampling level via llama-server GBNF json\_schema; regex stripper removes \<think\>...\</think\> blocks if using R1-Distill models |
| **Mod Conflict with Work Tab / CommonSense** | Medium | Medium | Because JobGiver\_AIColonistDesire yields standard Verse.AI.Job instances through the native ThinkTree, sub-job mods like *CommonSense* or *Pick Up And Haul* still hook cleanly |
| **VRAM Exhaustion on Single 16GB P100** | Medium | Low | Single-P100 preset shares a single Qwen2.5-3B (Q8\_0) for Layers 1/2 and Qwen2.5-7B (Q4\_K\_M) for Layer 3 with FP16 KV cache (\-ctk f16 \-ctv f16), capping VRAM at \~10.5 GB |

## **11\. Testing Strategy**

### **11.1 Automated & Diagnostic Harness Tests**

| Test | Method | Pass Criteria |
| :---- | :---- | :---- |
| **Main-Thread Tick Budget** | Profile JobGiver\_AIColonistDesire.TryGiveJob across 10,000 ticks using Dubs Performance Analyzer | Average execution time \< 0.02ms per tick; zero spikes \> 0.5ms |
| **Pause & 4x Speed Invariance** | Leave game paused for 5 minutes; then run at 4x speed for 5 minutes | 0 calls fired while paused; calls/minute at 4x speed never exceed configured real-time ceiling |
| **Stale Affordance Recovery** | Destroy or forbid the target meal while a Layer 1 HTTP request is in flight | IsValidAndReservable catches stale target cleanly, discards desire without exception, and falls back to vanilla ThinkTree |
| **KV Prefix Cache Hit Rate** | Inspect llama-server slot metrics (prompt\_tokens\_processed vs tokens\_cached) across 20 calls | \> 70% of prompt tokens served from slot KV cache |

## **Appendix A: Reference Tables**

### **A.1 Sensory Framing Translation Rules**

| Vanilla Game State | ❌ Forbidden Mechanical Input | ✅ Injected Sensory Framing |
| :---- | :---- | :---- |
| needs.food.CurLevel \< 0.25 | "hunger: 0.22 (UrgentlyHungry)" | "Your stomach cramps sharply; you haven't eaten in hours." |
| needs.rest.CurLevel \< 0.20 | "rest: 0.18 (Exhausted)" | "Your eyelids feel like lead and your legs ache with every step." |
| health.hediffSet.PainTotal \> 0.3 | "pain: 0.35, source: GunShot Torso" | "A dull, throbbing heat radiates from the bandaged wound in your chest." |
| needs.mood.thoughts (AteWithoutTable) | "thought: AteWithoutTable (-3)" | "You're still annoyed about having to crouch and eat off the cold floor earlier." |
| RoomStatDefOf.Cleanliness \< \-0.5 | "room\_cleanliness: \-0.65" | "The floor here is crusted with dried grime and smells faintly sour." |

## **Appendix B: Updated Mod Project File Structure**

Plaintext

```
AIColonists/
├── About/
│   └── About.xml
├── Defs/
│   ├── ThinkTreeDefs/
│   │   └── AIColonist_ThinkTreePatch.xml   # Injects ThinkNode below Drafted/QueuedJob
│   └── InteractionDefs/
│       └── AIColonist_Interactions.xml     # Custom LLM-driven social interactions
├── Source/
│   ├── AIColonists.csproj
│   ├── Core/
│   │   ├── AIColonistMod.cs                # Mod entry point & Harmony initialization
│   │   ├── AIColonistGameComponent.cs      # Dual-Clock scheduler & async worker pool
│   │   ├── AIColonistWorldComponent.cs     # IExposable save/load state dictionary
│   │   ├── ColonistIntentBuffer.cs         # Thread-safe ConcurrentDictionary<string, QueuedDesire>
│   │   └── AreaBatcher.cs                  # Room & 12-tile cluster batching
│   ├── Inference/
│   │   ├── ILlamaBridge.cs                 # Async interface for Layer 1/2/3 calls
│   │   ├── LlamaServerClient.cs            # HTTP/SSE client with GBNF json_schema & prefix cache order
│   │   └── GbnfSchemas.cs                  # Strict JSON schemas for Layers 1, 2, and 3
│   ├── ThinkTree/
│   │   ├── ThinkNode_ConditionalAIColonist.cs
│   │   ├── JobGiver_AIColonistDesire.cs    # Non-blocking (<0.01ms) desire consumer
│   │   └── AffordanceScanner.cs            # Scans reachable Things/WorkGivers on main thread
│   ├── Prompts/
│   │   ├── PrefixPromptBuilder.cs          # Static prefix + dynamic suffix assembly
│   │   └── SensoryFramingBuilder.cs        # Translates Needs, Hediffs, and RoomStats to prose
│   ├── State/
│   │   ├── ColonistMindState.cs            # IExposable per-pawn personality & memory store
│   │   ├── RelationshipVector.cs           # 6-axis directional relationship state
│   │   └── MemoryStore.cs                  # Episodic, Semantic, Procedural, Emotional lists
│   ├── Patches/
│   │   ├── InteractionTrackerPatches.cs    # Hooks TryInteractWith
│   │   ├── ThoughtHandlerPatches.cs        # Hooks TryGainMemory for event triggers
│   │   └── DraftControllerPatches.cs       # Purges intent buffer on draft toggle
│   └── UI/
│       ├── ITab_Pawn_AIMind.cs             # In-game inspection tab for inner monologue & drift
│       └── MoteBubbleUtility.cs            # Renders inner_voice / spoken_mutter bubbles
```


