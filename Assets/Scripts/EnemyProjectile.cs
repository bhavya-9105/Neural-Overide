using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private int damage = 1;

    private Rigidbody2D rb;
    private Vector2 moveDirection = Vector2.right;
    public bool IsAllied { get; set; } = false;

    public Vector2 MoveDirection
    {
        get => moveDirection;
        set
        {
            moveDirection = value.normalized;
            if (rb != null)
            {
#pragma warning disable 0618
                rb.velocity = moveDirection * speed;
#pragma warning restore 0618
            }
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
    }

    private void Start()
    {
        if (moveDirection != Vector2.zero && rb != null)
        {
#pragma warning disable 0618
            rb.velocity = moveDirection * speed;
#pragma warning restore 0618
        }

        if (IsAllied)
        {
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0f, 1f, 1f, 1f); // Electric Cyan
            }
        }

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;

        // 1. If projectile is fired by an allied/hijacked sniper:
        if (IsAllied)
        {
            // Ignore player and other allied minions
            if (other.CompareTag("Player") || other.name == "Player" || other.name == "TetherLine") return;

            EnemyChaser chaser = other.GetComponent<EnemyChaser>();
            if (chaser != null && !chaser.isHijacked)
            {
                chaser.TakeDamage(damage);
                Destroy(gameObject);
                return;
            }

            SniperNode sniper = other.GetComponent<SniperNode>();
            if (sniper != null && !sniper.isHijacked)
            {
                sniper.TakeDamage(damage);
                Destroy(gameObject);
                return;
            }

            if (!other.isTrigger)
            {
                Destroy(gameObject);
            }
            return;
        }

        // 2. Wild enemy projectile:
        // Ignore other enemies and other projectiles
        if (other.CompareTag("Enemy") || other.GetComponent<EnemyProjectile>() != null || other.GetComponent<SniperNode>() != null || other.GetComponent<EnemyChaser>() != null)
        {
            return;
        }

        if (other.CompareTag("Player") || other.name == "Player")
        {
            PlayerController player = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            bool damaged = false;
            if (player != null)
            {
                damaged = player.TakeDamage(damage);
            }

            if (damaged && GameManager.Instance != null)
            {
                GameManager.Instance.PlayPlayerHurtSound();
            }

            CameraFollow2D.TriggerShake(0.15f, 0.25f);
            Destroy(gameObject);
            return;
        }

        // If hits arena walls/obstacles
        if (!other.isTrigger)
        {
            Destroy(gameObject);
        }
    }
}
