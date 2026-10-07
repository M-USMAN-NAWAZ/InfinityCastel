using UnityEngine;

public sealed class InfinityCastleTrainingBootstrap : MonoBehaviour
{
    [Range(1, 64)] public int parallelAgents = 32;
    public int seed = 1729;
    private void Start()
    {
        for (int i = 0; i < parallelAgents; i++) InfinityCastleMLAgent.Create(transform, true, seed + i * 97);
    }
}
