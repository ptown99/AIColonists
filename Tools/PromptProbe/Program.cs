// ============================================================================
//  OLLAMA E2E PROBE — run with:  dotnet run --project Tools/PromptProbe [endpoint] [model]
// ----------------------------------------------------------------------------
//  Defaults: http://127.0.0.1:11434/v1/chat/completions  qwen2.5:3b
//
//  What it proves (the inference half of T4, no game needed):
//   1. The endpoint serves the model and accepts the mod's EXACT wire payload
//      (byte-identical to what LlamaServerClient sends in-game — built by the
//      real BuildChatCompletionsJson, parsed by the real ParseLayer1).
//   2. Responses are schema-valid: impulse_id lands in 1..N every time.
//   3. Latency sits under the 3000 ms dispatch budget (first call = cold;
//      repeats = prefix-cache evidence — Ollama caches identical prefixes).
//   4. Differentiation: two different identities facing IDENTICAL affordances
//      produce different choices/voices, and mood-mapped sampling (content vs
//      furious) shifts tone — the whole point of Phase 2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AIColonists.Core;
using AIColonists.Inference;
using AIColonists.Prompts;
using AIColonists.ThinkTree;
using RimWorld;
using Verse;

namespace AIColonists.Probe
{
    public static class Program
    {
        const int RunsPerPersona = 3;

        public static async Task<int> Main(string[] args)
        {
            string endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:11434/v1/chat/completions";
            string model = args.Length > 1 ? args[1] : "qwen2.5:3b";

            Console.WriteLine("AIColonists — Ollama E2E probe");
            Console.WriteLine("endpoint: " + endpoint);
            Console.WriteLine("model:    " + model);
            Console.WriteLine();

            // Wire the world component exactly like the mod entry point does, so identity capture sees real mind states.
            Find.World.RegisterForTests(new AIColonistWorldComponent(Find.World));

            var settings = new Settings(); // defaults mirror a fresh install: baseTemp 0.65, minP 0.08, maxTokens 80
            var client = new LlamaServerClient(settings); // only for ParseLayer1 (private — via reflection, as the harness does)
            var parseMi = typeof(LlamaServerClient).GetMethod("ParseLayer1", BindingFlags.NonPublic | BindingFlags.Instance);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) }; // cold model load can exceed the in-game 3 s budget; that is a finding, not a crash

            // --- Personas: identical affordances, different identities + moods. Any difference is the model's doing.
            var alice = MakePersona("Alice", 42, "Stoic", 1, new[] { ("mining", 9), ("cooking", 3) }, 8, 0.55f); // content
            var bob = MakePersona("Bob", 27, "Quick-Tempered", -2, new[] { ("cooking", 9), ("mining", 2) }, 3, 0.10f); // furious

            List<AffordanceOption> options = new List<AffordanceOption>
            {
                MakeOpt(1, "Eat the hot simple meal on the dining table.", JobDefOf.Ingest),
                MakeOpt(2, "Mine the exposed steel vein nearby.", new JobDef()),
                MakeOpt(3, "Haul the wooden box to storage.", new JobDef()),
                MakeOpt(4, "Lie down on the bed.", JobDefOf.LayDown),
            };

            // Print what the model actually sees (once) — this is the Phase 2 identity prefix in the wild.
            var aliceSnap = Capture(alice, options);
            Console.WriteLine("===== Alice's system prompt (static identity prefix) =====");
            Console.WriteLine(PrefixPromptBuilder.BuildSystemPrompt(aliceSnap));
            Console.WriteLine();

            int failures = 0;
            var report = new List<string>();

            foreach (var persona in new[] { ("Alice", alice, aliceSnap), ("Bob", bob, Capture(bob, options)) })
            {
                string name = persona.Item1;
                SensorySnapshot snap = persona.Item3;
                float temp = MoodEntropyMapper.MoodToTemperature(settings.BaseTemperature, snap.MoodPercentage);
                float topP = MoodEntropyMapper.MoodToTopP(snap.MoodPercentage);

                Console.WriteLine("===== " + name + " — mood " + snap.MoodPercentage.ToString("0.00") +
                                  " → tier " + MoodEntropyMapper.MoodTier(snap.MoodPercentage) +
                                  ", temperature " + temp.ToString("0.00") + ", top_p " + topP.ToString("0.00") + " =====");

                for (int run = 1; run <= RunsPerPersona; run++)
                {
                    var req = new Layer1Request
                    {
                        model = model,
                        systemPrompt = PrefixPromptBuilder.BuildSystemPrompt(snap),
                        userPrompt = PrefixPromptBuilder.BuildUserPrompt(snap),
                        temperature = temp,
                        topP = topP,
                        minP = settings.MinP,
                        maxTokens = settings.Layer1MaxOutputTokens,
                        optionCount = options.Count,
                    };

                    string body = LlamaServerClient.BuildChatCompletionsJson(req); // byte-exact in-game payload

                    var sw = Stopwatch.StartNew();
                    string raw;
                    int status;
                    try
                    {
                        using var resp = await http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
                        status = (int)resp.StatusCode;
                        raw = await resp.Content.ReadAsStringAsync();
                    }
                    catch (Exception e)
                    {
                        sw.Stop();
                        failures++;
                        Console.WriteLine("  run " + run + ": TRANSPORT FAILURE after " + sw.ElapsedMilliseconds + " ms — " + e.Message);
                        continue;
                    }
                    sw.Stop();

                    if (status != 200)
                    {
                        failures++;
                        string excerpt = raw.Length > 300 ? raw.Substring(0, 300) + "…" : raw;
                        Console.WriteLine("  run " + run + ": HTTP " + status + " after " + sw.ElapsedMilliseconds + " ms — " + excerpt);
                        continue;
                    }

                    Layer1Result result = null;
                    string parseError = null;
                    try { result = (Layer1Result)parseMi.Invoke(client, new object[] { raw }); }
                    catch (TargetInvocationException tie) { parseError = tie.InnerException?.Message ?? tie.Message; }

                    bool inRange = result != null && result.impulse_id >= 1 && result.impulse_id <= options.Count;
                    if (result == null || !inRange) failures++;

                    string choice = inRange ? options[result.impulse_id - 1].sensoryDescription : "INVALID (" + (result == null ? "unparseable" : result.impulse_id.ToString()) + ")";
                    Console.WriteLine("  run " + run + ": " + sw.ElapsedMilliseconds + " ms — impulse_id=" + (result?.impulse_id ?? -1) +
                                      " → \"" + choice + "\"");
                    Console.WriteLine("           inner_voice=\"" + (result?.inner_voice ?? "") + "\"" +
                                      (string.IsNullOrEmpty(result?.spoken_mutter) ? "" : "  mutter=\"" + result.spoken_mutter + "\""));
                    if (run == 1)
                        Console.WriteLine("           raw content: " + FirstContent(raw));

                    report.Add(name + "|" + run + "|" + sw.ElapsedMilliseconds + "|" + (result?.impulse_id ?? -1) + "|" + choice);
                }
                Console.WriteLine();
            }

