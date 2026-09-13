using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private int damage = 1;

    [Header("Durability & Scoring")]
    [SerializeField] private int health = 1;
    public int scoreReward = 50;

    public int Health { get => health; set => health = value; }

    private Rigidbody2D rb;
    private Vector2 moveDirection = Vector2.right;
    private bool isDestroyed = false;
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
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null)
        {
            col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.25f;
        }
        col.isTrigger = false;

        // Ensure projectile is on a shootable layer ("Enemies" or "Enemy")
        int enemyLayerIndex = LayerMask.NameToLayer("Enemy");
        if (enemyLayerIndex == -1) enemyLayerIndex = LayerMask.NameToLayer("Enemy");
        if (enemyLayerIndex != -1 && (gameObject.layer == 0 || gameObject.layer == LayerMask.NameToLayer("Default")))
        {
            gameObject.layer = enemyLayerIndex;
        }

        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker == null)
        {
            marker = gameObject.AddComponent<MiniMapMarker>();
        }
        marker.markerType = MiniMapMarker.MarkerType.Grenade;
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

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision != null && collision.gameObject != null)
        {
            HandleCollision(collision.gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other != null && other.gameObject != null)
        {
            HandleCollision(other.gameObject);
        }
    }

    private void HandleCollision(GameObject other)
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

            Collider2D c = other.GetComponent<Collider2D>();
            if (c != null && !c.isTrigger)
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
        Collider2D otherCol = other.GetComponent<Collider2D>();
        if (otherCol != null && !otherCol.isTrigger)
        {
            Destroy(gameObject);
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDestroyed) return;

        health -= damage;
        if (health <= 0)
        {
            isDestroyed = true;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddScore(scoreReward);
                GameManager.Instance.PlayEnemyHitSound();
            }

            SpawnBurstDebris();
            Destroy(gameObject);
        }
    }

    private void SpawnBurstDebris()
    {
        GameObject debrisObj = new GameObject("ProjectileBurst");
        debrisObj.transform.position = transform.position;
        ParticleSystem ps = debrisObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.25f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 1f), Color.yellow);
        main.loop = false;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 8) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;
        ParticleSystemRenderer psr = debrisObj.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            psr.material = new Material(Shader.Find("Sprites/Default"));
            psr.sortingOrder = 15;
        }
        ps.Play();
        Destroy(debrisObj, 0.5f);
    }
}
