using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawn : MonoBehaviour
{
    [SerializeField] private List<GameObject> _enemyPrefab;
    [SerializeField] private float _spawnTime = 3.0f;
    [SerializeField] private Transform _enemySpawnPoint;

    private GameObject currentEnemy;
    private bool isWaiting;

    void Start()
    {
        isWaiting = false;
        Spawn();
    }

    void Update()
    {
        if(currentEnemy == null && !isWaiting)
        {
            StartCoroutine(SpawnAfterDelay(_spawnTime));
        }
    }

    private void SetSpawnTime()
    {
        _spawnTime = _spawnTime * (9.0f / 10.0f); 
    }

    private void Spawn()
    {
        int index = Random.Range(0, _enemyPrefab.Count);
        currentEnemy = Instantiate(_enemyPrefab[index], _enemySpawnPoint.position, Quaternion.Euler(0f, 0f, 0f));
    }
    private IEnumerator SpawnAfterDelay(float spawnTime)
    {
        isWaiting = true;
        yield return new WaitForSeconds(spawnTime);
        Spawn();
        SetSpawnTime();
        isWaiting = false;
    }
}
