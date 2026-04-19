using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

[ExecuteInEditMode]
public class GrassInteractions : MonoBehaviour
{
    /// 全局就一个，instance方便获取
    public static GrassInteractions instance;
    public List<GrassCollider2> grassColliders = new();
    public Matrix4x4[] grassColliderMatrices = new Matrix4x4[64];
    public int grassColliderCount = 0;
    public Material material;
    
    // TODO: public 出去一个size,坐标依据transform.position.xz自动调整
    public Vector4 AABB_Range = new(-1, -1, 1, 1);

    void Start()
    {
        grassColliders.Clear();
        grassColliderCount = 0;
    }

    void OnEnable()
    {
        instance = this;
    }

    void OnDisable()
    {
        instance = null;
    }

    void OnDrawGizmos()
    {
        if (Camera.main == null)
            return;

        float4 aabb = new(AABB_Range);
        float2 min = aabb.xy, max = aabb.zw;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(new Vector3((min.x + max.x) * 0.5f, 0, (min.y + max.y) * 0.5f), new Vector3(max.x - min.x, 0, max.y - min.y));
    }

    public void UpdateGrassColliderMatrices()
    {
        for (int i = 0; i < grassColliders.Count; i++)
        {
            var collider = grassColliders[i];
            Matrix4x4 m = collider.transform.localToWorldMatrix;
            Vector3 scale = collider.transform.localScale;
            
            m.SetColumn(0, Vector3.right * collider.radius*scale.x);
            m.SetColumn(1, Vector3.up * collider.radius*scale.y);
            m.SetColumn(2, Vector3.forward * collider.radius*scale.z);
            grassColliderMatrices[i] = m;
        }
        grassColliderCount = grassColliders.Count;
    }


}
