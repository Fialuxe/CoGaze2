using System.Globalization;
using System.Text;

namespace CoGaze.Core
{
    /// <summary>Formats CSV rows the same way everywhere: invariant culture, quoted only when needed.</summary>
    public static class Csv
    {
        public static string Row(params object[] fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Field(fields[i]));
            }
            return sb.ToString();
        }

        public static string Field(object value)
        {
            switch (value)
            {
                case null: return "";
                case bool b: return b ? "1" : "0";
                case float f: return float.IsNaN(f) ? "" : f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture);
                case System.IFormattable x: return Quote(x.ToString(null, CultureInfo.InvariantCulture));
                default: return Quote(value.ToString());
            }
        }

        static string Quote(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
