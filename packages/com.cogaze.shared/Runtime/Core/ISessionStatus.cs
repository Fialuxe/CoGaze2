using System.Collections.Generic;

namespace CoGaze.Core
{
    /// <summary>What the experimenter screen shows about the session.</summary>
    public interface ISessionStatus
    {
        string ParticipantId { get; }
        TrialState State { get; }
        /// <summary>Condition sent, waiting for the worker to echo its id.</summary>
        bool AwaitingWorker { get; }
        int TrialIndex { get; }
        int TrialCount { get; }
        TrialKind CurrentKind { get; }
        long StateSinceNs { get; }
        /// <summary>The condition the next PrepareNext will send; null when the plan is done.</summary>
        string NextConditionId { get; }
        /// <summary>The participant's whole order with each entry's state.</summary>
        IReadOnlyList<PlanItem> Plan { get; }
        /// <summary>Ready, worker agreed on the condition, and the checklist is all green.</summary>
        bool CanStartTrial { get; }
    }
}
