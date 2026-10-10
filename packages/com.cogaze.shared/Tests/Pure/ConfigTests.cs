using System.Collections.Generic;
using CoGaze.Config;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class ConfigTests
    {
        const string Valid = @"{
            ""rehearsal"": true,
            ""view"": ""2d"",
            ""cue_rate_hz"": 30,
            ""monitor"": { ""width_px"": 1920, ""height_px"": 1080, ""width_mm"": 527, ""height_mm"": 296, ""viewing_distance_mm"": 650 },
            ""video_rect_px"": { ""x"": 160, ""y"": 90, ""width"": 1600, ""height"": 900 },
            ""filters"": { ""default"": { ""min_cutoff_hz"": 1.0, ""beta"": 0.007, ""d_cutoff_hz"": 1.0 } }
        }";

        [Test]
        public void ValidConfig_HasNoErrors()
        {
            var errors = new List<string>();
            var c = AppConfigLoader.Parse(Valid, errors);
            CollectionAssert.IsEmpty(errors);
            Assert.IsTrue(c.Rehearsal);
            Assert.AreEqual(1600, c.VideoRectPx.Width);
        }

        [Test]
        public void EveryProblem_IsReported()
        {
            var errors = new List<string>();
            AppConfigLoader.Parse(@"{ ""view"": ""4d"", ""cue_rate_hz"": 60, ""video_rect_px"": { ""x"": 0, ""y"": 0, ""width"": 0, ""height"": 0 }, ""filters"": {} }", errors);
            Assert.GreaterOrEqual(errors.Count, 4);
        }

        [Test]
        public void UnknownField_IsAnError()
        {
            var errors = new List<string>();
            AppConfigLoader.Parse(@"{ ""cue_rate"": 30 }", errors);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void RepositorySample_IsValid()
        {
            string path = TestPaths.Repo("config", "app.json");
            var errors = new List<string>();
            AppConfigLoader.Load(path, errors);
            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
