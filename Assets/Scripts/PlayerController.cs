#pragma warning disable 0618
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

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
    private Camera cachedMainCam;

    [Header("Visual Root & Body Sprites")]
    public Transform visualRoot;
    public SpriteRenderer bodySpriteRenderer;
    public Sprite spriteFront; // Down shooting sprite
    public Sprite spriteBack;  // Up shooting sprite
    public Sprite spriteSide;  // Side profile sprite

    [Header("360° Weapon Aiming")]
    public Transform weaponPivot;
    public Transform weaponSprite;
    public SpriteRenderer weaponSpriteRenderer;
    public Transform muzzlePoint; // Child transform for weapon fire origin
    public Sprite weaponSpriteAsset;

    [Header("Procedural Motion Juice")]
    public Vector2 recoilOffset = Vector2.zero;
    [SerializeField] private float recoilRecoverySpeed = 12f;
    [SerializeField] private float scaleRecoverySpeed = 12f;

    // Backward compatibility property for existing references
    public SpriteRenderer spriteRenderer => bodySpriteRenderer;

    [Header("Arena Boundary Settings")]
    [SerializeField] private float halfWidth = 18f;
    [SerializeField] private float halfHeight = 10f;

    public float HalfWidth { get => halfWidth; set => halfWidth = value; }
    public float HalfHeight { get => halfHeight; set => halfHeight = value; }
    public Rigidbody2D Rb => rb;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public bool IsInvulnerable => isInvulnerable;
    public bool IsDead { get; set; } = false;

    private Vector2 RbVelocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb != null ? rb.linearVelocity : Vector2.zero;
#else
            return rb != null ? rb.velocity : Vector2.zero;
