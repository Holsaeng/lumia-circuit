using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// 네온 서킷 — 정비 로봇 루미가 회로 미로에서 에너지 셀을 모으고 보안 드론을 피한다.
/// 장면은 비어 있어도 된다. 모든 것을 코드로 만든다.
public class Game : MonoBehaviour
{
    enum State { Title, Ready, Play, Dying, Clear, Over }

    class Mover
    {
        public Vector2Int cell, dir;
        public float t;
        public Vector3 Pos => Vector3.Lerp(Maze.World(cell), Maze.World(cell + dir), t);
    }

    class Drone
    {
        public Mover m = new Mover();
        public Transform tr, ring;
        public Material glow;
        public Color color;
        public Rig rig;
        public int kind, patrol;
        public float dock;
        public bool vulnerable;
        public Vector2Int home;
    }

    // ---------- 절차적 애니메이션: 모델 부품마다 회전 축(피벗)을 만들어 코드로 움직인다 ----------
    class Part
    {
        public Transform t;
        public Quaternion baseRel;
        public Vector3 right, up, fwd;   // 부모 기준으로 본 캐릭터의 오른쪽·위·앞
        public float phase, side;
    }

    class Rig
    {
        public string kind;
        public Transform holder, model;
        public Quaternion modelBase; public Vector3 modelBasePos, modelBaseScale;
        public readonly List<Part> legs = new List<Part>(), wings = new List<Part>(), tails = new List<Part>(),
            antennas = new List<Part>(), eyes = new List<Part>();
        public float gait, move, roll, squash, blinkT = 2f, lastYaw;
        public Vector3 lastPos; public bool init;
    }

    const float ModelYaw = 180f;      // Blender -Y 정면 → Unity 진행 방향
    const float WallH = 0.6f;
    // 야생동물: 늑대(추적) · 박쥐(매복) · 멧돼지(순찰) · 닭(방랑)
    static readonly string[] AnimalModels = { "Wolf", "Bat", "Boar", "Chicken" };
    static readonly Color[] DroneColors =
    {
        new Color(1f, 0.12f, 0.25f), new Color(0.8f, 0.25f, 1f), new Color(1f, 0.5f, 0.05f), new Color(1f, 0.9f, 0.2f),
    };
    static readonly Color Scared = new Color(0.25f, 0.45f, 1f);
        static readonly string[] ZoneNames = { "북서", "북동", "남서", "남동" };
    static readonly string[] Loot = { "운석", "생명의 나무", "미스릴", "포스 코어" };
    const float FuseMax = 20f;

    // 금지구역: 한 번에 한 구역. 예고(노랑) → 금지(빨강) → 해제를 되풀이한다.
    GameObject[] zoneQuads; Material[] zoneMats;
    int zone = -1; float zoneT, fuse = FuseMax; bool zoneLive;
    Light sunLight; Text fuseText; Image panel;
    Transform foam;
    readonly List<Light> lamps = new List<Light>();

    Material litBase, wallGlow;
    readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    readonly Dictionary<Vector2Int, GameObject> pickups = new Dictionary<Vector2Int, GameObject>();
    readonly List<Transform> spinners = new List<Transform>();
    Transform pickupRoot;

    Mover player = new Mover();
    Transform playerT, playerRing;
    Rig playerRig;
    Light playerLight;
    Vector2Int want, facing = Vector2Int.down;
    Drone[] drones;
    int[,] playerDist;
    Vector2Int playerDistCell = new Vector2Int(-1, -1);

    State state;
    float stateT, emp, bonusT, time;
    int score, hi, lives, level, combo, cellsLeft, cellsTotal, blip;
    bool paused;
    GameObject bonus;
    Vector2Int bonusCell;
    readonly List<int> bonusAt = new List<int>();

    Camera cam;
    Vector3 camBase, camLook;
    AudioSource audioSrc;
    Mesh cubeMesh;
    Text scoreText, hiText, livesText, centerText, popText;
    float popT;

