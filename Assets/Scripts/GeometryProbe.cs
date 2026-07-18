using System.Collections;
using System.Text;
using CoGaze2.EnvLine;
using UnityEngine;
using UnityEngine.Android;
#if UNITY_ANDROID && !UNITY_EDITOR
using Meta.XR;
#endif

/// <summary>
/// M-1 + M0 on-device probe. Throwaway: delete once the geometry is validated and the numbers
/// are recorded in docs/geometry_validation.md.
///
/// Logs the values M0 needs (M-1), then runs:
///   Test A — our CameraGeometry against MRUK's WorldToViewportPoint oracle, both half-pixel
///            variants. Gate: max error &lt; 0.5 px. Proves the crop algebra, NOT lens distortion
///            and NOT pose/image time alignment (both use the same pose and the same pinhole).
///   Test C — stereo viability: L/R timestamp delta and baseline. Measurement, not a decision.
///
/// Test B (controller crosshair, the real gate) needs the server round-trip and comes with M1.
/// </summary>
public class GeometryProbe : MonoBehaviour
{
    const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

    [SerializeField] Vector2Int requestedResolution = new Vector2Int(1280, 960);
    [SerializeField] int testAPointCount = 1000;

#if UNITY_ANDROID && !UNITY_EDITOR
    PassthroughCameraAccess _left, _right;
#endif

    IEnumerator Start()
    {
        Debug.Log($"[Probe] device={SystemInfo.deviceModel} os={SystemInfo.operatingSystem}");

#if UNITY_ANDROID && !UNITY_EDITOR
        // Single gate. Nothing downstream re-checks; if we get past here the camera works.
        if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
        {
            Permission.RequestUserPermission(HeadsetCameraPermission);
            yield return new WaitForSeconds(1f); // permission dialog is async, no callback we can await
        }
        if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
            !PassthroughCameraAccess.IsSupported)
        {
            Debug.LogError($"[Probe] passthrough camera unavailable " +
                           $"(permission={Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)}, " +
                           $"supported={PassthroughCameraAccess.IsSupported}). Disabling.");
            enabled = false;
            yield break;
        }

        LogSupportedResolutions();

        _left = Attach(PassthroughCameraAccess.CameraPositionType.Left);
        _right = Attach(PassthroughCameraAccess.CameraPositionType.Right);

        // Both cameras need a few frames before IsPlaying flips and Intrinsics are populated.
        yield return new WaitUntil(() => _left.IsPlaying && _right.IsPlaying);

        LogIntrinsics("L", _left);
        LogIntrinsics("R", _right);

        RunTestA(_left);
        StartCoroutine(RunTestC());
#else
        Debug.LogWarning("[Probe] editor build — device probe skipped. " +
                         "Run the EditMode tests for the offline geometry checks.");
        yield break;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    PassthroughCameraAccess Attach(PassthroughCameraAccess.CameraPositionType position)
    {
        var pca = gameObject.AddComponent<PassthroughCameraAccess>();
        pca.CameraPosition = position;
        pca.RequestedResolution = requestedResolution;
        return pca;
    }

    static void LogSupportedResolutions()
    {
        foreach (var pos in new[] { PassthroughCameraAccess.CameraPositionType.Left,
                                    PassthroughCameraAccess.CameraPositionType.Right })
        {
            var sb = new StringBuilder($"[Probe] supported {pos}: ");
            foreach (var r in PassthroughCameraAccess.GetSupportedResolutions(pos)) sb.Append($"{r.x}x{r.y} ");
            Debug.Log(sb.ToString());
        }
    }

