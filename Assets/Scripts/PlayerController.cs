using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
#pragma warning disable 0414
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float rotationOffset = 0f;
#pragma warning restore 0414

    [Header("Health Settings")]
    public int maxHealth = 5;
    public int currentHealth = 5;

    [Header("Life Extend Settings (Health < 3)")]
    [Tooltip("Health threshold below which hitting enemies or getting hit can trigger life extend.")]
    public int lifeExtendThreshold = 3;

    [Tooltip("Cooldown between life extends earned by attacking/destroying enemies when under 3 HP.")]
    public float attackLifeExtendCooldown = 0.8f;
    private float lastAttackLifeExtend = -99f;

    [Tooltip("Cooldown for emergency life extend triggered when hit by an enemy while under 3 HP.")]
    public float emergencyLifeExtendCooldown = 6.0f;
    private float lastEmergencyLifeExtend = -99f;

    [Tooltip("Invulnerability time granted when an emergency defensive life extend activates.")]
    public float emergencyInvulnerabilityDuration = 1.2f;
    private bool isInvulnerable = false;

    [Header("Raycast Weapon Settings")]
    [SerializeField] private float shootRange = 25f;
    [SerializeField] private int shootDamage = 1;
    [SerializeField] private float beamDuration = 0.05f;
    [SerializeField] private LayerMask shootLayers = ~0;
    private LineRenderer shootLine;
    private Coroutine beamCoroutine;

    [Header("Animation Hook")]
    [Tooltip("Optional Animator hook (for controllers stored in Assets/Animations/)")]
    [SerializeField] private Animator animator;

    private Rigidbody2D rb;
    private Vector2 moveInput;

    [Header("4-Directional Aim Sprites")]
    public Sprite spriteFront; // Down shooting sprite
    public Sprite spriteBack;  // Up shooting sprite
    public Sprite spriteSide;  // Side profile sprite
    public Transform muzzlePoint; // Child transform for weapon fire origin
    private SpriteRenderer spriteRenderer;

    [Header("Arena Boundary Settings")]
    [SerializeField] private float halfWidth = 18f;
    [SerializeField] private float halfHeight = 10f;

    public bool IsDead { get; private set; } = false;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public bool IsInvulnerable => isInvulnerable;
    public float HalfWidth { get => halfWidth; set => halfWidth = value; }
    public float HalfHeight { get => halfHeight; set => halfHeight = value; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
        transform.rotation = Quaternion.identity;

        if (muzzlePoint == null)
        {
            Transform mChild = transform.Find("MuzzlePoint");
            if (mChild != null)
            {
                muzzlePoint = mChild;
            }
            else
            {
                GameObject mObj = new GameObject("MuzzlePoint");
                mObj.transform.SetParent(transform);
                mObj.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                muzzlePoint = mObj.transform;
            }
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        shootLine = GetComponent<LineRenderer>();
        if (shootLine != null)
        {
            shootLine.enabled = false;
        }

        currentHealth = maxHealth;
        IsDead = false;
        isInvulnerable = false;

        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker == null)
        {
            marker = gameObject.AddComponent<MiniMapMarker>();
        }
        marker.markerType = MiniMapMarker.MarkerType.Player;
    }

    private void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        transform.rotation = Quaternion.identity;

        if (muzzlePoint == null)
        {
            Transform mChild = transform.Find("MuzzlePoint");
            if (mChild != null)
            {
                muzzlePoint = mChild;
            }
        }
    }

    private void Update()
    {
        if (Time.timeScale == 0f)
        {
            return;
        }

        if (IsDead || currentHealth <= 0)
        {
            return;
        }

        // Lock Transform rotation: do NOT rotate player main transform
        transform.rotation = Quaternion.identity;

        // 4-Directional Aim & Sprite Swapping
        UpdateAimAndSprite();

        // 8-directional input via Input.GetAxisRaw("Horizontal") and "Vertical", normalized
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(horizontal, vertical).normalized;

        // Update Animator boolean parameter "IsMoving"
        if (animator != null)
        {
            animator.SetBool("IsMoving", moveInput.sqrMagnitude > 0f);
        }

        // Left-click fires Raycast Beam (handled by RaycastShooter if present)
        if (GetComponent<RaycastShooter>() == null && Input.GetMouseButtonDown(0))
        {
            ShootRaycast();
        }

        // Hard Position Clamping Fallback
        ClampPositionToBounds();
    }

    private void LateUpdate()
    {
        // Enforce locked rotation on main transform across all circumstances
        transform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Calculates angle toward mouse cursor in world space and updates 4-directional sprite state and muzzlePoint.
    /// </summary>
    private void UpdateAimAndSprite()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -mainCam.transform.position.z;
        Vector3 mousePos = mainCam.ScreenToWorldPoint(mouseScreen);
        Vector2 aimDir = ((Vector2)mousePos - (Vector2)transform.position).normalized;
        if (aimDir == Vector2.zero)
        {
            aimDir = Vector2.up;
        }

        ApplyAimDirection(aimDir);
    }

    /// <summary>
    /// Applies 4-directional sprite swapping and positions the muzzlePoint towards aimDir.
    /// </summary>
    public void ApplyAimDirection(Vector2 aimDir)
    {
        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;

        if (spriteRenderer != null)
        {
            if (angle >= 67.5f && angle < 112.5f)
            {
                // UP (67.5° <= angle < 112.5°)
                if (spriteBack != null) spriteRenderer.sprite = spriteBack;
                spriteRenderer.flipX = false;
            }
            else if (angle >= -112.5f && angle < -67.5f)
            {
                // DOWN (-112.5° <= angle < -67.5°)
                if (spriteFront != null) spriteRenderer.sprite = spriteFront;
                spriteRenderer.flipX = false;
            }
            else if (angle >= -67.5f && angle < 67.5f)
            {
                // RIGHT (-67.5° <= angle < 67.5°)
                if (spriteSide != null) spriteRenderer.sprite = spriteSide;
                spriteRenderer.flipX = false;
            }
            else
            {
                // LEFT (angle >= 112.5° || angle < -112.5°)
                if (spriteSide != null) spriteRenderer.sprite = spriteSide;
                spriteRenderer.flipX = true;
            }
        }

        if (muzzlePoint != null)
        {
            muzzlePoint.position = (Vector2)transform.position + aimDir * 0.6f;
            muzzlePoint.up = aimDir;
        }
    }

    private void FixedUpdate()
    {
        if (IsDead || currentHealth <= 0)
        {
            if (rb != null)
            {
#pragma warning disable 0618
                rb.velocity = Vector2.zero;
#pragma warning restore 0618
            }
            return;
        }

        if (rb != null)
        {
#pragma warning disable 0618
            rb.velocity = moveInput * moveSpeed;
#pragma warning restore 0618
        }

        // Hard Position Clamping Fallback: ensure velocity or dash forces never penetrate outside the boundary
        ClampPositionToBounds();
    }

    private void ClampPositionToBounds()
    {
        transform.position = new Vector3(
            Mathf.Clamp(transform.position.x, -halfWidth + 0.5f, halfWidth - 0.5f),
            Mathf.Clamp(transform.position.y, -halfHeight + 0.5f, halfHeight - 0.5f),
            0f
        );

        if (rb != null)
        {
            rb.position = transform.position;
        }
    }

    /// <summary>
    /// Faces the target direction by swapping 4-directional sprites and positioning muzzlePoint.
    /// Does NOT rotate the main transform.
    /// </summary>
    public void LookAt(Vector2 targetWorldPosition)
    {
        transform.rotation = Quaternion.identity;
        Vector2 aimDir = (targetWorldPosition - (Vector2)transform.position).normalized;
        if (aimDir == Vector2.zero)
        {
            aimDir = Vector2.up;
        }
        ApplyAimDirection(aimDir);
    }

    /// <summary>
    /// Grants a Life Extend (+1 or more HP) and triggers celebratory audio/visual feedback.
    /// </summary>
    public bool ExtendLife(int amount = 1, string reason = "LIFE EXTEND! +1 HP")
    {
        if (IsDead || currentHealth <= 0) return false;
        if (currentHealth >= maxHealth) return false;

        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

        // Notify GameManager for HUD announcement and chime
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyLifeExtend(reason, currentHealth, maxHealth);
        }

        // Trigger punch pulse on HealthBar
        HealthBarUI hb = Object.FindAnyObjectByType<HealthBarUI>();
        if (hb != null)
        {
            hb.TriggerLifeExtendPulse();
        }

        return true;
    }

    /// <summary>
    /// Triggered when the player shoots/hits or destroys an enemy.
    /// If health < 3, rewards the player with a clutch Life Extend!
    /// </summary>
    public void OnHitEnemyLifeExtend()
    {
        if (IsDead || currentHealth <= 0) return;

        if (currentHealth < lifeExtendThreshold && Time.time >= lastAttackLifeExtend + attackLifeExtendCooldown)
        {
            lastAttackLifeExtend = Time.time;
            ExtendLife(1, "[+1 HP] CLUTCH STRIKE: INTEGRITY RESTORED");
        }
    }

    /// <summary>
    /// Triggered when the player is hit by an enemy while health < 3.
    /// Activates an emergency defibrillator/barrier that grants a defensive Life Extend!
    /// </summary>
    private Coroutine invulnerabilityCoroutine;

    private bool CheckEmergencyHitLifeExtend()
    {
        if (currentHealth < lifeExtendThreshold && Time.time >= lastEmergencyLifeExtend + emergencyLifeExtendCooldown)
        {
            lastEmergencyLifeExtend = Time.time;
            if (invulnerabilityCoroutine != null)
            {
                StopCoroutine(invulnerabilityCoroutine);
            }
            invulnerabilityCoroutine = StartCoroutine(EmergencyInvulnerabilityRoutine());
            ExtendLife(1, "[+1 HP] EMERGENCY BARRIER: INTEGRITY OVERCHARGE");
            return true;
        }
        return false;
    }

    private IEnumerator EmergencyInvulnerabilityRoutine()
    {
        isInvulnerable = true;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        float elapsed = 0f;

        while (elapsed < emergencyInvulnerabilityDuration)
        {
            if (sr != null)
            {
                sr.enabled = !sr.enabled; // flicker invulnerability effect
            }
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (sr != null)
        {
            sr.enabled = true;
        }
        isInvulnerable = false;
        invulnerabilityCoroutine = null;
    }

    /// <summary>
    /// Inflicts damage on the player. Returns true if damage was actually sustained.
    /// </summary>
    public bool TakeDamage(int damage = 1)
    {
        if (IsDead || currentHealth <= 0 || isInvulnerable)
        {
            return false;
        }

        // If health is below 3 and emergency life extend is ready, absorb and extend life!
        if (currentHealth < lifeExtendThreshold)
        {
            if (CheckEmergencyHitLifeExtend())
            {
                return true;
            }
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);

        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }

        if (currentHealth <= 0)
        {
            IsDead = true;
            moveInput = Vector2.zero;
            if (rb != null)
            {
#pragma warning disable 0618
                rb.velocity = Vector2.zero;
#pragma warning restore 0618
            }

            // Disable player collider so dead body doesn't block or take hits
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            // Find WaveSpawner in scene and call StopSpawning()
            WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>();
            if (spawner != null)
            {
                spawner.StopSpawning();
            }

            // Trigger Game Over in GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerGameOver();
            }

            if (animator != null)
            {
                animator.SetTrigger("Die");
            }
        }

        return true;
    }

    /// <summary>
    /// Fully restores the player to active spawn state for restart or returning to main menu.
    /// </summary>
    public void ResetPlayerState()
    {
        gameObject.SetActive(true);
        currentHealth = maxHealth;
        IsDead = false;
        isInvulnerable = false;

        if (invulnerabilityCoroutine != null)
        {
            StopCoroutine(invulnerabilityCoroutine);
            invulnerabilityCoroutine = null;
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.enabled = true;
        }

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        moveInput = Vector2.zero;

        if (spriteRenderer != null && spriteFront != null)
        {
            spriteRenderer.sprite = spriteFront;
            spriteRenderer.flipX = false;
        }

        if (muzzlePoint != null)
        {
            muzzlePoint.position = (Vector2)transform.position + Vector2.up * 0.6f;
            muzzlePoint.up = Vector2.up;
        }

        if (rb != null)
        {
#pragma warning disable 0618
            rb.velocity = Vector2.zero;
#pragma warning restore 0618
        }

        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    public void ShootRaycast()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        LookAt(mouseWorldPos);
        Vector2 origin = muzzlePoint != null ? (Vector2)muzzlePoint.position : (Vector2)transform.position;
        Vector2 direction = ((Vector2)mouseWorldPos - origin).normalized;

        if (direction == Vector2.zero) direction = transform.right;

        RaycastHit2D hit = Physics2D.Raycast(origin, direction, shootRange, shootLayers);
        Vector3 hitPoint = origin + direction * shootRange;

        if (hit.collider != null)
        {
            hitPoint = hit.point;

            EnemyChaser chaser = hit.collider.GetComponent<EnemyChaser>() ?? hit.collider.GetComponentInParent<EnemyChaser>();
            if (chaser != null)
            {
                chaser.TakeDamage(shootDamage);
                OnHitEnemyLifeExtend();
            }

            SniperNode sniper = hit.collider.GetComponent<SniperNode>() ?? hit.collider.GetComponentInParent<SniperNode>();
            if (sniper != null)
            {
                sniper.TakeDamage(shootDamage);
                OnHitEnemyLifeExtend();
            }

            EnemyProjectile proj = hit.collider.GetComponent<EnemyProjectile>() ?? hit.collider.GetComponentInParent<EnemyProjectile>();
            if (proj != null && !proj.IsAllied)
            {
                proj.TakeDamage(shootDamage);
            }
        }

        if (shootLine != null)
        {
            if (beamCoroutine != null) StopCoroutine(beamCoroutine);
            beamCoroutine = StartCoroutine(ShowBeam(origin, hitPoint));
        }

        // Radar line-of-fire indicator
        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker != null)
        {
            ColorUtility.TryParseHtmlString("#06B6D4", out Color cyanCol);
            marker.TriggerFiringRay(direction, shootRange, cyanCol);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayShootSound();
        }

        CameraFollow2D.TriggerShake(0.06f, 0.08f);
    }

    private IEnumerator ShowBeam(Vector3 start, Vector3 end)
    {
        shootLine.enabled = true;
        shootLine.positionCount = 2;
        shootLine.SetPosition(0, start);
        shootLine.SetPosition(1, end);

        yield return new WaitForSeconds(beamDuration);

        shootLine.enabled = false;
        beamCoroutine = null;
    }
}