#endif
        }
        set
        {
            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = value;
#else
                rb.velocity = value;
#endif
            }
        }
    }

    private void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        transform.rotation = Quaternion.identity;

        EnsureVisualHierarchy();

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

        int enemiesMask = LayerMask.GetMask("Enemies");
        shootLayers = enemiesMask != 0 ? enemiesMask : (1 << 11);

        if (cachedMainCam == null) cachedMainCam = Camera.main;
    }

    private void Start()
    {
        transform.rotation = Quaternion.identity;
        EnsureVisualHierarchy();

        if (bodySpriteRenderer != null && bodySpriteRenderer.sprite == null && spriteFront != null)
        {
            bodySpriteRenderer.sprite = spriteFront;
        }
        if (weaponSpriteRenderer != null && weaponSpriteRenderer.sprite == null && weaponSpriteAsset != null)
        {
            weaponSpriteRenderer.sprite = weaponSpriteAsset;
        }
    }

    /// <summary>
    /// Self-healing initialization for VisualRoot, BodySprite, WeaponPivot, WeaponSprite, and MuzzlePoint.
    /// </summary>
    public void EnsureVisualHierarchy()
    {
        // 1. VisualRoot (holds visual components, bob, tilt, and recoil offsets without moving physical collider)
        if (visualRoot == null)
        {
            Transform vChild = transform.Find("VisualRoot");
            if (vChild != null)
            {
                visualRoot = vChild;
            }
            else
            {
                GameObject vObj = new GameObject("VisualRoot");
                vObj.transform.SetParent(transform, false);
                vObj.transform.localPosition = Vector3.zero;
                vObj.transform.localRotation = Quaternion.identity;
                vObj.transform.localScale = Vector3.one;
                visualRoot = vObj.transform;
            }
        }

        // 2. Body SpriteRenderer
        if (bodySpriteRenderer == null)
        {
            if (visualRoot != null)
            {
                Transform bodyChild = visualRoot.Find("Body");
                if (bodyChild != null)
                {
                    bodySpriteRenderer = bodyChild.GetComponent<SpriteRenderer>();
                }
                else
                {
                    bodySpriteRenderer = visualRoot.GetComponent<SpriteRenderer>();
                }
            }
            if (bodySpriteRenderer == null)
            {
                bodySpriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        // 3. WeaponPivot (rotates 360° towards mouse cursor)
        Transform weaponParent = visualRoot != null ? visualRoot : transform;
        if (weaponPivot == null)
        {
            Transform wpChild = weaponParent.Find("WeaponPivot") ?? transform.Find("WeaponPivot");
            if (wpChild != null)
            {
                weaponPivot = wpChild;
                if (weaponPivot.parent != weaponParent)
                {
                    weaponPivot.SetParent(weaponParent, false);
                }
            }
            else
            {
                GameObject wpObj = new GameObject("WeaponPivot");
                wpObj.transform.SetParent(weaponParent, false);
                wpObj.transform.localPosition = Vector3.zero;
                wpObj.transform.localRotation = Quaternion.identity;
                weaponPivot = wpObj.transform;
            }
        }

        // 4. WeaponSprite (child of WeaponPivot offset forward by x: 0.45, y: 0)
        if (weaponPivot != null)
        {
            if (weaponSprite == null)
            {
                weaponSprite = weaponPivot.Find("WeaponSprite");
            }
            if (weaponSprite == null)
            {
                GameObject wsObj = new GameObject("WeaponSprite");
                wsObj.transform.SetParent(weaponPivot, false);
                wsObj.transform.localPosition = new Vector3(0.45f, 0f, 0f);
                wsObj.transform.localRotation = Quaternion.identity;
                weaponSprite = wsObj.transform;
            }
            else
            {
                weaponSprite.localPosition = new Vector3(0.45f, 0f, 0f);
            }

            if (weaponSpriteRenderer == null && weaponSprite != null)
            {
                weaponSpriteRenderer = weaponSprite.GetComponent<SpriteRenderer>() ?? weaponSprite.gameObject.AddComponent<SpriteRenderer>();
            }
            if (weaponSpriteRenderer != null)
            {
                weaponSpriteRenderer.sortingOrder = 5;
                if (weaponSpriteRenderer.sprite == null && weaponSpriteAsset != null)
                {
                    weaponSpriteRenderer.sprite = weaponSpriteAsset;
                }
            }

            // 5. MuzzlePoint (child of WeaponPivot offset forward at x: 0.8, y: 0)
            if (muzzlePoint == null)
            {
                muzzlePoint = weaponPivot.Find("MuzzlePoint");
            }
            if (muzzlePoint == null)
            {
                Transform oldMp = transform.Find("MuzzlePoint");
                if (oldMp != null)
                {
                    oldMp.SetParent(weaponPivot, false);
                    oldMp.localPosition = new Vector3(0.8f, 0f, 0f);
                    oldMp.localRotation = Quaternion.identity;
                    muzzlePoint = oldMp;
                }
                else
                {
                    GameObject mpObj = new GameObject("MuzzlePoint");
                    mpObj.transform.SetParent(weaponPivot, false);
                    mpObj.transform.localPosition = new Vector3(0.8f, 0f, 0f);
                    mpObj.transform.localRotation = Quaternion.identity;
                    muzzlePoint = mpObj.transform;
                }
            }
            else
            {
                if (muzzlePoint.parent != weaponPivot)
                {
                    muzzlePoint.SetParent(weaponPivot, false);
                }
                muzzlePoint.localPosition = new Vector3(0.8f, 0f, 0f);
            }
        }
    }

    private void Update()
    {
        if (Time.timeScale == 0f)
        {
            return;
        }

        if (SettingsManager.Instance != null && SettingsManager.Instance.IsSettingsOpen)
        {
            return;
        }

        if (IsDead || currentHealth <= 0)
        {
            return;
        }

        // Lock Transform rotation: do NOT rotate player main transform
        transform.rotation = Quaternion.identity;

        // 360-Degree Weapon Aiming & 4-Quadrant Body Sprite
        UpdateAimAndSprite();

        // Procedural Motion Juice: smoothly recover scale back to Vector3.one
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, scaleRecoverySpeed * Time.deltaTime);

        // Smoothly decay recoilOffset back to Vector2.zero
        recoilOffset = Vector2.Lerp(recoilOffset, Vector2.zero, recoilRecoverySpeed * Time.deltaTime);

        // Dynamic Walking Bob & Lean Tilt
        ApplyMovementJuice();

        // Configurable 8-directional input via SettingsManager movement mode (WASD, Arrows, or Both)
        float horizontal = 0f;
        float vertical = 0f;
        SettingsManager.MovementMode mMode = SettingsManager.Instance != null ? SettingsManager.Instance.CurrentMovementMode : SettingsManager.MovementMode.Both;

        if (mMode == SettingsManager.MovementMode.Both || mMode == SettingsManager.MovementMode.WASDOnly)
        {
            if (Input.GetKey(KeyCode.W)) vertical += 1f;
            if (Input.GetKey(KeyCode.S)) vertical -= 1f;
            if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D)) horizontal += 1f;
        }

        if (mMode == SettingsManager.MovementMode.Both || mMode == SettingsManager.MovementMode.ArrowKeysOnly)
        {
            if (Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
        }

        horizontal = Mathf.Clamp(horizontal, -1f, 1f);
        vertical = Mathf.Clamp(vertical, -1f, 1f);
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
    }

    private void LateUpdate()
    {
        // Enforce locked rotation on main transform across all circumstances
        transform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Computes aim direction towards mouse cursor in world space and applies 360° weapon rotation and body sprite.
    /// </summary>
    private void UpdateAimAndSprite()
    {
        if (cachedMainCam == null) cachedMainCam = Camera.main;
        Camera mainCam = cachedMainCam;
        if (mainCam == null) return;

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -mainCam.transform.position.z;
        Vector3 mousePos = mainCam.ScreenToWorldPoint(mouseScreen);
        Vector2 aimDir = ((Vector2)mousePos - (Vector2)transform.position).normalized;
        if (aimDir == Vector2.zero)
        {
            aimDir = Vector2.right;
        }

        ApplyAimDirection(aimDir);
    }

    /// <summary>
    /// Rotates WeaponPivot 360° to aimDir, flips WeaponSprite vertically when aiming left,
    /// and selects body sprite & flipX based on 4-quadrant aimDir.
    /// </summary>
    public void ApplyAimDirection(Vector2 aimDir)
    {
        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;

        // Rotate WeaponPivot: weaponPivot.rotation = Quaternion.Euler(0, 0, angle);
        if (weaponPivot != null)
        {
            weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // Flip WeaponSprite vertically when aiming left: weaponSpriteRenderer.flipY = (angle > 90f || angle < -90f);
        if (weaponSpriteRenderer != null)
        {
            weaponSpriteRenderer.flipY = (angle > 90f || angle < -90f);
        }

        // Update Body/Torso Sprite:
        if (bodySpriteRenderer != null)
        {
            bool isHorizontal = Mathf.Abs(aimDir.x) > Mathf.Abs(aimDir.y);
            bodySpriteRenderer.flipX = (aimDir.x < 0 && isHorizontal);

            if (isHorizontal)
            {
                if (spriteSide != null) bodySpriteRenderer.sprite = spriteSide;
            }
            else
            {
                if (aimDir.y > 0)
                {
                    if (spriteBack != null) bodySpriteRenderer.sprite = spriteBack;
                }
                else
                {
                    if (spriteFront != null) bodySpriteRenderer.sprite = spriteFront;
                }
            }
        }
    }

    /// <summary>
    /// Applies dynamic walking sine-wave bob and velocity lean tilt to visualRoot without modifying physics collider.
    /// </summary>
    private void ApplyMovementJuice()
    {
        float bob = 0f;
        float tiltAngle = 0f;

        Vector2 currentVel = RbVelocity;
        if (currentVel.sqrMagnitude > 0.01f || moveInput.sqrMagnitude > 0.01f)
        {
            bob = Mathf.Sin(Time.time * 12f) * 0.04f;
            tiltAngle = -moveInput.x * 4.0f;
        }

        if (visualRoot != null)
        {
            visualRoot.localPosition = new Vector3(0f, bob, 0f) + (Vector3)recoilOffset;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, tiltAngle);
        }
        else
        {
            if (bodySpriteRenderer != null)
            {
                bodySpriteRenderer.transform.localPosition = new Vector3(0f, bob, 0f) + (Vector3)recoilOffset;
                bodySpriteRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, tiltAngle);
            }
            if (weaponPivot != null)
            {
                weaponPivot.localPosition = new Vector3(0f, bob, 0f) + (Vector3)recoilOffset;
            }
        }
    }

    /// <summary>
    /// Triggers procedural recoil impulse: instant visual recoil offset and squash scale.
    /// </summary>
    public void TriggerRecoil(Vector2 shootDirection)
    {
        recoilOffset = -shootDirection.normalized * 0.15f;
        transform.localScale = new Vector3(0.85f, 1.15f, 1.0f);
    }

    /// <summary>
    /// Toggles body and weapon sprite renderers simultaneously.
    /// </summary>
    public void SetVisualsEnabled(bool isEnabled)
    {
        if (bodySpriteRenderer != null) bodySpriteRenderer.enabled = isEnabled;
        if (weaponSpriteRenderer != null) weaponSpriteRenderer.enabled = isEnabled;
    }

    private void FixedUpdate()
    {
        if (IsDead || currentHealth <= 0)
        {
            RbVelocity = Vector2.zero;
            return;
        }

        if (rb == null) return;

        // Physics updates run strictly inside FixedUpdate() using rigidbody.velocity
        Vector2 targetVel = moveInput * moveSpeed;

        Vector2 pos = rb.position;
        if ((pos.x <= -halfWidth + 0.5f && targetVel.x < 0) || (pos.x >= halfWidth - 0.5f && targetVel.x > 0))
        {
            targetVel.x = 0;
        }
        if ((pos.y <= -halfHeight + 0.5f && targetVel.y < 0) || (pos.y >= halfHeight - 0.5f && targetVel.y > 0))
        {
            targetVel.y = 0;
        }

        RbVelocity = targetVel;
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
            RbVelocity = Vector2.zero;

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

        RbVelocity = Vector2.zero;

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

        int targetMask = LayerMask.GetMask("Enemies");
        if (targetMask == 0) targetMask = shootLayers != 0 ? shootLayers.value : (1 << 11);

        RaycastHit2D hit = Physics2D.Raycast(origin, direction, shootRange, targetMask);
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

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
