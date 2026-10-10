using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoGaze.Expert.App.Editor
{
    /// <summary>
    /// The 3D view's shader (View3D/Resources/VertexColorUnlit) is URP, so the project needs a URP
    /// pipeline asset or the reconstruction silently draws nothing. The asset is committed under
    /// Assets/Settings; this re-creates and assigns it whenever none is assigned, so a fresh clone
    /// renders without manual steps. Also: CoGaze > Set Up Rendering, or -executeMethod Ensure.
    /// </summary>
    public static class RenderPipelineSetup
    {
        const string AssetPath = "Assets/Settings/ExpertURP.asset";
        const string RendererPath = "Assets/Settings/ExpertURP_Renderer.asset";

        [InitializeOnLoadMethod]
        static void EnsureOnLoad() => EditorApplication.delayCall += () =>
        {
            if (GraphicsSettings.defaultRenderPipeline == null) Ensure();
        };

        [MenuItem("CoGaze/Set Up Rendering")]
        public static void Ensure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                System.IO.Directory.CreateDirectory("Assets/Settings");
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.supportsHDR = false;            // unlit vertex colour and UI only: nothing to gain
                asset.msaaSampleCount = 4;            // reconstruction edges are thin; smooth them
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log($"[CoGaze] URP pipeline asset assigned: {AssetPath}");
        }
    }
}
