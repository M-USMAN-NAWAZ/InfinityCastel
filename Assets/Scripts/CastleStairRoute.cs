using UnityEngine;

// Measured walking centreline of the intact authored switchback, not a straight ramp.
public sealed class CastleStairRoute : MonoBehaviour
{
    public bool hasAuthoredTreads;
    public Vector3[] points;
    public float laneWidth;
}
