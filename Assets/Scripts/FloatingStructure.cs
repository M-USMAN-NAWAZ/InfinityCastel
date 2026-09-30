using UnityEngine;

public class FloatingStructure : MonoBehaviour
{
    [Header("Legacy Float")]
    public float amplitude = 0f;
    public float speed = 1f;

    private void Awake()
    {
        // Retained for prefab compatibility. Castle motion is owned by the pooled manager.
        enabled = false;
    }
}