    string shotPath; bool autopilot; int shotStage, startLevel = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
    }

    // ------------------------------------------------------------------ 준비
    void Awake()
    {
        Application.targetFrameRate = 120;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shot") { shotPath = args[i + 1]; autopilot = true; }
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-startlevel" && int.TryParse(args[i + 1], out var sl)) startLevel = Mathf.Max(1, sl);
        if (autopilot) PlayerPrefs.DeleteKey("hi");

        Maze.Load();
        Sfx.Build();
        hi = PlayerPrefs.GetInt("hi", 0);
        cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        audioSrc = gameObject.AddComponent<AudioSource>();

        BuildMaterials();
        BuildWorld();
        BuildActors();
        BuildHud();
        level = 1; lives = 3;
        StartLevel();
        SetState(State.Title);
    }

    Material Mat(string name, Color baseC, Color emis, float metal, float smooth)
    {
        var m = new Material(litBase) { name = name ?? "Glow" };
        m.SetColor("_BaseColor", baseC);
        m.SetColor("_EmissionColor", emis);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        Color oc = baseC * 0.28f; oc.a = 1;                 // 외곽선 = 바탕색을 어둡게
        m.SetColor("_OutlineColor", oc);
        if (name != null) mats[name] = m;
        return m;
    }

    /// 지형·바닥·작은 아이템처럼 외곽선과 테두리 빛이 필요 없는 재질
    static Material Flat(Material m, float vertexAO = 0)
    {
        m.SetShaderPassEnabled("SRPDefaultUnlit", false);
        m.SetFloat("_Rim", 0);
        m.SetFloat("_VertexAO", vertexAO);
        return m;
    }
    // 눈에 잘 띄어야 하는 것(아이템·상태 표시)만 아주 약하게 스스로 빛난다. 번쩍이는 발광은 쓰지 않는다.
    Material GlowMat(string name, Color c, float power) => Mat(name, c, c * power, 0, 0.1f);
    Material Matte(string name, float r, float g, float b) => Mat(name, new Color(r, g, b), Color.black, 0, 0.08f);

    void BuildMaterials()
    {
        litBase = Resources.Load<Material>("Materials/ToonBase");
        if (litBase == null) litBase = Resources.Load<Material>("Materials/LitBase");
        if (litBase == null) litBase = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        litBase.EnableKeyword("_EMISSION");
        // 루미 (보급 로봇)
        Matte("Visor", 0.05f, 0.06f, 0.1f); GlowMat("Glow", new Color(0.2f, 0.8f, 0.95f), 0.4f);
        // 야생동물
        Matte("Body", 0.9f, 0.9f, 0.9f); Matte("Accent", 0.95f, 0.55f, 0.12f); Matte("Metal", 0.2f, 0.22f, 0.26f);
        GlowMat("Aura", DroneColors[0], 0.35f);
        Matte("FurWhite", 0.93f, 0.9f, 0.85f); Matte("FurBrown", 0.42f, 0.27f, 0.17f); Matte("FurGrey", 0.48f, 0.5f, 0.55f);
        Matte("FurDark", 0.16f, 0.14f, 0.2f); Matte("FurPurple", 0.36f, 0.24f, 0.48f);
        Matte("Comb", 0.85f, 0.12f, 0.14f); Matte("Snout", 0.78f, 0.55f, 0.5f); Matte("Bone", 0.95f, 0.92f, 0.82f);
        // 아이템
        Flat(Mat("CellGlow", new Color(1f, 0.78f, 0.2f), new Color(1f, 0.78f, 0.2f) * 0.25f, 0.3f, 0.35f));
        GlowMat("CoreGlow", new Color(0.9f, 0.1f, 0.18f), 0.5f);
        GlowMat("BonusGlow", new Color(0.3f, 0.9f, 0.45f), 0.4f);
        Matte("Crate", 0.3f, 0.36f, 0.44f);
        // 소품·지형
        Matte("Leaf", 0.2f, 0.45f, 0.18f); Matte("LeafLight", 0.35f, 0.58f, 0.22f); Matte("Trunk", 0.35f, 0.24f, 0.15f);
        Matte("Stone", 0.52f, 0.52f, 0.54f); Matte("Wood", 0.62f, 0.45f, 0.27f); Matte("WoodDark", 0.4f, 0.27f, 0.16f);
        Flat(Matte("Floor", 0.5f, 0.4f, 0.27f), 1); Flat(Matte("Wall", 0.2f, 0.42f, 0.18f), 1);
        wallGlow = Flat(Matte("WallCap", 0.28f, 0.52f, 0.22f), 1);
        Flat(Matte("Grass", 0.36f, 0.56f, 0.26f)); Flat(Matte("Sand", 0.85f, 0.77f, 0.56f));
        var sea = Flat(Mat("Sea", new Color(0.12f, 0.42f, 0.58f), Color.black, 0, 0.3f));
        sea.SetFloat("_WaveAmp", 0.05f); sea.SetFloat("_Spec", 0.5f);
        Flat(Matte("Foam", 0.92f, 0.95f, 0.96f));
        // 가로등
        Matte("LampPole", 0.2f, 0.21f, 0.24f);
        Mat("LampGlow", new Color(1f, 0.86f, 0.6f), Color.black, 0, 0.1f);
    }

    // ------------------------------------------------------------------ 지형 메시
    /// 미로 바닥: 셀당 4×4 격자. 담에 가까운 정점일수록 어둡게(구석 그늘을 미리 구워 둔다).
    static Mesh BuildFloorMesh()
    {
        const int Sub = 4;
        int nx = Maze.W * Sub, ny = Maze.H * Sub;
        var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
        Vector3 origin = Maze.World(new Vector2Int(0, 0)) + new Vector3(-0.5f, 0, 0.5f);   // 왼쪽 위 모서리
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float gx = i / (float)Sub, gy = j / (float)Sub;          // 격자 좌표 (셀 단위)
                v.Add(origin + new Vector3(gx, 0, -gy));
                float d = 9;
                for (int cy = Mathf.FloorToInt(gy) - 1; cy <= Mathf.FloorToInt(gy) + 1; cy++)
                    for (int cx = Mathf.FloorToInt(gx) - 1; cx <= Mathf.FloorToInt(gx) + 1; cx++)
                    {
                        if (!Maze.Wall(cx, cy)) continue;
                        float dx = Mathf.Max(cx - gx, 0, gx - (cx + 1)), dy = Mathf.Max(cy - gy, 0, gy - (cy + 1));
                        d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dy * dy));
                    }
                float ao = Mathf.Lerp(0.58f, 1f, Mathf.SmoothStep(0, 1, d / 0.42f));
                col.Add(new Color(ao, ao, ao, 1));
            }
        int row = nx + 1;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * row + i, b = a + 1, c = a + row, e = c + 1;
                tri.Add(a); tri.Add(b); tri.Add(e); tri.Add(a); tri.Add(e); tri.Add(c);
            }
        var m = new Mesh { name = "Floor", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    /// 바다: 물결이 보이도록 잘게 나눈 넓은 판
    static Mesh BuildGrid(string name, float half, int n, float y)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                v.Add(new Vector3(-half + 2 * half * i / n, y, -half + 2 * half * j / n));
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, e = c + 1;
                tri.Add(a); tri.Add(c); tri.Add(e); tri.Add(a); tri.Add(e); tri.Add(b);
            }
        var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        m.bounds = new Bounds(new Vector3(0, y, 0), new Vector3(2 * half, 2, 2 * half));
        return m;
    }

    /// 사각 테두리 (해안 거품)
    static Mesh BuildFrame(string name, float hw, float hh, float width, float y)
    {
        float w0 = hw, h0 = hh, w1 = hw + width, h1 = hh + width;
        var v = new List<Vector3>
        {
            new Vector3(-w1, y, -h1), new Vector3(w1, y, -h1), new Vector3(w1, y, h1), new Vector3(-w1, y, h1),
            new Vector3(-w0, y, -h0), new Vector3(w0, y, -h0), new Vector3(w0, y, h0), new Vector3(-w0, y, h0),
        };
        var tri = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            int o0 = k, o1 = (k + 1) % 4, i0 = k + 4, i1 = (k + 1) % 4 + 4;
            tri.Add(o0); tri.Add(i0); tri.Add(i1); tri.Add(o0); tri.Add(i1); tri.Add(o1);
        }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    GameObject MeshObject(string name, Mesh mesh, Material m, bool castShadows = false)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        if (!castShadows) r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    // 지역 테마: 길 · 담 · 담 윗단 색과 담 위에 올리는 소품
    struct Theme { public string name, prop; public Color path, wall, cap; }
    static Color C(float r, float g, float b) => new Color(r, g, b);
    static readonly Theme[] Themes =
    {
        new Theme { name = "숲",     prop = "Tree",    path = C(0.55f, 0.44f, 0.3f),  wall = C(0.2f, 0.42f, 0.18f),  cap = C(0.3f, 0.54f, 0.22f) },
        new Theme { name = "병원",   prop = "Bush",    path = C(0.66f, 0.66f, 0.64f), wall = C(0.9f, 0.89f, 0.86f),  cap = C(0.78f, 0.26f, 0.26f) },
        new Theme { name = "항구",   prop = "WoodBox", path = C(0.52f, 0.5f, 0.47f),  wall = C(0.24f, 0.42f, 0.6f),  cap = C(0.88f, 0.62f, 0.2f) },
        new Theme { name = "연구소", prop = "WoodBox", path = C(0.6f, 0.62f, 0.66f),  wall = C(0.84f, 0.86f, 0.88f), cap = C(0.25f, 0.45f, 0.78f) },
        new Theme { name = "절",     prop = "Tree",    path = C(0.64f, 0.56f, 0.42f), wall = C(0.58f, 0.26f, 0.2f),  cap = C(0.24f, 0.3f, 0.28f) },
        new Theme { name = "공장",   prop = "WoodBox", path = C(0.44f, 0.42f, 0.4f),  wall = C(0.55f, 0.38f, 0.3f),  cap = C(0.32f, 0.32f, 0.34f) },
        new Theme { name = "학교",   prop = "Tree",    path = C(0.62f, 0.58f, 0.52f), wall = C(0.84f, 0.72f, 0.5f),  cap = C(0.3f, 0.5f, 0.36f) },
        new Theme { name = "성당",   prop = "Rock",    path = C(0.58f, 0.55f, 0.52f), wall = C(0.62f, 0.6f, 0.64f),  cap = C(0.46f, 0.32f, 0.52f) },
        new Theme { name = "호텔",   prop = "Bush",    path = C(0.66f, 0.55f, 0.46f), wall = C(0.78f, 0.64f, 0.46f), cap = C(0.56f, 0.2f, 0.2f) },
        new Theme { name = "묘지",   prop = "Rock",    path = C(0.42f, 0.44f, 0.4f),  wall = C(0.32f, 0.36f, 0.34f), cap = C(0.48f, 0.5f, 0.48f) },
    };
    Theme Now => Themes[(level - 1) % Themes.Length];
    Transform propRoot;

    GameObject Quad(string name, float hw, float hh, float y, Material m)
    {
        var go = new GameObject(name);
        var me = new Mesh { name = name };
        me.vertices = new[] { new Vector3(-hw, y, -hh), new Vector3(-hw, y, hh), new Vector3(hw, y, hh), new Vector3(hw, y, -hh) };
        me.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        me.RecalculateNormals();
        go.AddComponent<MeshFilter>().sharedMesh = me;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    /// 담 위와 섬 가장자리에 소품을 놓는다 (단계가 바뀔 때마다 새로).
    void BuildProps()
    {
        if (propRoot) Destroy(propRoot.gameObject);
        propRoot = new GameObject("Props").transform;
        var rng = new System.Random(level * 7919 + 13);
        string prop = Now.prop;
        void Put(string model, Vector3 pos, float scale)
        {
            var t = Spawn(model, propRoot).transform;
            t.position = pos; t.rotation = Quaternion.Euler(0, rng.Next(360), 0);
            t.localScale = Vector3.one * scale * (0.85f + (float)rng.NextDouble() * 0.3f);
        }
        for (int y = 0; y < Maze.H; y++)
            for (int x = 0; x < Maze.W; x++)
                if (Maze.Wall(x, y) && rng.Next(100) < 30)
                    Put(prop, Maze.World(new Vector2Int(x, y)) + Vector3.up * WallH, prop == "Tree" ? 1.0f : 1.0f);
        // 섬 가장자리 풀밭
        string[] outer = { "Tree", "Tree", "Bush", "Rock" };
        float hw = Maze.W * 0.5f, hh = Maze.H * 0.5f;
        for (int i = 0; i < 90; i++)
        {
            float px = ((float)rng.NextDouble() * 2 - 1) * (hw + 2.6f), pz = ((float)rng.NextDouble() * 2 - 1) * (hh + 2.6f);
            if (Mathf.Abs(px) < hw + 0.5f && Mathf.Abs(pz) < hh + 0.5f) continue;
            Put(outer[rng.Next(outer.Length)], new Vector3(px, 0, pz), 1.7f);
        }
    }

    Material FindMat(string n)
    {
        Material best = null; int len = -1;
        foreach (var kv in mats) if (n.StartsWith(kv.Key) && kv.Key.Length > len) { best = kv.Value; len = kv.Key.Length; }
        return best ?? mats["Body"];
    }

    GameObject Spawn(string model, Transform parent, Material droneGlow = null)
    {
        var prefab = Resources.Load<GameObject>("Models/" + model);
        GameObject go;
        if (prefab != null) go = Instantiate(prefab, parent);
        else { go = new GameObject(model); go.transform.SetParent(parent, false); var f = go.AddComponent<MeshFilter>(); f.sharedMesh = cubeMesh; go.AddComponent<MeshRenderer>(); go.transform.localScale = Vector3.one * 0.3f; }
        go.name = model;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                string n = ms[i] != null ? ms[i].name : model == "Cell" ? "CellGlow" : "Body";
                ms[i] = droneGlow != null && n.StartsWith("Aura") ? droneGlow : FindMat(n);
            }
            r.sharedMaterials = ms;
        }
        return go;
    }

    static Transform Child(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>()) if (c.name == name) return c;
        return null;
    }

    void BuildWorld()
    {
        // 카메라
        var cgo = new GameObject("Camera"); cgo.tag = "MainCamera";
        cam = cgo.AddComponent<Camera>();
        cgo.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.42f, 0.58f);
        cam.fieldOfView = 36; cam.nearClipPlane = 1; cam.farClipPlane = 120; cam.allowHDR = true;
        camBase = new Vector3(0, 34f, -11f); camLook = new Vector3(0, 0, 0.5f);
        cgo.transform.position = camBase; cgo.transform.LookAt(camLook);
        var cd = cam.GetUniversalAdditionalCameraData();
        cd.renderPostProcessing = true;
        cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // 후처리
        var vgo = new GameObject("PostFX");
        var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var tone = prof.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.Neutral;
        var vig = prof.Add<Vignette>(true); vig.intensity.value = 0.18f; vig.smoothness.value = 0.5f;
        var ca = prof.Add<ColorAdjustments>(true); ca.contrast.value = 6; ca.saturation.value = 10;
        vol.sharedProfile = prof;

        // 조명
        var lgo = new GameObject("Sun");
        var sun = lgo.AddComponent<Light>(); sun.type = LightType.Directional; sunLight = sun;
        sun.color = new Color(1f, 0.96f, 0.88f); sun.intensity = 1.5f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f;
        lgo.transform.rotation = Quaternion.Euler(52, 30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.6f);

        // 지형: 바다 위의 섬 — 모래사장 → 풀밭 → 길
        float mw = Maze.W * 0.5f, mh = Maze.H * 0.5f;
        MeshObject("Sea", BuildGrid("Sea", 60, 120, -0.25f), mats["Sea"]);
        foam = MeshObject("Foam", BuildFrame("Foam", mw + 4.2f, mh + 4.2f, 0.32f, 0), mats["Foam"]).transform;
        foam.position = new Vector3(0, -0.22f, 0);
        Quad("Sand", mw + 4.2f, mh + 4.2f, -0.08f, mats["Sand"]);
        Quad("Grass", mw + 3.2f, mh + 3.2f, -0.03f, mats["Grass"]);
        MeshObject("Floor", BuildFloorMesh(), mats["Floor"]);

        // 가로등: 섬 둘레 풀밭 (밤에만 켠다)
        lamps.Clear();
        float lx = mw + 1.2f, lz = mh + 1.2f;
        var lampPos = new[]
        {
            new Vector3(-lx, 0, -lz), new Vector3(0, 0, -lz), new Vector3(lx, 0, -lz),
            new Vector3(-lx, 0, 0), new Vector3(lx, 0, 0),
            new Vector3(-lx, 0, lz), new Vector3(0, 0, lz), new Vector3(lx, 0, lz),
        };
        foreach (var p in lampPos)
        {
            var t = Spawn("Lamp", null).transform; t.position = p;
            // 모델의 앞(Blender -Y)은 Unity의 -forward 쪽 → forward를 바깥으로 두면 등불이 섬 안쪽을 본다
            t.rotation = Quaternion.LookRotation(new Vector3(p.x, 0, p.z).normalized + Vector3.forward * 0.001f);
            var lgo2 = new GameObject("LampLight"); lgo2.transform.position = p + Vector3.up * 1.5f;
            var l = lgo2.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.5f);
            l.intensity = 4f; l.range = 7f; l.shadows = LightShadows.None;
            lamps.Add(l);
        }

        // 담
        var walls = new GameObject("Walls");
        walls.AddComponent<MeshFilter>().sharedMesh = Maze.BuildWallMesh(WallH, 0.12f);
        walls.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats["Wall"], wallGlow };

        pickupRoot = new GameObject("Pickups").transform;

        // 금지구역 바닥 (사분면 4개, 가운데 열·행은 언제나 안전)
        zoneQuads = new GameObject[4]; zoneMats = new Material[4];
        int midX = Maze.W / 2, midY = Maze.H / 2;
        for (int i = 0; i < 4; i++)
        {
            int cx0 = (i & 1) == 0 ? 1 : midX + 1, cx1 = (i & 1) == 0 ? midX - 1 : Maze.W - 2;
            int cy0 = (i & 2) == 0 ? 1 : midY + 1, cy1 = (i & 2) == 0 ? midY - 1 : Maze.H - 2;
            Vector3 a = Maze.World(new Vector2Int(cx0, cy1)), b = Maze.World(new Vector2Int(cx1, cy0));
            float x0 = a.x - 0.5f, x1 = b.x + 0.5f, z0 = a.z - 0.5f, z1 = b.z + 0.5f, y = 0.012f;
            var zm = new Mesh { name = "Zone" + i };
            zm.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            zm.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            zm.RecalculateNormals();
            var zq = new GameObject("Zone" + i);
            zq.AddComponent<MeshFilter>().sharedMesh = zm;
            zoneMats[i] = Flat(Mat(null, new Color(0.8f, 0.14f, 0.14f), Color.black, 0, 0.08f));
            var zr = zq.AddComponent<MeshRenderer>(); zr.sharedMaterial = zoneMats[i];
            zr.shadowCastingMode = ShadowCastingMode.Off;
            zq.SetActive(false);
            zoneQuads[i] = zq;
        }
    }

    static int ZoneOf(Vector2Int c)
    {
        int midX = Maze.W / 2, midY = Maze.H / 2;
        if (c.x == midX || c.y == midY) return -1;
        return (c.x > midX ? 1 : 0) + (c.y > midY ? 2 : 0);
    }

    void ClearZone()
    {
        if (zoneQuads != null) foreach (var q in zoneQuads) q.SetActive(false);
        zone = -1; zoneLive = false; zoneT = 0;
    }

    void UpdateZone(float dt)
    {
        zoneT += dt;
        if (zone < 0)
        {
            if (zoneT < 14f) return;
            zone = UnityEngine.Random.Range(0, 4); zoneT = 0; zoneLive = false;
            zoneQuads[zone].SetActive(true);
            Announce("<color=#FFD84A>금지구역 예고 — " + ZoneNames[zone] + " 구역</color>");
            audioSrc.PlayOneShot(Sfx.Bonus, 0.7f);
        }
        else if (!zoneLive)
        {
            bool on = Mathf.Repeat(zoneT * 3, 1) > 0.45f;
            zoneMats[zone].SetColor("_BaseColor", new Color(0.9f, 0.75f, 0.15f) * (on ? 1f : 0.6f));
            zoneMats[zone].SetColor("_EmissionColor", Color.black);
            if (zoneT > 7f)
            {
                zoneLive = true; zoneT = 0;
                zoneMats[zone].SetColor("_BaseColor", new Color(0.8f, 0.14f, 0.14f));
                Announce("<color=#FF5566>금지구역 지정 — " + ZoneNames[zone] + " 구역</color>");
                audioSrc.PlayOneShot(Sfx.Zap, 0.6f);
            }
        }
        else
        {
            zoneMats[zone].SetColor("_EmissionColor", new Color(1f, 0.08f, 0.12f) * (0.15f + 0.1f * Mathf.Sin(time * 6)));
            if (zoneT > 13f) { zoneQuads[zone].SetActive(false); zone = -1; zoneT = 0; zoneLive = false; }
        }

        bool inside = zoneLive && ZoneOf(player.cell) == zone;
        if (inside)
        {
            fuse -= dt;
            if (fuse <= 0) { fuse = 0; Announce("<color=#FF5566>폭사!</color>"); SetState(State.Dying); }
        }
        fuseText.text = "폭발 타이머  " + fuse.ToString("0.0");
        fuseText.color = inside ? (Mathf.Repeat(time * 4, 1) > 0.5f ? Color.white : new Color(1f, 0.3f, 0.35f)) : new Color(0.75f, 0.8f, 0.9f, 0.8f);
    }

    void Announce(string s) { popText.text = s; popT = 2.2f; var rt = popText.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.72f); rt.anchoredPosition = Vector2.zero; }

    Light AddLight(Transform parent, Color c, float intensity, float range)
    {
        var go = new GameObject("Light"); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, 0.9f, 0);
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }

    void BuildActors()
    {
        var holder = new GameObject("Player").transform;
        var model = Spawn("Lumi", holder).transform;
        model.localRotation = Quaternion.Euler(0, ModelYaw, 0); model.localScale = Vector3.one * 1.15f;
        playerT = holder; playerRing = Child(model, "Ring");
        playerLight = AddLight(holder, new Color(1f, 0.92f, 0.8f), 2.2f, 3.5f);   // 밤에만 켜는 작은 손전등
        playerLight.transform.localPosition = new Vector3(0, 1.4f, 0);
        playerRig = BuildRig(model, "Lumi");

        drones = new Drone[Mathf.Min(4, Maze.Docks.Count)];
        for (int i = 0; i < drones.Length; i++)
        {
            var d = new Drone { kind = i, color = DroneColors[i], home = Maze.Docks[i] };
            d.glow = GlowMat(null, d.color, 0.35f);
            var h = new GameObject("Drone" + i).transform;
            var dm = Spawn(AnimalModels[i % AnimalModels.Length], h, d.glow).transform;
            dm.localRotation = Quaternion.Euler(0, ModelYaw, 0); dm.localScale = Vector3.one * 1.15f;
            d.tr = h; d.ring = Child(dm, "Ring");
            d.rig = BuildRig(dm, AnimalModels[i % AnimalModels.Length]);
            drones[i] = d;
        }
    }

    // ------------------------------------------------------------------ HUD
    Text MakeText(Transform parent, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Color c, Font font)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontStyle = FontStyle.Bold; t.fontSize = size; t.alignment = align; t.color = c; t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
        var o = go.AddComponent<Outline>(); o.effectColor = new Color(0, 0.05f, 0.15f, 0.9f); o.effectDistance = new Vector2(2, -2);
        var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(900, 80);
        return t;
    }

    void BuildHud()
    {
        var cgo = new GameObject("HUD");
        var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cgo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1;
        Font font = Resources.Load<Font>("Fonts/NotoSansKR");   // 웹 빌드에서는 OS 글꼴을 못 쓰므로 글꼴을 함께 넣는다
        if (font == null) try { font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Segoe UI", "Arial" }, 40); } catch { }
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var pgo = new GameObject("Panel", typeof(RectTransform)); pgo.transform.SetParent(cgo.transform, false);
        panel = pgo.AddComponent<Image>(); panel.color = new Color(0, 0.01f, 0.04f, 0.9f); panel.raycastTarget = false;
        var prt = panel.rectTransform; prt.anchorMin = new Vector2(0, 0.2f); prt.anchorMax = new Vector2(1, 0.85f); prt.offsetMin = prt.offsetMax = Vector2.zero;
        var cyan = new Color(0.55f, 0.95f, 1f);
        scoreText = MakeText(cgo.transform, 40, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(40, -28), cyan, font);
        hiText = MakeText(cgo.transform, 40, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -28), new Color(1f, 0.85f, 0.4f), font);
        livesText = MakeText(cgo.transform, 40, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -28), cyan, font);
        centerText = MakeText(cgo.transform, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 30), Color.white, font);
        popText = MakeText(cgo.transform, 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Color(1f, 0.95f, 0.5f), font);
        popText.text = "";
        fuseText = MakeText(cgo.transform, 34, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -84), Color.white, font);
    }

    bool Night => level % 2 == 0;
    string StageName => ((level + 1) / 2) + "일차 " + (Night ? "밤" : "낮") + " · " + Now.name;

    void RefreshHud()
    {
        scoreText.text = "크레딧  " + score.ToString("N0");
        hiText.text = "최고  " + hi.ToString("N0");
        livesText.text = StageName + "    " + new string('◆', Mathf.Max(0, lives));
    }

    void Pop(string s, Vector3 world)
    {
        popText.text = s; popT = 0.9f;
        Vector3 sp = cam.WorldToViewportPoint(world + Vector3.up);
        var rt = popText.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(sp.x, sp.y); rt.anchoredPosition = Vector2.zero;
    }

    // ------------------------------------------------------------------ 진행
    void StartLevel()
    {
        foreach (var kv in pickups) Destroy(kv.Value);
        pickups.Clear(); spinners.Clear();
        cellsLeft = 0;
        for (int y = 0; y < Maze.H; y++)
            for (int x = 0; x < Maze.W; x++)
            {
                char c = Maze.T[x, y];
                if (c != '.' && c != 'o') continue;
                var cell = new Vector2Int(x, y);
                var go = Spawn(c == 'o' ? "Core" : "Cell", pickupRoot);
                go.transform.position = Maze.World(cell) + Vector3.up * (c == 'o' ? 0.42f : 0.3f);
                pickups[cell] = go; spinners.Add(go.transform); cellsLeft++;
            }
        cellsTotal = cellsLeft;
        bonusAt.Clear(); bonusAt.Add(cellsTotal * 2 / 3); bonusAt.Add(cellsTotal / 3);
        bonusCell = Maze.PlayerStart + new Vector2Int(0, -4);
        if (!Maze.Open(bonusCell)) bonusCell = Maze.PlayerStart;
        if (bonus) Destroy(bonus);
        // 지역 테마
        var th = Now;
        mats["Floor"].SetColor("_BaseColor", th.path);
        mats["Wall"].SetColor("_BaseColor", th.wall);
        wallGlow.SetColor("_BaseColor", th.cap);
        BuildProps();
        // 낮/밤
        sunLight.intensity = Night ? 0.55f : 1.5f;
        sunLight.color = Night ? new Color(0.55f, 0.65f, 1f) : new Color(1f, 0.96f, 0.88f);
        RenderSettings.ambientLight = Night ? new Color(0.2f, 0.24f, 0.38f) : new Color(0.5f, 0.54f, 0.6f);
        mats["Sea"].SetColor("_BaseColor", Night ? new Color(0.05f, 0.16f, 0.3f) : new Color(0.12f, 0.42f, 0.58f));
        foreach (var l in lamps) l.enabled = Night;
        playerLight.enabled = Night;
        mats["LampGlow"].SetColor("_EmissionColor", Night ? new Color(1f, 0.8f, 0.5f) * 1.3f : Color.black);
        cam.backgroundColor = mats["Sea"].GetColor("_BaseColor");
        fuse = FuseMax;
        ResetActors();
        RefreshHud();
    }

    void ResetActors()
    {
        player.cell = Maze.PlayerStart; player.dir = Vector2Int.zero; player.t = 0; want = Vector2Int.zero; facing = Vector2Int.down;
        playerT.position = player.Pos; playerT.localScale = Vector3.one; playerT.rotation = Quaternion.LookRotation(Vector3.back);
        emp = 0; combo = 0;
        ClearZone();
        if (fuse < 10f) fuse = 10f;   // 부활 시 폭발 타이머 최소 10초
        if (fuseText) fuseText.text = "폭발 타이머  " + fuse.ToString("0.0");
        for (int i = 0; i < drones.Length; i++)
        {
            var d = drones[i];
            d.m.cell = d.home; d.m.dir = Vector2Int.zero; d.m.t = 0; d.dock = 0.5f + i * 2.5f; d.vulnerable = false; d.patrol = i;
            d.tr.position = d.m.Pos; SetDroneColor(d, d.color);
        }
        playerDistCell = new Vector2Int(-1, -1);
    }

    void SetState(State s)
    {
        state = s; stateT = 0;
        if (panel) panel.enabled = s == State.Title || s == State.Over;
        switch (s)
        {
            case State.Title:
                centerText.text = "<size=120><color=#5FF2FF>LUMIA CIRCUIT</color></size>\n<size=54>루미아 서킷</size>\n<size=28><color=#9FB8D0>비공식 팬 게임 · 루미아 섬</color></size>\n\n<size=40>Enter 또는 Space — 실험 시작</size>\n<size=30><color=#9FB8D0>방향키 / WASD 이동 · P 일시정지 · Esc 종료</color></size>\n<size=26><color=#9FB8D0>크레딧을 모두 모으세요 · VF 혈액 샘플을 먹으면 야생동물을 사냥할 수 있습니다 · 금지구역에 오래 있으면 폭사합니다</color></size>";
                break;
            case State.Ready: centerText.text = "<size=56>" + StageName + "</size>\n<size=80><color=#FFE066>READY</color></size>"; audioSrc.PlayOneShot(Sfx.Start); break;
            case State.Play: centerText.text = ""; break;
            case State.Dying: audioSrc.PlayOneShot(Sfx.Death); Burst(playerT.position, new Color(0.3f, 0.9f, 1f), 40); break;
            case State.Clear: centerText.text = "<size=80><color=#7CFF9E>지역 확보!</color></size>"; ClearZone(); audioSrc.PlayOneShot(Sfx.Win); break;
            case State.Over:
                centerText.text = "<size=100><color=#FF5577>GAME OVER</color></size>\n\n<size=40>Enter 또는 Space — 다시 시작</size>";
                break;
        }
    }

    void NewGame()
    {
        score = 0; lives = 3; level = startLevel;
        StartLevel();
        SetState(State.Ready);
    }

    void AddScore(int n)
    {
        score += n;
        if (score > hi && !autopilot) { hi = score; PlayerPrefs.SetInt("hi", hi); }
        RefreshHud();
    }

    // ------------------------------------------------------------------ 매 프레임
    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        HandleShot();
        ReadInput();
        if (paused) return;
        time += dt; stateT += dt;

        switch (state)
        {
            case State.Title:
            case State.Over:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) NewGame();
                break;
            case State.Ready:
                if (stateT > 1.6f) SetState(State.Play);
                break;
            case State.Play:
                Step(dt);
                break;
            case State.Dying:
                playerT.Rotate(0, 900 * dt, 0);
                playerT.localScale = Vector3.one * Mathf.Clamp01(1 - stateT / 1.1f);
                if (stateT > 1.6f)
                {
                    lives--; RefreshHud();
                    if (lives <= 0) { PlayerPrefs.Save(); SetState(State.Over); }
                    else { ResetActors(); SetState(State.Ready); }
                }
                break;
            case State.Clear:
                float f = Mathf.PingPong(stateT * 4, 1);
                wallGlow.SetColor("_BaseColor", Color.Lerp(Now.cap, Color.white, f));
                if (stateT > 2.2f) { level++; StartLevel(); SetState(State.Ready); }
                break;
        }
        Animate(dt);
    }

    void ReadInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == State.Title) Application.Quit();
            else { paused = false; PlayerPrefs.Save(); level = 1; lives = 3; score = 0; StartLevel(); SetState(State.Title); }
        }
        if (Input.GetKeyDown(KeyCode.P) && state == State.Play)
        {
            paused = !paused;
            centerText.text = paused ? "<size=80>일시정지</size>" : "";
        }
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) want = new Vector2Int(0, -1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) want = new Vector2Int(0, 1);
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) want = new Vector2Int(-1, 0);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) want = new Vector2Int(1, 0);
    }

    void Step(float dt)
    {
        if (autopilot) Autopilot();
        MovePlayer(dt);
        if (player.cell != playerDistCell) { playerDist = Maze.Distances(player.cell); playerDistCell = player.cell; }

        if (emp > 0)
        {
            emp -= dt;
            if (emp <= 0) { combo = 0; foreach (var d in drones) { d.vulnerable = false; SetDroneColor(d, d.color); } }
        }
        foreach (var d in drones) MoveDrone(d, dt);
        UpdateZone(dt);
        if (state != State.Play) return;

        // 보너스
        if (bonus)
        {
            bonusT -= dt;
            if (bonusT <= 0) Destroy(bonus);
            else if ((bonus.transform.position - playerT.position).sqrMagnitude < 0.5f)
            {
                int pts = 500 * level;
                AddScore(pts); Pop(Loot[UnityEngine.Random.Range(0, Loot.Length)] + " +" + pts, bonus.transform.position);
                Burst(bonus.transform.position, new Color(0.3f, 1f, 0.5f), 30); audioSrc.PlayOneShot(Sfx.Bonus); Destroy(bonus);
            }
        }

        // 충돌
        Vector3 pp = player.Pos;
        foreach (var d in drones)
        {
            if (d.dock > 0 || (d.m.Pos - pp).sqrMagnitude > 0.42f) continue;
            if (d.vulnerable)
            {
                combo++;
                int pts = 200 << Mathf.Min(combo - 1, 3);
                AddScore(pts); Pop("+" + pts, d.tr.position);
                Burst(d.tr.position, d.color, 36); audioSrc.PlayOneShot(Sfx.Zap);
                d.vulnerable = false; d.m.cell = d.home; d.m.dir = Vector2Int.zero; d.m.t = 0; d.dock = 3.5f; SetDroneColor(d, d.color);
            }
            else if (!autopilot) { SetState(State.Dying); return; }
        }
        if (cellsLeft <= 0) SetState(State.Clear);
    }

    void MovePlayer(float dt)
    {
        var p = player;
        if (want != Vector2Int.zero)
        {
            if (p.dir != Vector2Int.zero && want == -p.dir) { p.cell += p.dir; p.dir = -p.dir; p.t = 1 - p.t; }
            else if (p.dir == Vector2Int.zero && Maze.Open(p.cell + want)) p.dir = want;
        }
        if (p.dir == Vector2Int.zero) { Collect(p.cell); return; }
        facing = p.dir;
        p.t += (5.4f + Mathf.Min(level - 1, 4) * 0.1f) * dt;
        while (p.t >= 1)
        {
            p.cell += p.dir; p.t -= 1;
            Collect(p.cell);
            if (want != Vector2Int.zero && Maze.Open(p.cell + want)) p.dir = want;
            else if (!Maze.Open(p.cell + p.dir)) { p.dir = Vector2Int.zero; p.t = 0; break; }
            facing = p.dir;
        }
    }

    void Collect(Vector2Int cell)
    {
        if (!pickups.TryGetValue(cell, out var go)) return;
        bool core = Maze.T[cell.x, cell.y] == 'o';
        pickups.Remove(cell); spinners.Remove(go.transform);
        Vector3 pos = go.transform.position;
        Destroy(go);
        cellsLeft--;
        if (core)
        {
            AddScore(50);
            emp = Mathf.Max(3f, 7.5f - (level - 1) * 0.5f); combo = 0;
            foreach (var d in drones)
            {
                if (d.dock > 0) continue;
                d.vulnerable = true; SetDroneColor(d, Scared);
                if (d.m.dir != Vector2Int.zero) { d.m.cell += d.m.dir; d.m.dir = -d.m.dir; d.m.t = 1 - d.m.t; }
            }
            Burst(pos, new Color(1f, 0.1f, 0.2f), 50); audioSrc.PlayOneShot(Sfx.Core);
            Announce("<color=#FF6680>VF 각성!</color>");
        }
        else
        {
            AddScore(10);
            Burst(pos, new Color(1f, 0.8f, 0.25f), 5);
            audioSrc.PlayOneShot((blip++ & 1) == 0 ? Sfx.Blip1 : Sfx.Blip2, 0.6f);
        }
        if (bonusAt.Contains(cellsLeft) && !bonus)
        {
            bonus = Spawn("Bonus", pickupRoot);
            bonus.transform.position = Maze.World(bonusCell) + Vector3.up * 0.45f; bonusT = 9f;
        }
    }

    void MoveDrone(Drone d, float dt)
    {
        if (d.dock > 0)
        {
            d.dock -= dt;
            if (d.dock > 0) return;
        }
        if (d.m.dir == Vector2Int.zero) { d.m.dir = ChooseDir(d); if (d.m.dir == Vector2Int.zero) return; }
        float speed = d.vulnerable ? 2.7f : Mathf.Min(4.2f + (level - 1) * 0.2f + (Night ? 0.3f : 0f), 5.6f);
        d.m.t += speed * dt;
        while (d.m.t >= 1)
        {
            d.m.cell += d.m.dir; d.m.t -= 1;
            d.m.dir = ChooseDir(d);
            if (d.m.dir == Vector2Int.zero) { d.m.t = 0; break; }
        }
    }

    Vector2Int ChooseDir(Drone d)
    {
        var options = new List<Vector2Int>(4);
        foreach (var dir in Maze.Dirs) if (dir != -d.m.dir && Maze.Open(d.m.cell + dir)) options.Add(dir);
        if (options.Count == 0) { if (Maze.Open(d.m.cell - d.m.dir) && d.m.dir != Vector2Int.zero) return -d.m.dir; return Vector2Int.zero; }
        if (options.Count == 1) return options[0];
        if (playerDist == null) playerDist = Maze.Distances(player.cell);

        int[,] map = null; bool flee = d.vulnerable;
        int near = playerDist[d.m.cell.x, d.m.cell.y];
        if (flee) map = playerDist;
        else switch (d.kind)
        {
            case 0: map = playerDist; break;                                   // 추적자: 최단 경로로 쫓는다
            case 1:                                                            // 매복자: 진행 방향 앞을 노린다
                var ahead = player.cell;
                for (int i = 0; i < 4 && Maze.Open(ahead + facing); i++) ahead += facing;
                map = near <= 3 ? playerDist : Maze.Distances(ahead); break;
            case 2:                                                            // 순찰자: 모서리를 돌다가 가까우면 쫓는다
                if (near >= 0 && near <= 7) { map = playerDist; break; }
                var corners = new[] { new Vector2Int(1, 1), new Vector2Int(Maze.W - 2, 1), new Vector2Int(Maze.W - 2, Maze.H - 2), new Vector2Int(1, Maze.H - 2) };
                if (d.m.cell == corners[d.patrol % 4]) d.patrol++;
                map = Maze.Distances(corners[d.patrol % 4]); break;
            default:                                                           // 방랑자: 제멋대로 다니다 가까우면 쫓는다
                if (near >= 0 && near <= 5) map = playerDist; break;
        }
        if (map == null) return options[UnityEngine.Random.Range(0, options.Count)];

        Vector2Int best = options[0]; int bestV = flee ? int.MinValue : int.MaxValue;
        foreach (var o in options)
        {
            var c = d.m.cell + o; int v = map[c.x, c.y];
            if (v < 0) continue;
            if (flee ? v > bestV : v < bestV) { bestV = v; best = o; }
        }
        return best;
    }

    void Autopilot()
    {
        if (player.dir != Vector2Int.zero && player.t > 0.01f && player.t < 0.99f && want != Vector2Int.zero) return;
        // 가장 가까운 셀 쪽으로
        var dist = Maze.Distances(player.cell);
        Vector2Int target = player.cell; int bestD = int.MaxValue;
        foreach (var kv in pickups) { int v = dist[kv.Key.x, kv.Key.y]; if (v >= 0 && v < bestD) { bestD = v; target = kv.Key; } }
        var back = Maze.Distances(target);
        int bv = int.MaxValue;
        foreach (var dir in Maze.Dirs)
        {
            var c = player.cell + dir;
            if (!Maze.Open(c)) continue;
            if (back[c.x, c.y] >= 0 && back[c.x, c.y] < bv) { bv = back[c.x, c.y]; want = dir; }
        }
    }

    // ------------------------------------------------------------------ 연출
    void SetDroneColor(Drone d, Color c)
    {
        d.glow.SetColor("_BaseColor", c); d.glow.SetColor("_EmissionColor", c * 0.35f);
    }

    static Bounds WorldBounds(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r ? r.bounds : new Bounds(t.position, Vector3.zero);
    }

    static Part MakePart(Rig r, Transform t, Vector3? pivotAt)
    {
        Transform p = t;
        if (pivotAt.HasValue)
        {
            p = new GameObject(t.name + "Pivot").transform;
            p.SetParent(t.parent, false);
            p.position = pivotAt.Value;
            p.rotation = r.holder.rotation;
            t.SetParent(p, true);
        }
        var par = p.parent;
        return new Part
        {
            t = p, baseRel = p.localRotation,
            right = par.InverseTransformDirection(r.holder.right).normalized,
            up = par.InverseTransformDirection(r.holder.up).normalized,
            fwd = par.InverseTransformDirection(r.holder.forward).normalized,
            side = Mathf.Sign(r.holder.InverseTransformPoint(p.position).x + 1e-4f),
        };
    }

    /// 모델 이름으로 부품을 찾아 피벗을 단다 (holder가 원점·정면일 때 호출)
    Rig BuildRig(Transform model, string kind)
    {
        var r = new Rig { kind = kind, holder = model.parent, model = model,
            modelBase = model.localRotation, modelBasePos = model.localPosition, modelBaseScale = model.localScale };
        Vector3 body = r.holder.position + Vector3.up * 0.45f;
        Transform antennaTip = null; Part antenna = null;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.StartsWith("Leg") && n.Length >= 4 && t.GetComponent<Renderer>())
            {
                var b = WorldBounds(t);
                var p = MakePart(r, t, new Vector3(b.center.x, b.max.y - 0.02f, b.center.z));
                bool left = n[3] == 'L', front = n.Length == 4 || n.EndsWith("0");
                p.phase = left == front ? 0 : Mathf.PI;             // 네발: 대각선 다리끼리 같이 / 두발: 번갈아
                r.legs.Add(p);
            }
            else if (n == "WingL" || n == "WingR")
            {
                if (t.GetComponent<Renderer>())
                {
                    var b = WorldBounds(t);
                    r.wings.Add(MakePart(r, t, new Vector3(b.center.x, b.max.y, b.center.z)));
                }
                else
                {
                    // 박쥐: 어깨 자리의 빈 객체가 곧 축. 가져올 때 축 변환 회전이 섞여 있어 기존 회전은 버리고 로컬 Z로 직접 돌린다.
                    var p = MakePart(r, t, null);
                    p.baseRel = Quaternion.identity; p.fwd = Vector3.forward; p.side = n == "WingL" ? 1 : -1;
                    r.wings.Add(p);
                }
            }
            else if (n == "Tail" && t.GetComponent<Renderer>())
                r.tails.Add(MakePart(r, t, WorldBounds(t).ClosestPoint(body)));
            else if (n == "Antenna" && t.GetComponent<Renderer>())
            {
                var b = WorldBounds(t);
                antenna = MakePart(r, t, new Vector3(b.center.x, b.min.y, b.center.z));
                r.antennas.Add(antenna);
            }
            else if (n == "AntennaTip") antennaTip = t;
            else if ((n == "EyeL" || n == "EyeR") && kind == "Lumi" && t.GetComponent<Renderer>())
                r.eyes.Add(MakePart(r, t, WorldBounds(t).center));
        }
        if (antenna != null && antennaTip) antennaTip.SetParent(antenna.t, true);
        return r;
    }

    void UpdateRig(Rig r, float dt, bool scared, bool idle)
    {
        var h = r.holder;
        Vector3 pos = h.position; pos.y = 0;
        if (!r.init) { r.lastPos = pos; r.lastYaw = h.eulerAngles.y; r.init = true; }
        float dist = (pos - r.lastPos).magnitude; r.lastPos = pos;
        if (dist > 1.5f) dist = 0;                                       // 리셋으로 순간이동한 경우
        float spd = dt > 0 ? dist / dt : 0;
        r.move = Mathf.Lerp(r.move, Mathf.Clamp01(spd / 3.5f), 1 - Mathf.Exp(-10 * dt));
        float yaw = h.eulerAngles.y, yawRate = dt > 0 ? Mathf.DeltaAngle(r.lastYaw, yaw) / dt : 0; r.lastYaw = yaw;
        r.roll = Mathf.Lerp(r.roll, Mathf.Clamp(-yawRate * 0.025f, -14, 14), 1 - Mathf.Exp(-8 * dt));
        if (Mathf.Abs(yawRate) > 250) r.squash = Mathf.Max(r.squash, 0.7f);
        r.squash = Mathf.MoveTowards(r.squash, 0, dt * 3.5f);
        float stride = r.kind == "Chicken" ? 9f : r.kind == "Boar" ? 6.5f : 5f;   // 한 칸 걸을 때 도는 각(라디안)
        r.gait += dist * stride;

        float m = r.move, g = r.gait, t = time;
        float pitch = 0, roll = r.roll, lift = 0, jx = 0;
        Vector3 sc = Vector3.one;
        switch (r.kind)
        {
            case "Lumi":
                pitch = 12 * m;
                sc = new Vector3(1 + 0.12f * r.squash, 1 - 0.15f * r.squash, 1 + 0.12f * r.squash);
                break;
            case "Wolf":
                lift = Mathf.Abs(Mathf.Sin(g)) * 0.05f * m; pitch = Mathf.Sin(2 * g) * 3 * m; break;
            case "Boar":
                lift = Mathf.Abs(Mathf.Sin(g)) * 0.04f * m; pitch = 5 * m + Mathf.Sin(2 * g) * 3 * m; break;
            case "Chicken":
                roll += Mathf.Sin(g) * 9 * m; pitch = 6 * m + Mathf.Sin(2 * g) * 5 * m;
                lift = Mathf.Abs(Mathf.Sin(g)) * 0.03f * m; break;
            case "Bat":
                pitch = 12 * m; break;
        }
        if (scared) jx = Mathf.Sin(t * 45) * 0.025f;                    // 겁먹으면 부들부들
        if (idle) sc.y *= 1 + 0.03f * Mathf.Sin(t * 3);                 // 숨쉬기
        r.model.localPosition = r.modelBasePos + new Vector3(jx, lift, 0);
        r.model.localRotation = Quaternion.Euler(pitch, 0, roll) * r.modelBase;
        r.model.localScale = Vector3.Scale(r.modelBaseScale, sc);

        float legAmp = r.kind == "Chicken" ? 38 : 30;
        foreach (var p in r.legs)
            p.t.localRotation = Quaternion.AngleAxis(Mathf.Sin(g + p.phase) * legAmp * m, p.right) * p.baseRel;
        foreach (var p in r.wings)
        {
            float a = r.kind == "Bat" ? 38 * Mathf.Sin(t * (14 + 6 * m))
                    : scared ? 35 * Mathf.Sin(t * 22) : 12 * m * Mathf.Sin(t * 18);
            p.t.localRotation = Quaternion.AngleAxis(a * p.side, p.fwd) * p.baseRel;
        }
        foreach (var p in r.tails)
        {
            float a = r.kind == "Wolf" ? 20 * Mathf.Sin(t * 7) : 28 * Mathf.Sin(t * 13);
            p.t.localRotation = Quaternion.AngleAxis(a, p.up) * p.baseRel;
        }
        foreach (var p in r.antennas)
            p.t.localRotation = Quaternion.AngleAxis(-16 * m + 5 * Mathf.Sin(t * 4) + r.squash * 10, p.right) * p.baseRel;
        if (r.eyes.Count > 0)
        {
            r.blinkT -= dt;
            float lid = r.blinkT < 0 ? 0.15f : 1f;
            if (r.blinkT < -0.13f) r.blinkT = UnityEngine.Random.Range(2f, 5f);
            foreach (var p in r.eyes) p.t.localScale = new Vector3(1, lid, 1);
        }
    }

    void Animate(float dt)
    {
        // 플레이어
        if (state != State.Dying)
        {
            playerT.position = player.Pos + Vector3.up * (0.06f + 0.04f * Mathf.Sin(time * 5));
            var look = Quaternion.LookRotation(new Vector3(facing.x, 0, -facing.y));
            playerT.rotation = Quaternion.Slerp(playerT.rotation, look, 1 - Mathf.Exp(-18 * dt));
            UpdateRig(playerRig, dt, false, player.dir == Vector2Int.zero);
        }
        if (playerRing) playerRing.Rotate(0, 0, 240 * dt, Space.Self);

        // 야생동물
        foreach (var d in drones)
        {
            bool flying = d.rig.kind == "Bat";
            float bob = flying ? 0.12f + 0.06f * Mathf.Sin(time * 4 + d.kind * 1.7f) : 0f;
            d.tr.position = d.m.Pos + Vector3.up * bob;
            Vector3 face = d.m.dir != Vector2Int.zero ? new Vector3(d.m.dir.x, 0, -d.m.dir.y) : playerT.position - d.tr.position;
            face.y = 0;
            if (face.sqrMagnitude > 0.001f) d.tr.rotation = Quaternion.Slerp(d.tr.rotation, Quaternion.LookRotation(face), 1 - Mathf.Exp(-12 * dt));
            if (d.ring) { float rs = 1 + 0.08f * Mathf.Sin(time * 5 + d.kind); d.ring.localScale = new Vector3(rs, rs, rs); }
            d.tr.localScale = Vector3.one * (d.dock > 0 ? 0.75f : d.vulnerable ? 0.85f : 1f);
            UpdateRig(d.rig, dt, d.vulnerable, d.dock > 0 || d.m.dir == Vector2Int.zero);
            if (d.vulnerable && emp < 2f) SetDroneColor(d, Mathf.Repeat(emp * 5, 1) > 0.5f ? Color.white : Scared);
        }

        // 아이템
        for (int i = 0; i < spinners.Count; i++)
        {
            var t = spinners[i]; if (!t) continue;
            t.Rotate(0, 90 * dt, 0, Space.World);
            float s = 1.35f + 0.15f * Mathf.Sin(time * 4 + t.position.x * 0.8f + t.position.z * 0.6f);
            t.localScale = new Vector3(s, s, s);
        }
        if (bonus) { bonus.transform.Rotate(40 * dt, 160 * dt, 0); bonus.SetActive(bonusT > 2.5f || Mathf.Repeat(bonusT * 6, 1) > 0.4f); }

        // 카메라: 플레이어 쪽으로 살짝 기운다
        Vector3 off = new Vector3(playerT.position.x, 0, playerT.position.z) * 0.06f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, camBase + off, 1 - Mathf.Exp(-3 * dt));
        cam.transform.rotation = Quaternion.LookRotation(camLook - camBase);
        if (closeTarget)   // 검증용 근접 촬영
        {
            Vector3 p = closeTarget.position; p.y = 0;
            cam.transform.position = p + new Vector3(1.2f, 2.6f, -2.4f);
            cam.transform.LookAt(p + Vector3.up * 0.4f);
        }

        // 해안 거품: 물결 따라 오르내린다
        if (foam) foam.position = new Vector3(0, -0.23f + 0.035f * Mathf.Sin(time * 1.4f), 0);

        // 문구
        if (popT > 0) { popT -= dt; popText.rectTransform.anchoredPosition += Vector2.up * 30 * dt; if (popT <= 0) popText.text = ""; }
        if (state == State.Title || state == State.Over)
            centerText.color = new Color(1, 1, 1, 0.82f + 0.18f * Mathf.Sin(time * 3));
        else centerText.color = Color.white;
    }

    void Burst(Vector3 pos, Color c, int n)
    {
        var go = new GameObject("Burst"); go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startRotation3D = true; main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = cubeMesh;
        var m = Flat(new Material(litBase)); m.SetColor("_BaseColor", c); m.SetColor("_EmissionColor", c * 0.3f);
        r.sharedMaterial = m;
        ps.Emit(n);
        Destroy(go, 1.5f); Destroy(m, 1.6f);
    }

    // ------------------------------------------------------------------ 검증용 화면 저장 (-shot <경로>)
    void HandleShot()
    {
        if (shotPath == null) return;
        float t = Time.realtimeSinceStartup;
        if (shotStage == 0 && t > 2f) { ScreenCapture.CaptureScreenshot(shotPath + "_title.png"); shotStage = 1; }
        else if (shotStage == 1 && t > 3f) { NewGame(); shotStage = 2; }
        else if (shotStage == 2 && t > 12f) { ScreenCapture.CaptureScreenshot(shotPath + "_play.png"); shotStage = 3; }
        else if (shotStage >= 3 && shotStage < 3 + CloseShots.Length * 2)
        {
            // 근접 촬영: 대상마다 0.5초 따라간 뒤 찍는다
            int k = (shotStage - 3) / 2; bool capture = (shotStage - 3) % 2 == 1;
            float at = 12.3f + k * 0.9f + (capture ? 0.55f : 0f);
            if (t > at)
            {
                if (!capture) closeTarget = CloseShots[k] < 0 ? playerT : drones[CloseShots[k]].tr;
                else ScreenCapture.CaptureScreenshot(shotPath + "_close" + k + ".png");
                shotStage++;
            }
        }
        else if (shotStage == 3 + CloseShots.Length * 2 && t > 12.3f + CloseShots.Length * 0.9f + 0.5f) { Application.Quit(); shotStage++; }
    }
    static readonly int[] CloseShots = { -1, 0, 1, 2, 3 };   // 루미, 늑대, 박쥐, 멧돼지, 닭
    Transform closeTarget;
}
