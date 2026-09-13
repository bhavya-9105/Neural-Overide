using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class SniperNode : MonoBehaviour, IHijackable
{
    [Header("Stats")]
    [SerializeField] private int health = 3;
    [SerializeField] private float moveSpeed = 1.8f;
    [SerializeField] private float preferredDistance = 7f;
    [SerializeField] private float fireInterval = 2.5f;
    public int scoreReward = 200;

    [Header("Combat References")]
    public GameObject projectilePrefab;
    [SerializeField] private float hitFlashDuration = 0.05f;

    [Header("Puppet Protocol / Neural Hijack")]
    public bool isHijacked = false;
    private float hijackTimer = 0f;
    private Transform minionTarget;
    private Coroutine scalePulseCoroutine;
    private float minionAttackCooldown = 0f;
    private Vector3 originalScale;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Color originalColor = Color.white;
    private Coroutine hitFlashCoroutine;
    private Transform playerTransform;
    private float fireTimer = 0f;
    private bool isDead = false;

    public int Health => health;
    public bool IsHijacked => isHijacked;
    public float HijackRemainingTime => hijackTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.isTrigger = false;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        fireTimer = Random.Range(0.5f, fireInterval);
        originalScale = transform.localScale;

        int enemiesLayer = LayerMask.NameToLayer("Enemies");
        if (enemiesLayer == -1) enemiesLayer = LayerMask.NameToLayer("Enemy");
        if (enemiesLayer != -1 && (gameObject.layer == 0 || gameObject.layer == LayerMask.NameToLayer("Default")))
        {
            gameObject.layer = enemiesLayer;
        }

        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker == null)
        {
            marker = gameObject.AddComponent<MiniMapMarker>();
        }
        marker.markerType = MiniMapMarker.MarkerType.Sniper;
    }

    public static readonly Color DefaultEnemyColor = new Color(0.984f, 0.443f, 0.522f, 1f); // Coral #FB7185

    private void Start()
    {
        FindPlayer();

        if (GameManager.Instance != null)
        {
            moveSpeed *= GameManager.Instance.enemySpeedMultiplier;
        }

        if (spriteRenderer != null && !isHijacked)
        {
            spriteRenderer.color = DefaultEnemyColor;
            originalColor = spriteRenderer.color;
        }
    }

    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player") ?? GameObject.Find("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }
    }

    private void Update()
    {
        if (isDead || Time.timeScale == 0f) return;

        if (minionAttackCooldown > 0f)
        {
            minionAttackCooldown -= Time.deltaTime;
        }

        if (isHijacked)
        {
            hijackTimer -= Time.deltaTime;

            if (hijackTimer <= 0f)
            {
                OverloadDetonation();
                return;
            }

            // Aim and shoot at closest wild enemy
            if (minionTarget == null || !minionTarget.gameObject.activeInHierarchy)
            {
                FindClosestWildEnemy();
            }

            if (minionTarget != null)
            {
                Vector2 direction = ((Vector2)minionTarget.position - (Vector2)transform.position).normalized;
                if (direction != Vector2.zero)
                {
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
                }

                fireTimer -= Time.deltaTime;
                if (fireTimer <= 0f)
                {
                    fireTimer = fireInterval * 0.8f; // Slightly faster fire rate while slaved
                    Shoot(direction);
                }
            }
            return;
        }

        // Wild Sniper aiming & shooting at player
        if (playerTransform == null || !playerTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        Vector2 playerDir = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        if (playerDir != Vector2.zero)
        {
            float angle = Mathf.Atan2(playerDir.y, playerDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
        }

        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            fireTimer = fireInterval;
            Shoot(playerDir);
        }
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        // If hijacked: move aggressively towards closest rogue node
        if (isHijacked)
        {
            if (minionTarget == null || !minionTarget.gameObject.activeInHierarchy)
            {
                FindClosestWildEnemy();
            }

            if (minionTarget != null && rb != null)
            {
                Vector2 myPos = rb.position;
                Vector2 targetPos = minionTarget.position;
                Vector2 newPos = Vector2.MoveTowards(myPos, targetPos, moveSpeed * 1.5f * Time.fixedDeltaTime);
                rb.MovePosition(newPos);
            }
            return;
        }

        // Wild Sniper: maintain distance from player
        if (playerTransform == null || !playerTransform.gameObject.activeInHierarchy)
        {
            FindPlayer();
            if (playerTransform == null)
            {
#pragma warning disable 0618
                rb.velocity = Vector2.zero;
#pragma warning restore 0618
                return;
            }
        }

        Vector2 playerPos = playerTransform.position;
        Vector2 curPos = transform.position;
        float distance = Vector2.Distance(curPos, playerPos);

        if (distance < preferredDistance)
        {
            Vector2 awayDir = (curPos - playerPos).normalized;
            if (awayDir == Vector2.zero) awayDir = Vector2.up;
#pragma warning disable 0618
            rb.velocity = awayDir * (moveSpeed * 0.8f);
#pragma warning restore 0618
        }
        else if (distance > preferredDistance + 2f)
        {
            Vector2 towardDir = (playerPos - curPos).normalized;
#pragma warning disable 0618
            rb.velocity = towardDir * moveSpeed;
#pragma warning restore 0618
        }
        else
        {
#pragma warning disable 0618
            rb.velocity = Vector2.zero;
#pragma warning restore 0618
        }
    }

    private void FindClosestWildEnemy()
    {
        float closestDist = 15f;
        Transform bestTarget = null;
        Vector2 myPos = transform.position;

        EnemyChaser[] chasers = Object.FindObjectsByType<EnemyChaser>();
        foreach (EnemyChaser c in chasers)
        {
            if (c == null || c.isHijacked || c.CurrentHealth <= 0) continue;
            float d = Vector2.Distance(myPos, c.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = c.transform;
            }
        }

        SniperNode[] snipers = Object.FindObjectsByType<SniperNode>();
        foreach (SniperNode s in snipers)
        {
            if (s == null || s == this || s.isHijacked || s.Health <= 0) continue;
            float d = Vector2.Distance(myPos, s.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = s.transform;
            }
        }

        minionTarget = bestTarget;
    }

    public void OverrideControl(float duration)
    {
        if (isDead) return;

        isHijacked = true;
        hijackTimer = duration;

        Color electricCyan = new Color(0f, 1f, 1f, 1f);
        if (spriteRenderer != null)
        {
            spriteRenderer.color = electricCyan;
        }

        if (scalePulseCoroutine != null) StopCoroutine(scalePulseCoroutine);
        scalePulseCoroutine = StartCoroutine(ScalePulseRoutine());

        gameObject.tag = "Player";
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer != -1)
        {
            gameObject.layer = defaultLayer;
        }

        FindClosestWildEnemy();
        Debug.Log($"<color=#00FFFF>[SniperNode] Control overridden for {duration}s. Targeting rogue enemies.</color>");
    }

    private IEnumerator ScalePulseRoutine()
    {
        Vector3 baseScale = originalScale != Vector3.zero ? originalScale : transform.localScale;
        Vector3 peakScale = baseScale * 1.2f;

        float elapsed = 0f;
        while (elapsed < 0.15f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(baseScale, peakScale, elapsed / 0.15f);
            yield return null;
        }

        while (isHijacked && !isDead)
        {
            float pulse = 1.15f + 0.06f * Mathf.Sin(Time.time * 8f);
            transform.localScale = baseScale * pulse;
            yield return null;
        }

        transform.localScale = baseScale;
    }

    public void OverloadDetonation()
    {
        if (isDead) return;
        isDead = true;

        float aoeRadius = 3f;
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, aoeRadius);
        foreach (Collider2D col in colliders)
        {
            if (col == null || col.gameObject == gameObject) continue;

            EnemyChaser rogueChaser = col.GetComponent<EnemyChaser>();
            if (rogueChaser != null && !rogueChaser.isHijacked)
            {
                rogueChaser.TakeDamage(999);
            }

            SniperNode rogueSniper = col.GetComponent<SniperNode>();
            if (rogueSniper != null && !rogueSniper.isHijacked)
            {
                rogueSniper.TakeDamage(999);
            }

            EnemyProjectile proj = col.GetComponent<EnemyProjectile>();
            if (proj != null && !proj.IsAllied)
            {
                Destroy(proj.gameObject);
            }
        }

        CameraFollow2D.TriggerShake(0.3f, 0.45f);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayVictorySound();
            GameManager.Instance.AddScore(250);
            GameManager.Instance.IncrementEnemiesEliminated();
        }

        if (isHijacked)
        {
#pragma warning disable 0618
            var tether = FindObjectOfType<NeuralTether>();
#pragma warning restore 0618
            if (tether != null)
            {
                tether.ReleaseMinion();
            }
        }

        Destroy(gameObject);
    }

    private void Shoot(Vector2 direction)
    {
        if (projectilePrefab == null) return;

        Vector3 spawnPos = transform.position + (Vector3)(direction * 0.6f);
        GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        EnemyProjectile proj = projObj.GetComponent<EnemyProjectile>();
        if (proj != null)
        {
            proj.MoveDirection = direction;
            if (isHijacked)
            {
                proj.IsAllied = true;
            }
        }

        // Radar line-of-fire indicator
        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker != null)
        {
            Color rayCol = isHijacked ? new Color(0.024f, 0.714f, 0.831f, 1f) : new Color(1f, 0.8f, 0f, 1f); // Cyan if hijacked, Amber Gold if wild
            marker.TriggerFiringRay(direction, 12f, rayCol);
        }
    }

    public void TakeDamage(int amount)
    {
        if (isDead) return;

        health -= amount;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayEnemyHitSound();
        }

        if (spriteRenderer != null)
        {
            if (hitFlashCoroutine != null) StopCoroutine(hitFlashCoroutine);
            hitFlashCoroutine = StartCoroutine(HitFlashRoutine());
        }

        if (health <= 0)
        {
            if (isHijacked)
            {
                OverloadDetonation();
            }
            else
            {
                Die();
            }
        }
    }

    private IEnumerator HitFlashRoutine()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
        }

        yield return new WaitForSeconds(hitFlashDuration);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = isHijacked ? new Color(0f, 1f, 1f, 1f) : DefaultEnemyColor;
        }

        hitFlashCoroutine = null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleCollision(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleCollision(collision != null && collision.gameObject != null ? collision.gameObject : null);
    }

    private void HandleCollision(GameObject otherObj)
    {
        if (isDead || otherObj == null || otherObj == gameObject) return;

        if (isHijacked && minionAttackCooldown <= 0f)
        {
            EnemyChaser rogueChaser = otherObj.GetComponent<EnemyChaser>();
            if (rogueChaser != null && !rogueChaser.isHijacked)
            {
                rogueChaser.TakeDamage(2);
                minionAttackCooldown = 0.35f;
                CameraFollow2D.TriggerShake(0.12f, 0.18f);
                return;
            }

            SniperNode rogueSniper = otherObj.GetComponent<SniperNode>();
            if (rogueSniper != null && !rogueSniper.isHijacked)
            {
                rogueSniper.TakeDamage(2);
                minionAttackCooldown = 0.35f;
                CameraFollow2D.TriggerShake(0.12f, 0.18f);
                return;
            }
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddScore(scoreReward);
            GameManager.Instance.IncrementEnemiesEliminated();
            GameManager.Instance.PlayEnemyHitSound();
        }

        SpawnDeathDebris();

        if (isHijacked)
        {
#pragma warning disable 0618
            var tether = FindObjectOfType<NeuralTether>();
#pragma warning restore 0618
            if (tether != null)
            {
                tether.ReleaseMinion();
            }
        }

        Destroy(gameObject);
    }

    private void SpawnDeathDebris()
    {
        GameObject debrisObj = new GameObject("SniperDebris");
        debrisObj.transform.position = transform.position;
        ParticleSystem ps = debrisObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.35f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.984f, 0.443f, 0.522f, 1f), Color.white);
        main.loop = false;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 14) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        ParticleSystemRenderer psr = debrisObj.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            psr.material = new Material(Shader.Find("Sprites/Default"));
            psr.sortingOrder = 15;
        }
        ps.Play();
        Destroy(debrisObj, 0.6f);
    }

    private void OnDestroy()
    {
        if (isHijacked)
        {
#pragma warning disable 0618
            var tether = FindObjectOfType<NeuralTether>();
#pragma warning restore 0618
            if (tether != null)
            {
                tether.ReleaseMinion();
            }
        }
    }
}
