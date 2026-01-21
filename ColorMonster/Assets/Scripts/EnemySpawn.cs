using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawn : MonoBehaviour
{
    [Header("出現させる敵のプレハブ")]
    [SerializeField] private List<GameObject> _enemyPrefab; // goblin / hobgoblin / troll / wolf

    [Header("スポーン間隔")]
    [SerializeField] private float _spawnTime = 3.0f;

    [Header("スポーン位置")]
    [SerializeField] private Transform _enemySpawnPoint;

    private GameObject currentEnemy; // 現在存在する敵
    private bool isWaiting;           // スポーン待機中かどうか

    void Start()
    {
        isWaiting = false;
        Spawn(); // 最初の敵を出現
    }

    void Update()
    {
        // 現在敵がいなく、かつ待機中でなければ次の敵をスポーン
        if (currentEnemy == null && !isWaiting)
        {
            StartCoroutine(SpawnAfterDelay(_spawnTime));
        }
    }

    // スポーン間隔を徐々に短くする（10%ずつ）
    private void SetSpawnTime()
    {
        _spawnTime = _spawnTime * 0.9f;
    }

    // 敵を1体ランダムに生成
    private void Spawn()
    {
        if (_enemyPrefab.Count == 0)
        {
            Debug.LogWarning("Enemy Prefab がリストに登録されていません！");
            return;
        }

        int index = Random.Range(0, _enemyPrefab.Count);
        currentEnemy = Instantiate(
            _enemyPrefab[index],
            _enemySpawnPoint.position,
            Quaternion.identity
        );
    }

    // スポーンまで待機
    private IEnumerator SpawnAfterDelay(float spawnTime)
    {
        isWaiting = true;
        yield return new WaitForSeconds(spawnTime);
        Spawn();
        SetSpawnTime();
        isWaiting = false;
    }
}
