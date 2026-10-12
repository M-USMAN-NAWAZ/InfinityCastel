using UnityEngine;

/// <summary>Authored surfaces and verified openings for pooled architectural modules.</summary>
[DisallowMultipleComponent]
public sealed class CastleArchitectureModule : MonoBehaviour
{
    public enum ModuleKind { Room, Corridor, Roof, Stair }
    [System.Serializable]
    public struct Port
    {
        public Vector3 position;
        public Vector3 outward;
        public float width;
    }

    public ModuleKind kind;
    public Bounds localBounds;
    public Vector3 floorPoint;
    public Vector2 floorSize;
    public Vector3 roofPoint;
    public Vector2 roofSize;
    public Port[] ports = System.Array.Empty<Port>();
    public bool requiresPassageRepair;
    public Bounds WorldBounds
    {
        get
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 size = localBounds.size;
            return new Bounds(matrix.MultiplyPoint3x4(localBounds.center), new Vector3(
                Mathf.Abs(matrix.m00) * size.x + Mathf.Abs(matrix.m01) * size.y + Mathf.Abs(matrix.m02) * size.z,
                Mathf.Abs(matrix.m10) * size.x + Mathf.Abs(matrix.m11) * size.y + Mathf.Abs(matrix.m12) * size.z,
                Mathf.Abs(matrix.m20) * size.x + Mathf.Abs(matrix.m21) * size.y + Mathf.Abs(matrix.m22) * size.z));
        }
    }
    public Vector3 WorldFloor => transform.TransformPoint(floorPoint);
}
