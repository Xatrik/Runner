using System.Collections;
using UnityEngine;

public class Spawner : MonoBehaviour
{
    [Tooltip("Seconds between spawn waves.")]
    [SerializeField] private float spawnTimer = 2f;
    [Tooltip("Objects spawned per wave.")]
    [SerializeField] private int spawnAmount = 1;
    [Tooltip("Delay between each object within a wave.")]
    [SerializeField] private float spawnStagger = 0.1f;
    [SerializeField] private GameObject spawnObject;
    [SerializeField] private Transform spawnPoint;

    private void Start()
    {
        StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            for (int i = 0; i < spawnAmount; i++)
            {
                if (spawnObject != null)
                    Instantiate(spawnObject, spawnPoint.position, transform.rotation);

                if (i < spawnAmount - 1)
                    yield return new WaitForSeconds(spawnStagger);
            }

            yield return new WaitForSeconds(spawnTimer);
        }
    }
}
