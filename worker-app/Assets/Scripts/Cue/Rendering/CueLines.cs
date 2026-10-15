using UnityEngine;

namespace CoGaze.Worker.Cue.Rendering
{
    /// <summary>LineRenderer helpers shared by the three renderers.</summary>
    static class CueLines
    {
        public static LineRenderer Create(Transform parent, string name, bool loop, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.widthMultiplier = CuePalette.LineWidthM;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        public static void SetOpacity(LineRenderer lr, float opacity)
        {
            var c = CuePalette.Primary;
            c.a = opacity;
            lr.startColor = c;
            lr.endColor = c;
        }

        /// <summary>A circle on the plane through centre with the given normal.</summary>
        public static void Circle(LineRenderer lr, Vector3 centre, Vector3 normal, float radius)
        {
            var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var bitangent = Vector3.Cross(normal, tangent);
            int n = CuePalette.CircleSegments;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                // lift a millimetre so the line does not z-fight with a scanned surface
                lr.SetPosition(i, centre + normal * 0.001f + radius * (Mathf.Cos(a) * tangent + Mathf.Sin(a) * bitangent));
            }
        }
    }
}
