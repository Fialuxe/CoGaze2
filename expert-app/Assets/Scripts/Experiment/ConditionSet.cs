using System;
using System.Collections.Generic;
using System.Linq;
using CoGaze.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoGaze.Expert.Experiment
{
    /// <summary>
    /// conditions.json: the condition list and each participant's order (EXP-F-054, 055). Every
    /// problem is collected, so the experimenter sees all of them at once; an invalid file stops
    /// start-up rather than running with defaults.
    /// </summary>
    public sealed class ConditionSet
    {
        public IReadOnlyDictionary<string, Condition> Conditions { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<PlanEntry>> Orders { get; }

        ConditionSet(Dictionary<string, Condition> conditions, Dictionary<string, IReadOnlyList<PlanEntry>> orders)
        {
            Conditions = conditions;
            Orders = orders;
        }

        /// <summary>Sources a condition may name. "webcam:&lt;estimator&gt;" is accepted for any estimator.</summary>
        public static bool IsKnownSource(string s) =>
            s == "beam" || s == "tobii" || s == "replay" || s == Condition.NoSource ||
            (s != null && s.StartsWith("webcam:", StringComparison.Ordinal) && s.Length > "webcam:".Length);

        public static ConditionSet Parse(string json, ICollection<string> filterProfiles, List<string> errors)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (JsonException e)
            {
                errors.Add($"conditions.json: {e.Message}");
                return null;
            }

            var conditions = new Dictionary<string, Condition>();
            var seen = new HashSet<string>();
            if (!(root["conditions"] is JArray list) || list.Count == 0)
                errors.Add("conditions.json: \"conditions\" must be a non-empty list.");
            else
                for (int i = 0; i < list.Count; i++)
                    ParseCondition(list[i] as JObject, i, filterProfiles, seen, conditions, errors);

            var orders = new Dictionary<string, IReadOnlyList<PlanEntry>>();
            if (!(root["orders"] is JObject ord) || !ord.HasValues)
                errors.Add("conditions.json: \"orders\" must map participant ids to lists.");
            else
                foreach (var p in ord.Properties())
                    orders[p.Name] = ParseOrder(p, conditions, errors);

            return errors.Count == 0 ? new ConditionSet(conditions, orders) : null;
        }

        static void ParseCondition(JObject o, int i, ICollection<string> filters, HashSet<string> seen,
                                   Dictionary<string, Condition> into, List<string> errors)
        {
            string where = $"conditions[{i}]";
            if (o == null) { errors.Add($"{where} is not an object."); return; }

            string id = (string)o["condition_id"];
            if (string.IsNullOrWhiteSpace(id)) { errors.Add($"{where}: condition_id is missing."); return; }
            where = $"condition {id}";
            if (!seen.Add(id)) { errors.Add($"{where}: duplicate condition_id."); return; }

            int before = errors.Count;
            string source = (string)o["display_source"];
            if (!IsKnownSource(source)) errors.Add($"{where}: unknown display_source \"{source}\".");

            Visualization vis = Visualization.Circle;
            string visName = (string)o["visualization"];
            switch (visName)
            {
                case "ray": vis = Visualization.Ray; break;
                case "circle": vis = Visualization.Circle; break;
                case "frustum": vis = Visualization.Frustum; break;
                default: errors.Add($"{where}: visualization must be ray, circle or frustum, got \"{visName}\"."); break;
            }

            var showTok = o["show_uncertainty"];
            if (showTok == null || showTok.Type != JTokenType.Boolean) errors.Add($"{where}: show_uncertainty must be true or false.");

            SigmaSource sigma = SigmaSource.Fixed;
            string sigmaName = (string)o["sigma_source"];
            switch (sigmaName)
            {
                case "fixed": sigma = SigmaSource.Fixed; break;
                case "calib": sigma = SigmaSource.Calib; break;
                case "calib_live": sigma = SigmaSource.CalibLive; break;
                default: errors.Add($"{where}: sigma_source must be fixed, calib or calib_live, got \"{sigmaName}\"."); break;
            }

            float fixedSigma = float.NaN;
            var fixedTok = o["fixed_sigma"];
            if (fixedTok != null && (fixedTok.Type == JTokenType.Float || fixedTok.Type == JTokenType.Integer))
                fixedSigma = (float)fixedTok;
            if (sigma == SigmaSource.Fixed && !(fixedSigma > 0))
                errors.Add($"{where}: sigma_source is fixed but fixed_sigma is missing or not positive.");

            string filter = (string)o["filter"];
            if (string.IsNullOrEmpty(filter) || !filters.Contains(filter))
                errors.Add($"{where}: filter \"{filter}\" is not a profile in app.json.");

            string task = (string)o["task"];
            if (string.IsNullOrWhiteSpace(task)) errors.Add($"{where}: task is missing.");

            if (errors.Count == before)
                into[id] = new Condition(id, source, vis, (bool)showTok, sigma, fixedSigma, filter, task);
        }

        static IReadOnlyList<PlanEntry> ParseOrder(JProperty p, Dictionary<string, Condition> conditions, List<string> errors)
        {
            var plan = new List<PlanEntry>();
            if (!(p.Value is JArray steps) || steps.Count == 0)
            {
                errors.Add($"orders.{p.Name}: must be a non-empty list.");
                return plan;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                string where = $"orders.{p.Name}[{i}]";
                string id = (string)steps[i]["condition_id"];
                string kind = (string)steps[i]["kind"] ?? "main";
                if (id == null || !conditions.ContainsKey(id)) errors.Add($"{where}: unknown condition_id \"{id}\".");
                TrialKind k;
                switch (kind)
                {
                    case "tutorial": k = TrialKind.Tutorial; break;
                    case "practice": k = TrialKind.Practice; break;
                    case "main": k = TrialKind.Main; break;
                    default: errors.Add($"{where}: kind must be tutorial, practice or main."); continue;
                }
                plan.Add(new PlanEntry(id, k));
            }
            if (!plan.Any(e => e.Kind == TrialKind.Main)) errors.Add($"orders.{p.Name}: has no main trial.");
            return plan;
        }
    }
}
