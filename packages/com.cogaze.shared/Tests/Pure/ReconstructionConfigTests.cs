using System.Collections.Generic;
using CoGaze.Config;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    /// <summary>app.json "reconstruction": where the 3D view gets the worker's space from.</summary>
    public class ReconstructionConfigTests
    {
        static List<string> Errors(string view, string reconstruction)
        {
            var errors = new List<string>();
            AppConfigLoader.Parse($@"{{
                ""view"": ""{view}"",
                ""video_rect_px"": {{ ""x"": 160, ""y"": 90, ""width"": 1600, ""height"": 900 }},
                ""filters"": {{ ""default"": {{ }} }}
                {(reconstruction == null ? "" : @", ""reconstruction"": " + reconstruction)}
            }}", errors);
            return errors;
        }

        [Test]
        public void Absent_IsFineFor2D_AndDefaultsToNothing()
        {
            CollectionAssert.IsEmpty(Errors("2d", null));
            var c = AppConfigLoader.Parse(@"{ ""filters"": { ""default"": { } }, ""video_rect_px"": { ""width"": 10, ""height"": 10 } }", new List<string>());
            Assert.AreEqual("", c.Reconstruction.Url);
            Assert.AreEqual("", c.Reconstruction.StagingDir);
        }

        [Test]
        public void View3D_NeedsASource()
        {
            Assert.AreEqual(1, Errors("3d", null).Count);
            CollectionAssert.IsEmpty(Errors("3d", @"{ ""url"": ""ws://192.168.1.10:8765/expert"" }"));
            CollectionAssert.IsEmpty(Errors("3d", @"{ ""staging_dir"": ""recordings/synthetic_chunks"" }"));
        }

        [Test]
        public void Url_MustBeAWebSocket_AndNotBothSources()
        {
            Assert.AreEqual(1, Errors("2d", @"{ ""url"": ""http://pc:8765/expert"" }").Count);
            Assert.AreEqual(1, Errors("2d", @"{ ""url"": ""ws://pc:8765/expert"", ""staging_dir"": ""x"" }").Count);
            Assert.AreEqual(1, Errors("2d", @"{ ""staging_dir"": ""x"", ""replay_interval_s"": -1 }").Count);
            CollectionAssert.IsEmpty(Errors("2d", @"{ ""url"": ""wss://pc/expert"" }"));
        }
    }
}
