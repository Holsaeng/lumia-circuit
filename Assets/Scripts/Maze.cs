using System.Collections.Generic;
using UnityEngine;

/// 미로 데이터. 왼쪽 절반(가운데 열 포함)만 적고 좌우 대칭으로 펼친다.
/// '#' 벽 · '.' 에너지 셀 · 'o' EMP 코어 · ' ' 빈 길 · 'P' 플레이어 시작 · 'D' 드론 도크
public static class Maze
{
    static readonly string[] Half =
    {
        "###########",
        "#o.........",
        "#.###.###.#",
        "#.....#....",
        "###.#.#.###",
        "#...#......",
        "#.###.###.#",
        "#.....#....",
        "#.#.#.#.# D",
        "#.#.#...#DD",
        "#.#.###.###",
        "#..........",
        "#.###.#.###",
        "#...#.#....",
        "###.#.###.#",
        "#.........P",
        "#.#######.#",
        "#o.........",
        "###########",
    };

    public static int W, H;
    public static char[,] T;
    public static Vector2Int PlayerStart;
    public static readonly List<Vector2Int> Docks = new List<Vector2Int>();
    public static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    public static void Load()
    {
        H = Half.Length;
        int half = Half[0].Length;
        W = half * 2 - 1;
        T = new char[W, H];
        Docks.Clear();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char c = Half[y][x < half ? x : W - 1 - x];
                if (c == 'P') { PlayerStart = new Vector2Int(x, y); c = ' '; }
                else if (c == 'D') { Docks.Add(new Vector2Int(x, y)); c = ' '; }
                T[x, y] = c;
            }
    }

    public static bool Open(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H && T[c.x, c.y] != '#';
    public static bool Wall(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && T[x, y] == '#';

    /// 격자 좌표 → 월드 좌표 (행 0이 화면 위쪽)
    public static Vector3 World(Vector2Int c) => new Vector3(c.x - (W - 1) * 0.5f, 0, (H - 1) * 0.5f - c.y);

    /// from에서 각 칸까지의 걸음 수 (못 가는 칸은 -1)
    public static int[,] Distances(Vector2Int from)
    {
        var d = new int[W, H];
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) d[x, y] = -1;
        if (!Open(from)) return d;
        var q = new Queue<Vector2Int>();
        d[from.x, from.y] = 0; q.Enqueue(from);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var dir in Dirs)
            {
                var n = c + dir;
                if (Open(n) && d[n.x, n.y] < 0) { d[n.x, n.y] = d[c.x, c.y] + 1; q.Enqueue(n); }
            }
        }
        return d;
    }

    /// 벽 메시: 서브메시 0 = 벽 몸체, 1 = 네온 테두리
    public static Mesh BuildWallMesh(float h, float trim)
    {
        var v = new List<Vector3>(); var body = new List<int>(); var glow = new List<int>();
        void Quad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
        const float e = 0.004f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!Wall(x, y)) continue;
                Vector3 c = World(new Vector2Int(x, y));
                float x0 = c.x - 0.5f, x1 = c.x + 0.5f, z0 = c.z - 0.5f, z1 = c.z + 0.5f;
                // 윗면
                Quad(body, new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0));
                // 옆면 + 테두리 (격자 y-1 = 월드 +z)
                bool n = !Wall(x, y - 1), s = !Wall(x, y + 1), w = !Wall(x - 1, y), ea = !Wall(x + 1, y);
                float ht = h - trim, t = trim;
                if (n)
                {
                    Quad(body, new Vector3(x1, 0, z1), new Vector3(x1, h, z1), new Vector3(x0, h, z1), new Vector3(x0, 0, z1));
                    Quad(glow, new Vector3(x1, ht, z1 + e), new Vector3(x1, h, z1 + e), new Vector3(x0, h, z1 + e), new Vector3(x0, ht, z1 + e));
                    Quad(glow, new Vector3(x0, h + e, z1 - t), new Vector3(x0, h + e, z1), new Vector3(x1, h + e, z1), new Vector3(x1, h + e, z1 - t));
                }
                if (s)
                {
                    Quad(body, new Vector3(x0, 0, z0), new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, 0, z0));
                    Quad(glow, new Vector3(x0, ht, z0 - e), new Vector3(x0, h, z0 - e), new Vector3(x1, h, z0 - e), new Vector3(x1, ht, z0 - e));
                    Quad(glow, new Vector3(x0, h + e, z0), new Vector3(x0, h + e, z0 + t), new Vector3(x1, h + e, z0 + t), new Vector3(x1, h + e, z0));
                }
                if (w)
                {
                    Quad(body, new Vector3(x0, 0, z1), new Vector3(x0, h, z1), new Vector3(x0, h, z0), new Vector3(x0, 0, z0));
                    Quad(glow, new Vector3(x0 - e, ht, z1), new Vector3(x0 - e, h, z1), new Vector3(x0 - e, h, z0), new Vector3(x0 - e, ht, z0));
                    Quad(glow, new Vector3(x0, h + e, z0), new Vector3(x0, h + e, z1), new Vector3(x0 + t, h + e, z1), new Vector3(x0 + t, h + e, z0));
                }
                if (ea)
                {
                    Quad(body, new Vector3(x1, 0, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x1, 0, z1));
                    Quad(glow, new Vector3(x1 + e, ht, z0), new Vector3(x1 + e, h, z0), new Vector3(x1 + e, h, z1), new Vector3(x1 + e, ht, z1));
                    Quad(glow, new Vector3(x1 - t, h + e, z0), new Vector3(x1 - t, h + e, z1), new Vector3(x1, h + e, z1), new Vector3(x1, h + e, z0));
                }
                // 오목한 모서리의 빈틈 메우기
                for (int dx = -1; dx <= 1; dx += 2)
                    for (int dy = -1; dy <= 1; dy += 2)
                    {
                        if (!Wall(x + dx, y) || !Wall(x, y + dy) || Wall(x + dx, y + dy)) continue;
                        if (x + dx < 0 || x + dx >= W || y + dy < 0 || y + dy >= H) continue;
                        float cx0 = dx > 0 ? x1 - t : x0, cz0 = dy < 0 ? z1 - t : z0;
                        Quad(glow, new Vector3(cx0, h + e, cz0), new Vector3(cx0, h + e, cz0 + t), new Vector3(cx0 + t, h + e, cz0 + t), new Vector3(cx0 + t, h + e, cz0));
                    }
            }
        var m = new Mesh { name = "Walls", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v);
        // 담 아래쪽일수록 어둡게 (땅에 닿는 곳의 그늘)
        var col = new List<Color>(v.Count);
        foreach (var p in v) { float a = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p.y / h)); col.Add(new Color(a, a, a, 1)); }
        m.SetColors(col);
        m.subMeshCount = 2;
        m.SetTriangles(body, 0);
        m.SetTriangles(glow, 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
