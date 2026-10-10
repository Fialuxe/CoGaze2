using CoGaze.Core;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze.Worker.Surface
{
    /// <summary>
    /// A work surface defined by this transform (its up axis is the plane normal). Used in the editor
    /// and as the manual-adjust base; the QR-tracked surface (#23) implements the same interface.
    /// </summary>
    public sealed class ManualWorkSurfaceBehaviour : MonoBehaviour, IWorkSurface
    {
        public AlignmentState State => AlignmentState.Aligned;

        public bool TryGetPlane(out WorkPlane plane)
        {
            var t = transform;
            plane = new WorkPlane(t.position.ToNumerics(), t.up.ToNumerics(), t.right.ToNumerics(), t.forward.ToNumerics());
            return true;
        }
    }
}