    static void LogIntrinsics(string tag, PassthroughCameraAccess pca)
    {
        var i = pca.Intrinsics;
        Debug.Log($"[Probe] {tag} sensor={i.SensorResolution.x}x{i.SensorResolution.y} " +
                  $"current={pca.CurrentResolution.x}x{pca.CurrentResolution.y} " +
                  $"focal=({i.FocalLength.x:F3},{i.FocalLength.y:F3}) " +
                  $"principal=({i.PrincipalPoint.x:F3},{i.PrincipalPoint.y:F3}) " +
                  $"lensOffset.pos={i.LensOffset.position} lensOffset.rot={i.LensOffset.rotation}");

        Rect crop = CameraGeometry.SensorCropRegion(i.SensorResolution, pca.CurrentResolution);
        Debug.Log($"[Probe] {tag} derived crop=({crop.x:F2},{crop.y:F2},{crop.width:F2},{crop.height:F2})");
    }

    /// <summary>
    /// Test A. Compares our derived K against the MRUK oracle. camPose is captured ONCE and passed
    /// explicitly to WorldToViewportPoint — omitting it makes MRUK re-fetch the pose, which mixes a
    /// pose difference into what is meant to be a pure intrinsics check.
    /// </summary>
    void RunTestA(PassthroughCameraAccess pca)
    {
        Pose camPose = pca.GetCameraPose();
        Pose camPoseCv = CameraGeometry.FlipY(camPose);
        var i = pca.Intrinsics;

        foreach (bool halfPixel in new[] { false, true })
        {
            var k = CameraGeometry.DeriveIntrinsics(i.FocalLength, i.PrincipalPoint,
                                                    i.SensorResolution, pca.CurrentResolution, halfPixel);
            float maxErr = 0f, sumErr = 0f;
            int compared = 0;

            for (int n = 0; n < testAPointCount; n++)
            {
                // Random point in front of the camera, in camera-local space, then to world.
                var local = new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), Random.Range(0.5f, 4f));
                Vector3 world = camPose.position + camPose.rotation * local;

                Vector2 oracle = pca.WorldToViewportPoint(world, camPose);
                if (!CameraGeometry.Project(CameraGeometry.FlipY(world), camPoseCv, k, out Vector2 px, out _))
                    continue;
                Vector2 ours = CameraGeometry.PixelToViewport(px, k);

                // Compare in pixels, not viewport units — 0.5 px is the gate.
                float err = new Vector2((oracle.x - ours.x) * k.width,
                                        (oracle.y - ours.y) * k.height).magnitude;
                maxErr = Mathf.Max(maxErr, err);
                sumErr += err;
                compared++;
            }

            string verdict = maxErr < 0.5f ? "PASS" : "FAIL";
            Debug.Log($"[Probe] TestA halfPixel={halfPixel}: {verdict} " +
                      $"max={maxErr:F4}px mean={(compared > 0 ? sumErr / compared : 0f):F4}px n={compared}");
        }
    }

    /// <summary>Test C. Stereo viability — timestamp delta and baseline. Measurement, not a gate.</summary>
    IEnumerator RunTestC()
    {
        const int samples = 120;
        double maxDeltaMs = 0d, sumDeltaMs = 0d;
        float minBase = float.MaxValue, maxBase = 0f;
        int n = 0;

        while (n < samples)
        {
            yield return new WaitForEndOfFrame();
            if (!_left.IsUpdatedThisFrame || !_right.IsUpdatedThisFrame) continue;

            double deltaMs = System.Math.Abs((_left.Timestamp - _right.Timestamp).TotalMilliseconds);
            float baseline = Vector3.Distance(_left.GetCameraPose().position,
                                              _right.GetCameraPose().position);

            maxDeltaMs = System.Math.Max(maxDeltaMs, deltaMs);
            sumDeltaMs += deltaMs;
            minBase = Mathf.Min(minBase, baseline);
            maxBase = Mathf.Max(maxBase, baseline);
            n++;
        }

        Debug.Log($"[Probe] TestC timestamp delta: max={maxDeltaMs:F2}ms mean={sumDeltaMs / n:F2}ms " +
                  $"(frame interval is ~33ms — a delta near that means the cameras are not co-triggered)");
        Debug.Log($"[Probe] TestC baseline: {minBase * 100f:F2}–{maxBase * 100f:F2} cm " +
                  $"(spread {(maxBase - minBase) * 1000f:F2} mm — a rigid rig should barely vary)");
    }
#endif
}
