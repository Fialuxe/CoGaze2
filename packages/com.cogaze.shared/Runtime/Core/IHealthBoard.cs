using System.Collections.Generic;

namespace CoGaze.Core
{
    /// <summary>
    /// Health of everything the experiment depends on, shown as the checklist. Problems are reported
    /// here and in events.csv, never silently corrected.
    /// </summary>
    public interface IHealthBoard
    {
        void Set(in HealthItem item);
        IReadOnlyList<HealthItem> Items { get; }
        /// <summary>True when every item is Ok — the start gate (EXP-F-002).</summary>
        bool AllOk { get; }
    }
}
