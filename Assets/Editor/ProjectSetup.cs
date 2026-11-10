using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// 프로젝트 설정과 빌드를 코드로 한다.
/// 명령줄: Unity -batchmode -projectPath . -executeMethod ProjectSetup.Build -quit
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("LumiaCircuit/Setup")]
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory("Assets/Resources/Materials");
        Directory.CreateDirectory("Assets/Scenes");

        // URP
        var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rd, "Assets/Settings/Renderer.asset");
        rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
        rd.renderingMode = RenderingMode.Forward;   // 툰 셰이더의 보조 조명(가로등) 반복문이 Forward 방식을 쓴다
        EditorUtility.SetDirty(rd);
        var urp = UniversalRenderPipelineAsset.Create(rd);
        urp.supportsHDR = true;
        urp.msaaSampleCount = 4;
        urp.shadowDistance = 60;
        urp.maxAdditionalLightsCount = 8;
        AssetDatabase.CreateAsset(urp, "Assets/Settings/URP.asset");
        GraphicsSettings.defaultRenderPipeline = urp;
        int q = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(q, false);

        // 실행 중 복제해 쓰는 기본 재질 (빌드에 셰이더가 포함되도록 에셋으로 둔다)
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.EnableKeyword("_EMISSION");
        lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        lit.SetColor("_EmissionColor", Color.black);
        AssetDatabase.CreateAsset(lit, "Assets/Resources/Materials/LitBase.mat");
        var toonShader = Shader.Find("Lumia/Toon");
        if (toonShader != null)
        {
            var toon = new Material(toonShader);
            AssetDatabase.CreateAsset(toon, "Assets/Resources/Materials/ToonBase.mat");
        }
        else Debug.LogError("[ProjectSetup] Lumia/Toon 셰이더를 찾지 못했습니다");

        // 빈 장면 (게임이 실행 중에 모든 것을 만든다)
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = "LumiaCircuit";
        PlayerSettings.productName = "Lumia Circuit";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] Setup done");
    }

    /// 웹(WebGL) 빌드 → docs/ (GitHub Pages가 그대로 서비스한다)
    [MenuItem("LumiaCircuit/Build WebGL")]
    public static void BuildWeb()
    {
        Setup();
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;   // GitHub Pages는 Content-Encoding 헤더를 못 붙인다
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "docs", BuildTarget.WebGL, BuildOptions.None);
        FitCanvasToWindow("docs/index.html");
        Debug.Log("[ProjectSetup] Build result: " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" + report.summary.totalSize);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    /// 기본 템플릿은 캔버스가 1280x720 고정이라 작은 창에서 잘린다 → 창에 맞춰 16:9로 늘인다.
    static void FitCanvasToWindow(string path)
    {
        if (!File.Exists(path)) return;
        string html = File.ReadAllText(path);
        html = html.Replace("canvas.style.width = \"1280px\";", "function fit() { var s = Math.min(window.innerWidth / 16, (window.innerHeight - 44) / 9); canvas.style.width = Math.floor(s * 16) + \"px\"; canvas.style.height = Math.floor(s * 9) + \"px\"; } fit(); window.addEventListener(\"resize\", fit); document.body.style.background = \"#10222c\";");
        html = html.Replace("canvas.style.height = \"720px\";", "");
        File.WriteAllText(path, html);
    }

    [MenuItem("LumiaCircuit/Build")]
    public static void Build()
    {
        Setup();
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Build/LumiaCircuit.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[ProjectSetup] Build result: " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" + report.summary.totalSize);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
