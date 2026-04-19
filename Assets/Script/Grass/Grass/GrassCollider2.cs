using UnityEngine;

[ExecuteInEditMode]
public class GrassCollider2 : MonoBehaviour
{
    public float radius = 1.0f;

    void OnEnable()
    {
        GrassInteractions.instance.grassColliders.Add(this);
    }

    void OnDisable()
    {
        GrassInteractions.instance.grassColliders.Remove(this);
    }
}
