using System.IO;
using System.Runtime.CompilerServices;

namespace CoGaze.Shared.Tests
{
    /// <summary>Locates repository files from this source file's path, in both Unity and dotnet test.</summary>
    public static class TestPaths
    {
        public static string Repo(params string[] parts) => Path.Combine(Root(), Path.Combine(parts));

        static string Root([CallerFilePath] string thisFile = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile));
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "config"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
