using System;
using System.Globalization;
using System.IO;
using CoGaze.Core;
using Newtonsoft.Json.Linq;

namespace CoGaze.Logging
{
    /// <summary>
    /// Creates sessions/P{id}_{yyyyMMdd-HHmmss}/ and writes session.json before anything else, so
    /// every data file can be traced to the exact settings and code that produced it (COM-F-010..014).
    /// </summary>
    public static class SessionFolder
    {
        public static string NameFor(string participantId, DateTime localStart) =>
            $"P{participantId.TrimStart('P', 'p')}_{localStart.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Creates the folder and session.json. Throws IOException if the folder already exists
        /// (COM-F-012) — nothing is created in that case.
        /// </summary>
        public static string Create(string sessionsRoot, string participantId, DateTime localStart, SessionManifest manifest)
        {
            string path = Path.Combine(sessionsRoot, NameFor(participantId, localStart));
            if (Directory.Exists(path))
                throw new IOException($"Session folder already exists: {path}");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "session.json"), manifest.ToJson(participantId, localStart).ToString());
            return path;
        }
    }

    /// <summary>What session.json records about the run.</summary>
    public sealed class SessionManifest
    {
        public string AppName;
        public string AppVersion;
        public string GitCommit;
        public string AppConfigJson;
        public string ConditionsJson;
        public string[] ConditionOrder = Array.Empty<string>();

        public JObject ToJson(string participantId, DateTime localStart) => new JObject
        {
            ["participant_id"] = participantId,
            ["start_local"] = localStart.ToString("o", CultureInfo.InvariantCulture),
            ["app"] = AppName,
            ["app_version"] = AppVersion,
            ["git_commit"] = GitCommit,
            ["log_format_version"] = LogSchema.Version,
            ["condition_order"] = new JArray(ConditionOrder),
            ["app_config"] = ParseOrRaw(AppConfigJson),
            ["conditions"] = ParseOrRaw(ConditionsJson),
        };

        static JToken ParseOrRaw(string json)
        {
            if (string.IsNullOrEmpty(json)) return JValue.CreateNull();
            try { return JToken.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return json; }
        }
    }
}
