using System;
using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// The one thing to do next, in words, with the button and key that do it. A four-step track
    /// (send → worker confirms → start → end) shows where in the cycle the session is.
    /// </summary>
    sealed class NextStepView
    {
        static readonly string[] Steps = { "Send condition", "Worker confirms", "Start trial", "End trial" };

        readonly ISessionStatus _session;
        readonly IHealthBoard _health;
        readonly IExperimentControl _control;
        readonly VisualElement[] _dots = new VisualElement[Steps.Length];
        readonly Label[] _stepLabels = new Label[Steps.Length];
        readonly Label _eyebrow, _title, _detail;
        readonly Button _primary;
        Action _action;

        public VisualElement Root { get; }

        public NextStepView(ISessionStatus session, IHealthBoard health, IExperimentControl control)
        {
            _session = session;
            _health = health;
            _control = control;

            Root = Theme.Box("cg-card", "cg-step");
            var track = Theme.Box("cg-track");
            for (int i = 0; i < Steps.Length; i++)
            {
                var step = Theme.Box("cg-track__step");
                _dots[i] = Theme.Box("cg-track__dot");
                step.Add(_dots[i]);
                _stepLabels[i] = Theme.Label(Steps[i], "cg-track__label");
                step.Add(_stepLabels[i]);
                track.Add(step);
            }
            Root.Add(track);

            _eyebrow = Theme.Label("", "cg-card__eyebrow");
            Root.Add(_eyebrow);
            _title = Theme.Label("", "cg-step__title");
            Root.Add(_title);
            _detail = Theme.Label("", "cg-step__detail");
            Root.Add(_detail);
            _primary = new Button(() => _action?.Invoke());
            _primary.AddToClassList("cg-button");
            Root.Add(_primary);
        }

        public void Refresh()
        {
            int n = _session.TrialIndex + 1, count = _session.TrialCount;
            var plan = _session.Plan;
            switch (_session.State)
            {
                case TrialState.Idle:
                case TrialState.InterTrial:
                    if (_session.NextConditionId == null) goto case TrialState.Finished;
                    var next = plan[NextIndex(plan)];
                    Set(0, $"TRIAL {next.Index + 1} OF {count} · {Describe.Kind(next.Kind).ToUpperInvariant()}",
                        $"Send condition {next.Condition.ConditionId} to the worker",
                        $"The worker will see: {Describe.Condition(next.Condition)}. Nothing starts until the worker app confirms it.",
                        "Send condition   N", true, _control.PrepareNext);
                    break;
                case TrialState.Ready when _session.AwaitingWorker:
                    Set(1, $"TRIAL {n} OF {count}", "Waiting for the worker app",
                        "The condition was sent; the worker app has not confirmed it yet. If this takes more than a few seconds, send it again.",
                        "Send again   N", true, _control.PrepareNext);
                    break;
                case TrialState.Ready when !_health.AllOk:
                    Set(2, $"TRIAL {n} OF {count}", "Fix the red items first",
                        "The checklist below has problems. Each red row says what to do. The trial can start once all rows are green.",
                        "Start trial   Space", false, null);
                    break;
                case TrialState.Ready:
                    Set(2, $"TRIAL {n} OF {count} · {Describe.Kind(_session.CurrentKind).ToUpperInvariant()}", "Ready — start the trial",
                        "Brief both participants, then start. During the trial this panel hides so the expert sees only the video (F1 shows it).",
                        "Start trial   Space", true, _control.StartTrial);
                    break;
                case TrialState.InTrial:
                    Set(3, $"TRIAL {n} OF {count}", "Trial running",
                        "End it when the task is done. Shift+A aborts instead; an aborted trial is run again.",
                        "End trial   Space", true, _control.EndTrial);
                    break;
                case TrialState.Finished:
                    Set(4, "SESSION COMPLETE", "All trials are done",
                        "Data is in the session folder. You can close the app.", "Done", false, null);
                    break;
            }
        }

        static int NextIndex(System.Collections.Generic.IReadOnlyList<PlanItem> plan)
        {
            for (int i = 0; i < plan.Count; i++)
                if (plan[i].State != PlanItemState.Done) return i;
            return plan.Count - 1;
        }

        void Set(int step, string eyebrow, string title, string detail, string button, bool enabled, Action action)
        {
            for (int i = 0; i < Steps.Length; i++)
            {
                _dots[i].EnableInClassList("cg-track__dot--done", i < step);
                _dots[i].EnableInClassList("cg-track__dot--now", i == step);
                _stepLabels[i].EnableInClassList("cg-track__label--now", i == step);
            }
            _eyebrow.text = eyebrow;
            _title.text = title;
            _detail.text = detail;
            _primary.text = button;
            _primary.SetEnabled(enabled);
            _action = action;
        }
    }
}
