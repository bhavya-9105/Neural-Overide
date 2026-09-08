using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Target Player transform (cached via FindWithTag in Start if null)")]
    [SerializeField] private Transform player;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject enemyPrefab;
    public GameObject sniperPrefab;
    [SerializeField] private float spawnDistance = 9f;
    [SerializeField] private float initialSpawnInterval = 1.5f;
    [SerializeField] private float minSpawnInterval = 0.5f;
    [SerializeField] private float intervalDecreaseRate = 0.05f;
    [SerializeField] private float sniperSpawnChance = 0.25f;

    [Header("Arena Boundary & Outer Perimeter")]
    [SerializeField] private float halfWidth = 18f;
    [SerializeField] private float halfHeight = 10f;
    [SerializeField] private float spawnOffsetOutside = 1.5f;

    private float currentSpawnInterval;
    private float spawnTimer;
    private bool isSpawning = true;

    public Transform Player { get => player; set => player = value; }
    public Transform PlayerTransform { get => player; set => player = value; }
    public GameObject EnemyPrefab { get => enemyPrefab; set => enemyPrefab = value; }
    public GameObject SniperPrefab { get => sniperPrefab; set => sniperPrefab = value; }
    public float SpawnDistance { get => spawnDistance; set => spawnDistance = value; }
    public float InitialSpawnInterval { get => initialSpawnInterval; set => initialSpawnInterval = value; }
    public float MinSpawnInterval { get => minSpawnInterval; set => minSpawnInterval = value; }
    public float CurrentSpawnInterval => currentSpawnInterval;
    public bool IsSpawning => isSpawning;
    public float HalfWidth { get => halfWidth; set => halfWidth = value; }
    public float HalfHeight { get => halfHeight; set => halfHeight = value; }
    public float SpawnOffsetOutside { get => spawnOffsetOutside; set => spawnOffsetOutside = value; }
    public float SniperSpawnChance { get => sniperSpawnChance; set => sniperSpawnChance = value; }

    public void ApplyDifficulty(GameManager.Difficulty diff)
    {
        ApplyDifficulty((int)diff);
    }

    public void ApplyDifficulty(int diff)
    {
        switch (diff)
        {
            case 0: // Easy (20s) - relaxed
                initialSpawnInterval = 2.0f;
                minSpawnInterval = 1.0f;
                sniperSpawnChance = 0.15f;
                break;
            case 1: // Normal (50s) - standard
                initialSpawnInterval = 1.2f;
                minSpawnInterval = 0.5f;
                sniperSpawnChance = 0.25f;
                break;
            case 2: // Expert (90s) - aggressive (0.7s, more snipers)
                initialSpawnInterval = 0.7f;
                minSpawnInterval = 0.35f;
                sniperSpawnChance = 0.40f;
                break;
        }

        currentSpawnInterval = initialSpawnInterval;
    }

    private void Start()
    {
        FindPlayer();

        if (GameManager.Instance != null)
        {
            ApplyDifficulty(GameManager.Instance.currentDifficulty);
        }
        else
        {
            currentSpawnInterval = initialSpawnInterval;
        }

        spawnTimer = 0f;
        isSpawning = true;
    }

    private void FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy) return;

        GameObject playerObj = GameObject.FindWithTag("Player") ?? GameObject.Find("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
    }

    private void Update()
    {
        // Stop spawning if flag is disabled
        if (!isSpawning)
        {
            return;
        }

        if (player == null || !player.gameObject.activeInHierarchy)
        {
            FindPlayer();
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                return;
            }
        }

        if (enemyPrefab == null && sniperPrefab == null)
        {
            return;
        }

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= currentSpawnInterval)
        {
            spawnTimer = 0f;
            SpawnEnemyAlongPerimeter();

            // Gradually decrease spawn interval over time
            if (currentSpawnInterval > minSpawnInterval)
            {
                currentSpawnInterval = Mathf.Max(minSpawnInterval, currentSpawnInterval - intervalDecreaseRate);
            }
        }
    }

    public void StopSpawning()
    {
        isSpawning = false;
    }

    public void ResetSpawner()
    {
        FindPlayer();
        spawnTimer = 0f;
        if (GameManager.Instance != null)
        {
            ApplyDifficulty(GameManager.Instance.currentDifficulty);
        }
        else
        {
            currentSpawnInterval = initialSpawnInterval;
        }
        isSpawning = true;
        enabled = true;
    }

    public void SpawnEnemy()
    {
        SpawnEnemyAlongPerimeter();
    }

    private void SpawnEnemyAlongPerimeter()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        // Chance to spawn sniperPrefab instead of EnemyChaser based on difficulty
        float sniperChance = sniperSpawnChance;

        GameObject prefabToSpawn = (sniperPrefab != null && Random.value < sniperChance) ? sniperPrefab : enemyPrefab;
        if (prefabToSpawn == null)
        {
            prefabToSpawn = enemyPrefab != null ? enemyPrefab : sniperPrefab;
        }

        if (prefabToSpawn == null)
        {
            return;
        }

        // Ensure enemy spawn coordinates originate along the outer perimeter
        // at distance just outside halfWidth/halfHeight so enemies spawn outside and enter through the boundary line
        Vector3 spawnPosition = GetRandomOuterPerimeterPosition();

        GameObject spawnedEnemy = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
        StartCoroutine(HandleEnemyArenaEntry(spawnedEnemy));
    }

    private Vector3 GetRandomOuterPerimeterPosition()
    {
        float edgeWidth = halfWidth * 2f;
        float edgeHeight = halfHeight * 2f;
        float totalPerimeter = (edgeWidth + edgeHeight) * 2f;

        float roll = Random.Range(0f, totalPerimeter);

        if (roll < edgeWidth)
        {
            // Top outer edge (just above halfHeight)
            float x = Random.Range(-halfWidth, halfWidth);
            return new Vector3(x, halfHeight + spawnOffsetOutside, 0f);
        }
        else if (roll < edgeWidth * 2f)
        {
            // Bottom outer edge (just below -halfHeight)
            float x = Random.Range(-halfWidth, halfWidth);
            return new Vector3(x, -halfHeight - spawnOffsetOutside, 0f);
        }
        else if (roll < edgeWidth * 2f + edgeHeight)
        {
            // Left outer edge (just left of -halfWidth)
            float y = Random.Range(-halfHeight, halfHeight);
            return new Vector3(-halfWidth - spawnOffsetOutside, y, 0f);
        }
        else
        {
            // Right outer edge (just right of halfWidth)
            float y = Random.Range(-halfHeight, halfHeight);
            return new Vector3(halfWidth + spawnOffsetOutside, y, 0f);
        }
    }

    private IEnumerator HandleEnemyArenaEntry(GameObject enemy)
    {
        if (enemy == null) yield break;

        Collider2D enemyCollider = enemy.GetComponent<Collider2D>();
        if (enemyCollider == null || enemyCollider.isTrigger) yield break;

        GameObject boundaryObj = GameObject.Find("Arena_Boundaries");
        if (boundaryObj == null) yield break;

        Collider2D[] wallColliders = boundaryObj.GetComponentsInChildren<Collider2D>();
        foreach (Collider2D wall in wallColliders)
        {
            if (wall != null && enemyCollider != null)
            {
                Physics2D.IgnoreCollision(enemyCollider, wall, true);
            }
        }

        // Wait until enemy has entered inside the arena boundaries
        while (enemy != null && (Mathf.Abs(enemy.transform.position.x) > halfWidth || Mathf.Abs(enemy.transform.position.y) > halfHeight))
        {
            yield return null;
        }

        // Restore wall collisions so enemy cannot escape
        if (enemy != null && enemyCollider != null)
        {
            foreach (Collider2D wall in wallColliders)
            {
                if (wall != null && enemyCollider != null)
                {
                    Physics2D.IgnoreCollision(enemyCollider, wall, false);
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfWidth * 2f, halfHeight * 2f, 0f));

        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3((halfWidth + spawnOffsetOutside) * 2f, (halfHeight + spawnOffsetOutside) * 2f, 0f));
    }
}
