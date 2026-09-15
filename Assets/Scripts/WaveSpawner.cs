using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Target Player transform (cached via FindWithTag in Start if null)")]
    [SerializeField] private Transform player;

    [Header("Spawn Settings")]
    [SerializeField] public GameObject chaserPrefab;
    [SerializeField] public GameObject sniperPrefab;
    [SerializeField] private float spawnDistance = 9f;
    [SerializeField] private float initialSpawnInterval = 1.5f;
    [SerializeField] private float minSpawnInterval = 0.5f;
    [SerializeField] private float intervalDecreaseRate = 0.05f;
    [SerializeField] private float sniperSpawnChance = 0.25f;

    [Header("Arena Boundary & Outer Perimeter")]
    [SerializeField] private float halfWidth = 18f;
    [SerializeField] private float halfHeight = 10f;
    [SerializeField] private float spawnOffsetOutside = 1.5f;

    public static readonly List<GameObject> activeEnemies = new List<GameObject>();
    public static IReadOnlyList<GameObject> ActiveEnemies => activeEnemies;

    public static void RegisterEnemy(GameObject enemy)
    {
        if (enemy != null && !activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);
        }
    }

    public static void UnregisterEnemy(GameObject enemy)
    {
        if (enemy != null)
        {
            activeEnemies.Remove(enemy);
        }
    }

    private float currentSpawnInterval;
    private float spawnTimer;
    private bool isSpawning = true;
    private Collider2D[] cachedBoundaryColliders;

    public Transform Player { get => player; set => player = value; }
    public Transform PlayerTransform { get => player; set => player = value; }
    public GameObject ChaserPrefab { get => chaserPrefab; set => chaserPrefab = value; }
    public GameObject EnemyPrefab { get => chaserPrefab; set => chaserPrefab = value; }
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

    private Transform playerTransform;

    private void Awake()
    {
        CacheBoundaryColliders();
    }

    private void Start()
    {
        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
        }
        else if (player != null)
        {
            playerTransform = player;
        }
        else
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }
        player = playerTransform;

        CacheBoundaryColliders();

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

    private void CachePlayer()
    {
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy) return;

        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
            player = playerTransform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            player = playerTransform;
        }
    }

    private void CacheBoundaryColliders()
    {
        if (cachedBoundaryColliders != null && cachedBoundaryColliders.Length > 0) return;
        GameObject boundaryObj = GameObject.Find("Arena_Boundaries");
        if (boundaryObj != null)
        {
            cachedBoundaryColliders = boundaryObj.GetComponentsInChildren<Collider2D>();
        }
    }

    private void Update()
    {
        // Stop spawning if flag is disabled
        if (!isSpawning)
        {
            return;
        }

        if (playerTransform == null)
        {
            if (PlayerController.Instance != null)
            {
                playerTransform = PlayerController.Instance.transform;
                player = playerTransform;
            }
            if (playerTransform == null) return;
        }

        if (chaserPrefab == null && sniperPrefab == null)
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
        CachePlayer();
        CacheBoundaryColliders();
        activeEnemies.RemoveAll(e => e == null);
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
        if (playerTransform == null)
        {
            if (PlayerController.Instance != null)
            {
                playerTransform = PlayerController.Instance.transform;
                player = playerTransform;
            }
            if (playerTransform == null) return;
        }

        if (chaserPrefab == null && sniperPrefab == null)
        {
            return;
        }

        // Ensure enemy spawn coordinates originate along the outer perimeter
        // at distance just outside halfWidth/halfHeight so enemies spawn outside and enter through the boundary line
        Vector3 spawnPosition = GetRandomOuterPerimeterPosition();
        GameObject spawnedEnemy = null;

        float sniperChance = sniperSpawnChance;
        if (sniperPrefab != null && (Random.value < sniperChance || chaserPrefab == null))
        {
            spawnedEnemy = Instantiate(sniperPrefab, spawnPosition, Quaternion.identity);
        }
        else if (chaserPrefab != null)
        {
            spawnedEnemy = Instantiate(chaserPrefab, spawnPosition, Quaternion.identity);
        }

        if (spawnedEnemy != null)
        {
            RegisterEnemy(spawnedEnemy);
            StartCoroutine(HandleEnemyArenaEntry(spawnedEnemy));
        }
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

        if (cachedBoundaryColliders == null || cachedBoundaryColliders.Length == 0)
        {
            CacheBoundaryColliders();
        }
        if (cachedBoundaryColliders == null || cachedBoundaryColliders.Length == 0) yield break;

        foreach (Collider2D wall in cachedBoundaryColliders)
        {
            if (wall != null && enemyCollider != null)
            {
                Physics2D.IgnoreCollision(enemyCollider, wall, true);
            }
        }

        // Wait until enemy has entered inside the arena boundaries (with 2.5s safety timeout)
        float timeout = 2.5f;
        while (enemy != null && timeout > 0f && (Mathf.Abs(enemy.transform.position.x) > halfWidth || Mathf.Abs(enemy.transform.position.y) > halfHeight))
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Restore wall collisions so enemy cannot escape
        if (enemy != null && enemyCollider != null)
        {
            foreach (Collider2D wall in cachedBoundaryColliders)
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
