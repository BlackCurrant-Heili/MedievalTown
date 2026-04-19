using UnityEngine;

public class MeshTool
{
    static Mesh _quad;
    public static Mesh Quad
    {
        get
        {
            if (_quad == null)
            {
                _quad = new Mesh();
                _quad.vertices = new Vector3[] {
                    new Vector3(-0.5f, 0, -0.5f),
                    new Vector3(0.5f, 0, -0.5f),    
                    new Vector3(-0.5f, 0, 0.5f),
                    new Vector3(0.5f, 0, 0.5f)
                };
                _quad.triangles = new int[] {
                    0, 1, 2, 1, 3, 2
                };
                _quad.uv = new Vector2[] {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                };  
                _quad.normals = new Vector3[] {
                    Vector3.up, Vector3.up, Vector3.up, Vector3.up
                };
                _quad.name = "Quad";
                _quad.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
            }

            return _quad;
        }
    }
}