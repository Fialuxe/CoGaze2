using System;
using System.Collections;
using System.Text;
using CoGaze2.EnvLine;
using Meta.XR;
using CoGaze.Geometry;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Validates the passthrough camera model against the headset before it is relied on.
///
/// Logs the sensor/lens parameters of both cameras, then runs two checks:
///
///   Projection check — projects random points with <see cref="CameraGeometry"/> and compares
///   against PassthroughCameraAccess.WorldToViewportPoint, which is treated as ground truth.
///   Passes under 0.5 px. This validates the sensor-crop arithmetic and the coordinate
///   conversion. It cannot detect lens distortion or a pose/image time offset, because both
///   sides share the same pose and the same pinhole model.
///
///   Stereo check — measures the capture-time difference and the distance between the two
///   cameras. Stereo depth requires the cameras to expose at nearly the same instant; a
///   difference approaching the frame interval means they are not co-triggered.
///
/// Runs in the Editor over Meta Horizon Link or the XR Simulator, as well as on device.
/// </summary>
public class GeometryProbe : MonoBehaviour
{
    const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

    [SerializeField] Vector2Int requestedResolution = new Vector2Int(1280, 960);
    [SerializeField] int projectionSamples = 1000;
    [SerializeField] int stereoSamples = 120;

    PassthroughCameraAccess _left, _right;

    /// <summary>Installs itself at startup in diagnostic builds only: add COGAZE_PROBE to the scripting defines.</summary>
#if COGAZE_PROBE
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#endif
    static void Bootstrap()
    {
        var go = new GameObject(nameof(GeometryProbe));
        DontDestroyOnLoad(go);
        go.AddComponent<GeometryProbe>();
    }

    IEnumerator Start()
    {
        // IsSupported accepts SystemHeadset.None in the Editor, so it returns true with nothing
        // connected. Report the headset type directly, which does distinguish the two.
        var headset = OVRPlugin.GetSystemHeadsetType();
        Debug.Log($"[Probe] device={SystemInfo.deviceModel} os={SystemInfo.operatingSystem} headset={headset}");
        if (headset == OVRPlugin.SystemHeadset.None)
        {
            Debug.LogError("[Probe] no headset detected. Connect Quest Link or run on device; " +
                           "camera metadata may still be reported but carries no tracking.");
        }

        if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
        {
            // The permission dialog is asynchronous and offers no completion callback.
            Permission.RequestUserPermission(HeadsetCameraPermission);
            yield return new WaitForSeconds(1f);
        }

        // Single availability gate; nothing below re-checks.
        if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
            !PassthroughCameraAccess.IsSupported)
        {
            Debug.LogError("[Probe] passthrough camera unavailable " +
                           $"(permission={Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)}, " +
                           $"supported={PassthroughCameraAccess.IsSupported}). Disabling.");
            enabled = false;
            yield break;
        }

        LogSupportedResolutions();

        _left = Attach(PassthroughCameraAccess.CameraPositionType.Left);
        _right = Attach(PassthroughCameraAccess.CameraPositionType.Right);
        // Bounded, so a camera that never starts reports the failure instead of hanging silently.
        float deadline = Time.time + 10f;
        yield return new WaitUntil(() => (_left.IsPlaying && _right.IsPlaying) || Time.time > deadline);
        if (!_left.IsPlaying || !_right.IsPlaying)
        {
            Debug.LogError($"[Probe] cameras did not start within 10s " +
                           $"(left={_left.IsPlaying}, right={_right.IsPlaying}).");
            yield break;
        }

        LogCameraParameters("L", _left);
        LogCameraParameters("R", _right);

