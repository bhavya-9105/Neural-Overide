using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class EnemyChaser : MonoBehaviour, IHijackable
{
    [Header("Stats")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private int currentHealth = 3;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private int damageToPlayer = 1;
    [SerializeField] private float deathDelay = 0.5f;

    [Header("Visual & Feedback")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Optional Animator hook (for states like Walk, Hit, Die)")]
    [SerializeField] private Animator animator;
    [SerializeField] private float hitFlashDuration = 0.05f;

    [Header("Puppet Protocol / Neural Hijack")]
    public bool isHijacked = false;
    private float hijackTimer = 0f;
    private Transform minionTarget;
    private Coroutine scalePulseCoroutine;
    private float minionAttackCooldown = 0f;
    private Vector3 originalScale;

    private Rigidbody2D rb;
    private Transform player;
    private bool isDead = false;
    private Coroutine hitFlashCoroutine;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsHijacked => isHijacked;
    public float HijackRemainingTime => hijackTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        currentHealth = maxHealth;
        originalScale = transform.localScale;
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
        }
    }

    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player") ?? GameObject.Find("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
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
            }
        }
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        // If hijacked: seek and destroy rogue enemy nodes
        if (isHijacked)
        {
            UpdateHijackedMovement();
            return;
        }

        // Standard wild enemy behavior: chase player
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            FindPlayer();
            if (player == null) return;
        }

        if (rb != null)
        {
            Vector2 currentPosition = rb.position;
            Vector2 targetPosition = player.position;
            Vector2 newPosition = Vector2.MoveTowards(currentPosition, targetPosition, moveSpeed * Time.fixedDeltaTime);
            rb.MovePosition(newPosition);
        }
    }

    private void UpdateHijackedMovement()
    {
        if (rb == null) return;

        // Acquire or maintain closest wild enemy target
        if (minionTarget == null || !minionTarget.gameObject.activeInHierarchy)
        {
            FindClosestWildEnemy();
        }

        if (minionTarget != null)
        {
            Vector2 currentPosition = rb.position;
            Vector2 targetPosition = minionTarget.position;
            // Move aggressively at 1.4x speed towards rogue target
            Vector2 newPosition = Vector2.MoveTowards(currentPosition, targetPosition, moveSpeed * 1.4f * Time.fixedDeltaTime);
            rb.MovePosition(newPosition);
        }
        else
        {
            // If no wild enemies remain, search again periodically
            FindClosestWildEnemy();
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
            if (c == null || c == this || c.isHijacked || c.isDead || c.CurrentHealth <= 0) continue;
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
            if (s == null || s.isHijacked || s.Health <= 0) continue;
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

        // Change color to bright Electric Cyan #00FFFF
        Color electricCyan = new Color(0f, 1f, 1f, 1f);
        if (spriteRenderer != null)
        {
            spriteRenderer.color = electricCyan;
        }

        // 1.2x scale pulse to visibly signify allied state
        if (scalePulseCoroutine != null) StopCoroutine(scalePulseCoroutine);
        scalePulseCoroutine = StartCoroutine(ScalePulseRoutine());

        // Swap collision/targeting tags & layers
        gameObject.tag = "Player"; // Wild enemies treat this minion as a target
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer != -1)
        {
            gameObject.layer = defaultLayer;
        }

        FindClosestWildEnemy();
        Debug.Log($"<color=#00FFFF>[EnemyChaser] Control overridden for {duration}s. Seeking rogue targets.</color>");
    }

    private IEnumerator ScalePulseRoutine()
    {
        Vector3 baseScale = originalScale != Vector3.zero ? originalScale : transform.localScale;
        Vector3 peakScale = baseScale * 1.2f;

        // Punch up to 1.2x
        float elapsed = 0f;
        while (elapsed < 0.15f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(baseScale, peakScale, elapsed / 0.15f);
            yield return null;
        }

        // Gentle breathing pulse while slaved
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

        // Explode in an AOE radius of 3 units, dealing lethal damage to nearby rogue nodes
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

        // Screen shake & sound
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

    public void TakeDamage(int amount = 1)
    {
        if (isDead) return;

        currentHealth -= amount;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayEnemyHitSound();
        }

        if (spriteRenderer != null)
        {
            if (hitFlashCoroutine != null) StopCoroutine(hitFlashCoroutine);
            hitFlashCoroutine = StartCoroutine(HitFlashRoutine());
        }

        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }

        if (currentHealth <= 0)
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

        // 1. If hijacked: attack wild enemies upon contact (TakeDamage(2))
        if (isHijacked)
        {
            if (minionAttackCooldown <= 0f)
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
            return; // Hijacked minion NEVER damages player!
        }

        // 2. Wild enemy: attack player upon contact
        if (otherObj.CompareTag("Player") || otherObj.name == "Player")
        {
            PlayerController playerController = otherObj.GetComponent<PlayerController>() 
                                             ?? otherObj.GetComponentInParent<PlayerController>();

            if (playerController != null)
            {
                bool damageApplied = playerController.TakeDamage(damageToPlayer);
                if (damageApplied && GameManager.Instance != null)
                {
                    GameManager.Instance.PlayPlayerHurtSound();
                }

                CameraFollow2D.TriggerShake(0.2f, 0.35f);
                Destroy(gameObject);
            }
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (rb != null)
        {
#pragma warning disable 0618
            rb.velocity = Vector2.zero;
#pragma warning restore 0618
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddScore(100);
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

        if (animator != null)
        {
            animator.SetTrigger("Die");
            Destroy(gameObject, deathDelay);
        }
        else
        {
            Destroy(gameObject);
        }
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
