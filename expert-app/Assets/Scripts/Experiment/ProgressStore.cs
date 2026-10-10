using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CoGaze.Expert.Experiment
{
    /// <summary>
    /// progress.json in the session folder: the next plan index, written after every trial. After a
    /// crash, starting again with the same participant id resumes from the latest session's file.
    /// </summary>
    public static class ProgressStore
    {
        public const string FileName = "progress.json";

        public static void Save(string sessionDir, string participantId, int nextIndex, int completedMain)
        {
            string path = Path.Combine(sessionDir, FileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, new JObject
            {
                ["participant_id"] = participantId,
                ["next_index"] = nextIndex,
                ["completed_main"] = completedMain,
            }.ToString());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);   // never leave a half-written progress file
        }

        /// <summary>Next index from the newest earlier session of this participant; 0 when there is none.</summary>
        public static int FindResumeIndex(string sessionsRoot, string participantId, out string fromSession)
        {
            fromSession = null;
            if (!Directory.Exists(sessionsRoot)) return 0;
            string prefix = $"P{participantId.TrimStart('P', 'p')}_";
            var latest = Directory.GetDirectories(sessionsRoot, prefix + "*")
                .Where(d => File.Exists(Path.Combine(d, FileName)))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .FirstOrDefault();
            if (latest == null) return 0;
            fromSession = Path.GetFileName(latest);
            var o = JObject.Parse(File.ReadAllText(Path.Combine(latest, FileName)));
            return (int?)o["next_index"] ?? 0;
        }
    }
}
