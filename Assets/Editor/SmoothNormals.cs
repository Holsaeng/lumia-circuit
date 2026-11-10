using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// FBX를 가져올 때 같은 자리 정점들의 노멀을 평균내 TEXCOORD3에 넣는다.
/// 툰 셰이더 외곽선이 각진(플랫 셰이딩) 모델에서도 끊기지 않게 하려는 것.
public class SmoothNormals : AssetPostprocessor
{
    public override uint GetVersion() => 1;   // 바꾸면 모든 모델을 다시 가져온다

    void OnPostprocessModel(GameObject root)
    {
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh != null) Bake(f.sharedMesh);
    }

    static void Bake(Mesh mesh)
    {
        var v = mesh.vertices;
        var n = mesh.normals;
        if (n == null || n.Length != v.Length) return;
        var sum = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
        for (int i = 0; i < v.Length; i++)
        {
            var k = Key(v[i]);
            sum.TryGetValue(k, out var s);
            sum[k] = s + n[i];
        }
        var outN = new List<Vector3>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            var s = sum[Key(v[i])];
            outN.Add(s.sqrMagnitude > 1e-8f ? s.normalized : n[i]);
        }
        mesh.SetUVs(3, outN);
    }
}