            // --- Summary
            Console.WriteLine("===== Summary =====");
            var byPersona = report.GroupBy(r => r.Split('|')[0]);
            foreach (var g in byPersona)
            {
                var ms = g.Select(r => int.Parse(r.Split('|')[2])).ToList();
                var ids = g.Select(r => int.Parse(r.Split('|')[3])).ToList();
                Console.WriteLine(g.Key + ": avg " + (ms.Average()).ToString("0") + " ms (first " + ms.First() + ", last " + ms.Last() +
                                  ") — choices: " + string.Join(", ", ids.Select(i => i <= options.Count ? options[i - 1].sensoryDescription.Split(' ')[0] : "?").Distinct()));
            }
            Console.WriteLine();
            if (failures == 0)
                Console.WriteLine("PASS — all responses parsed, all impulse_ids in range.");
            else
                Console.WriteLine(failures + " FAILURE(S) — see lines above.");

            // Prefix-cache note: Ollama caches identical prefixes automatically; if the model is already warm,
            // run 2/3 for a persona should be markedly faster than run 1 (the spec's ~70% prefill reduction).
            return failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- helpers

        static AffordanceOption MakeOpt(int id, string desc, JobDef jobDef) => new AffordanceOption
        {
            optionId = id,
            sensoryDescription = desc,
            jobDef = jobDef,
            targetA = default(LocalTargetInfo),
        };

        /// <summary>Builds a full stub pawn with the real identity surface (age/traits/skills/mood) + colony name.</summary>
        static Pawn MakePersona(string label, int age, string traitLabel, int degree, (string skill, int level)[] skills, int intellect, float moodPct)
        {
            var map = new Map();
            map.Parent = new RimWorld.Planet.Settlement { Name = "Home" };
            Find.CurrentMap = map;

            var pawn = new Pawn { ThingID = label + "-probe", Faction = Faction.OfPlayer, LabelCap = label };
            pawn.Map = map;
            map.mapPawns.AllPawnsSpawned.Add(pawn);
            pawn.ageTracker.AgeBiologicalYears = age;
            pawn.story.traits.allTraits.Add(new Trait { LabelCap = traitLabel, Degree = degree });
            foreach (var (skill, level) in skills)
                pawn.skills.skills.Add(new SkillRecord { def = new SkillDef { label = skill }, levelInt = level });
            pawn.skills.skills.Add(new SkillRecord { def = SkillDefOf.Intellectual, levelInt = intellect });
            pawn.needs.mood.CurLevelPercentage = moodPct;
            return pawn;
        }

        static SensorySnapshot Capture(Pawn pawn, List<AffordanceOption> options) =>
            SensoryFramingBuilder.CaptureMainThreadSnapshot(pawn, options);

        /// <summary>Extracts choices[0].message.content from the raw envelope for display (best-effort).</summary>
        static string FirstContent(string raw)
        {
            int i = raw.IndexOf("\"content\"");
            if (i < 0) return raw.Length > 200 ? raw.Substring(0, 200) + "…" : raw;
            string s = raw.Substring(i);
            int start = s.IndexOf('"', 11); // after "content":
            if (start < 0) return "(unparsed)";
            var sb = new StringBuilder();
            for (int j = start + 1; j < s.Length; j++)
            {
                char c = s[j];
                if (c == '\\' && j + 1 < s.Length) { sb.Append(s[j + 1]); j++; continue; }
                if (c == '"') break;
                sb.Append(c);
            }
            string content = sb.ToString();
            return content.Length > 260 ? content.Substring(0, 260) + "…" : content;
        }
    }
}
