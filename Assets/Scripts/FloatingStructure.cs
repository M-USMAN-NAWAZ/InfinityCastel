using UnityEngine;

public class FloatingStructure : MonoBehaviour
{
    [Header("Legacy Float")]
    public float amplitude = 0f;
    public float speed = 1f;

    private Vector3 startLocalPosition;

    private void Awake()
    {
        startLocalPosition = transform.localPosition;
    }

    private void Update()
    {
        if (amplitude <= 0f)
            return;

        transform.localPosition = startLocalPosition + Vector3.up * Mathf.Sin(Time.time * speed) * amplitude;
    }
}
