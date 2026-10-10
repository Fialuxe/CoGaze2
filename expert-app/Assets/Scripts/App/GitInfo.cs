using System;
using System.IO;
using System.Linq;

namespace CoGaze.Expert.App
{
    /// <summary>The commit the running code came from, read from .git without spawning git (COM-F-014).</summary>
    static class GitInfo
    {
        public static string Head(string repoRoot)
        {
            try
            {
                string git = Path.Combine(repoRoot, ".git");
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref: ")) return head;
                string reference = head.Substring(5);
                string loose = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
                string packed = Path.Combine(git, "packed-refs");
                var line = File.Exists(packed) ? File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + reference)) : null;
                return line?.Split(' ')[0] ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";   // builds run without the repository next to them
            }
        }
    }
}
