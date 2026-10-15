using System.Linq;
using System.Text.RegularExpressions;
using CoGaze.Core;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class SchemaTests
    {
        static readonly Regex Unitless = new Regex("^(t|x|y|z)$");

        [Test]
        public void EveryTable_HasUniqueSnakeCaseColumns_StartingWithTime()
        {
            foreach (var table in LogSchema.Tables)
            {
                var cols = LogSchema.ColumnsOf(table);
                Assert.IsNotNull(cols, table);
                Assert.AreEqual(cols.Length, cols.Distinct().Count(), $"{table}: duplicate column");
                Assert.IsTrue(cols.All(c => Regex.IsMatch(c, "^[a-z][a-z0-9_]*$")), $"{table}: column not snake_case");
                Assert.IsFalse(cols.Any(c => Unitless.IsMatch(c)), $"{table}: bare coordinate without unit");
                Assert.IsTrue(cols[0] == "t_ns" || cols[0] == "video_frame_id", $"{table}: first column");
            }
        }

        [Test]
        public void ColumnsOf_ReturnsACopy()
        {
            LogSchema.ColumnsOf(LogSchema.Events)[0] = "changed";
            Assert.AreEqual("t_ns", LogSchema.ColumnsOf(LogSchema.Events)[0]);
        }

        [Test]
        public void Csv_QuotesOnlyWhenNeeded()
        {
            Assert.AreEqual("1,a,\"b,c\",,0.5,1", Csv.Row(1, "a", "b,c", float.NaN, 0.5f, true));
        }
    }
}