        RunProjectionCheck(_left);
        yield return RunStereoCheck();
    }

    PassthroughCameraAccess Attach(PassthroughCameraAccess.CameraPositionType position)
    {
        // Only one component may own a given camera, so adopt an existing one rather than
        // competing with it.
        foreach (var existing in FindObjectsByType<PassthroughCameraAccess>(FindObjectsSortMode.None))
        {
            if (existing.CameraPosition != position) continue;
            Debug.Log($"[Probe] reusing existing {position} camera on '{existing.gameObject.name}'");
            return existing;
        }

        // The component claims its camera slot in OnEnable, which AddComponent triggers
        // immediately. Configure it on an inactive object first, otherwise both cameras start
        // as the default position and the second one is rejected as a duplicate.
        var go = new GameObject($"PassthroughCamera_{position}");
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        var pca = go.AddComponent<PassthroughCameraAccess>();
        pca.CameraPosition = position;
        pca.RequestedResolution = requestedResolution;

        go.SetActive(true);
        Debug.Log($"[Probe] attached {position}, component reports {pca.CameraPosition}");
        return pca;
    }

    void OnDestroy()
    {
        // The camera slots are held by a static table, so release them explicitly rather than
        // relying on teardown order.
        foreach (Transform child in transform)
            if (child != null) Destroy(child.gameObject);
    }

    static void LogSupportedResolutions()
    {
        foreach (var position in new[] { PassthroughCameraAccess.CameraPositionType.Left,
                                         PassthroughCameraAccess.CameraPositionType.Right })
        {
            var sb = new StringBuilder($"[Probe] supported {position}: ");
            foreach (var r in PassthroughCameraAccess.GetSupportedResolutions(position))
                sb.Append($"{r.x}x{r.y} ");
            Debug.Log(sb.ToString());
        }
    }

    static void LogCameraParameters(string tag, PassthroughCameraAccess pca)
    {
        var p = pca.Intrinsics;
        Debug.Log($"[Probe] {tag} sensor={p.SensorResolution.x}x{p.SensorResolution.y} " +
                  $"delivered={pca.CurrentResolution.x}x{pca.CurrentResolution.y} " +
                  $"focal=({p.FocalLength.x:F3},{p.FocalLength.y:F3}) " +
                  $"principal=({p.PrincipalPoint.x:F3},{p.PrincipalPoint.y:F3}) " +
                  $"lensOffset.pos={p.LensOffset.position} lensOffset.rot={p.LensOffset.rotation}");

        Rect crop = CameraGeometry.SensorCropRegion(p.SensorResolution, pca.CurrentResolution);
        Debug.Log($"[Probe] {tag} crop=({crop.x:F2},{crop.y:F2},{crop.width:F2},{crop.height:F2})");
    }

    /// <summary>Compares our projection against the SDK's. Passes under 0.5 px.</summary>
    void RunProjectionCheck(PassthroughCameraAccess pca)
    {
        // Captured once and passed explicitly below: letting the SDK re-fetch the pose per call
        // would fold a pose difference into what is meant to measure intrinsics alone.
        Pose camPose = pca.GetCameraPose();
        Pose camPoseCv = CameraGeometry.FlipY(camPose);
        var p = pca.Intrinsics;

        // Both sides of this comparison use camPose, so an untracked headset yields a pass that
        // means nothing. An exactly-default pose indicates no tracking data rather than a
        // headset that happens to sit at the origin.
        if (camPose.position == Vector3.zero && camPose.rotation == Quaternion.identity)
        {
            Debug.LogError("[Probe] camera pose is exactly default — no headset tracking. " +
                           "Connect Quest Link or run on device; results below are meaningless.");
        }

        var k = CameraGeometry.DeriveIntrinsics(p.FocalLength, p.PrincipalPoint,
                                                p.SensorResolution, pca.CurrentResolution);
        float maxErr = 0f, sumErr = 0f;
        int compared = 0;

        for (int n = 0; n < projectionSamples; n++)
        {
            var local = new Vector3(UnityEngine.Random.Range(-2f, 2f),
                                    UnityEngine.Random.Range(-2f, 2f),
                                    UnityEngine.Random.Range(0.5f, 4f));
            Vector3 world = camPose.position + camPose.rotation * local;

            Vector2 expected = pca.WorldToViewportPoint(world, camPose);
            if (!CameraGeometry.Project(CameraGeometry.FlipY(world), camPoseCv, k, out Vector2 px, out _))
                continue;
            Vector2 actual = CameraGeometry.PixelToViewport(px, k);

            float err = new Vector2((expected.x - actual.x) * k.width,
                                    (expected.y - actual.y) * k.height).magnitude;
            maxErr = Mathf.Max(maxErr, err);
            sumErr += err;
            compared++;
        }

        Debug.Log($"[Probe] projection: {(maxErr < 0.5f ? "PASS" : "FAIL")} " +
                  $"max={maxErr:F4}px mean={(compared > 0 ? sumErr / compared : 0f):F4}px n={compared}");
    }

    /// <summary>Measures whether the two cameras are co-triggered and how far apart they sit.</summary>
    IEnumerator RunStereoCheck()
    {
        double maxDeltaMs = 0d, sumDeltaMs = 0d;
        float minBaseline = float.MaxValue, maxBaseline = 0f;
        int n = 0;

        while (n < stereoSamples)
        {
            yield return new WaitForEndOfFrame();
            if (!_left.IsUpdatedThisFrame || !_right.IsUpdatedThisFrame) continue;

            double deltaMs = Math.Abs((_left.Timestamp - _right.Timestamp).TotalMilliseconds);
            float baseline = Vector3.Distance(_left.GetCameraPose().position,
                                              _right.GetCameraPose().position);

            maxDeltaMs = Math.Max(maxDeltaMs, deltaMs);
            sumDeltaMs += deltaMs;
            minBaseline = Mathf.Min(minBaseline, baseline);
            maxBaseline = Mathf.Max(maxBaseline, baseline);
            n++;
        }

        Debug.Log($"[Probe] stereo capture-time delta: max={maxDeltaMs:F2}ms mean={sumDeltaMs / n:F2}ms " +
                  $"(compare against the ~33ms frame interval)");
        Debug.Log($"[Probe] stereo baseline: {minBaseline * 100f:F2}-{maxBaseline * 100f:F2} cm " +
                  $"(varies by {(maxBaseline - minBaseline) * 1000f:F2} mm)");
    }
}
