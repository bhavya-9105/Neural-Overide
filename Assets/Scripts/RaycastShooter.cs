using System.Collections;
using UnityEngine;

public class RaycastShooter : MonoBehaviour
{
    [Header("Weapon Stats")]
    public float fireRate = 0.2f;
    public float weaponRange = 15f;
    public int damage = 1;
    public LayerMask enemyLayerMask;
    public LayerMask enemyLayer;
    public LineRenderer beamRenderer;

    [Header("Aiming & Visual Crosshair")]
    public LineRenderer aimLaserRenderer;
    public RectTransform customCrosshairUI;

    private float nextFireTime = 0f;
    private Coroutine beamCoroutine;
    private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[16];
    private Camera cachedMainCam;

    private Camera MainCam
    {
        get
        {
            if (cachedMainCam == null) cachedMainCam = Camera.main;
            return cachedMainCam;
        }
    }

    private void Awake()
    {
        if (beamRenderer == null)
        {
            beamRenderer = GetComponent<LineRenderer>();
        }

        if (aimLaserRenderer == null)
        {
            Transform aimObj = transform.Find("AimLaser");
            if (aimObj != null)
            {
                aimLaserRenderer = aimObj.GetComponent<LineRenderer>();
            }
        }

        // Ensure triggers are queried by 2D Raycasts so projectiles are intercepted cleanly
        Physics2D.queriesHitTriggers = true;

        EnforceEnemyLayerMask();

        if (beamRenderer != null)
        {
            beamRenderer.sortingOrder = 10;
            beamRenderer.enabled = false;
        }

        if (aimLaserRenderer != null)
        {
            aimLaserRenderer.enabled = false;
        }

        if (customCrosshairUI != null)
        {
            customCrosshairUI.gameObject.SetActive(false);
        }

        Cursor.visible = true;
    }

    private void Start()
    {
        EnforceEnemyLayerMask();
        if (aimLaserRenderer != null)
        {
            aimLaserRenderer.enabled = false;
        }
        if (customCrosshairUI != null)
        {
            customCrosshairUI.gameObject.SetActive(false);
        }
        Cursor.visible = true;
    }

    public void SetupAimLaser()
    {
        if (aimLaserRenderer == null) return;
        aimLaserRenderer.enabled = false;
    }

    /// <summary>
    /// Strictly locks enemyLayerMask to detect ONLY the "Enemies" layer.
    /// Explicitly removes the "Default" layer or any other fallback layers from raycast evaluations under all conditions.
    /// </summary>
    private void EnforceEnemyLayerMask()
    {
        int mask = LayerMask.GetMask("Enemies");
        if (mask == 0)
        {
            int idx = LayerMask.NameToLayer("Enemies");
            if (idx != -1) mask = (1 << idx);
        }

        // Explicitly strip Default layer or any non-enemy layers under all conditions
        int defaultMask = LayerMask.GetMask("Default");
        if (defaultMask != 0)
        {
            mask &= ~defaultMask;
        }
        mask &= ~1; // bit 0 is Default in Unity

        enemyLayerMask = mask;
        enemyLayer = mask;
    }

    private void Update()
    {
        // Enforce standard system hardware cursor and keep custom overlays deactivated
        Cursor.visible = true;

        if (customCrosshairUI != null && customCrosshairUI.gameObject.activeSelf)
        {
            customCrosshairUI.gameObject.SetActive(false);
        }

        if (aimLaserRenderer != null && aimLaserRenderer.enabled)
        {
            aimLaserRenderer.enabled = false;
        }

        bool isGameplay = (GameManager.Instance == null || (GameManager.Instance.isGameActive && !GameManager.Instance.isPaused)) && Time.timeScale > 0f;
        if (!isGameplay) return;

        if (SettingsManager.Instance != null && SettingsManager.Instance.IsSettingsOpen)
        {
            return;
        }

        int primaryBtn = SettingsManager.Instance != null ? SettingsManager.Instance.PrimaryMouseButton : 0;
        int secondaryBtn = SettingsManager.Instance != null ? SettingsManager.Instance.SecondaryMouseButton : 1;
        int specialBtn = SettingsManager.Instance != null ? SettingsManager.Instance.SpecialMouseButton : 2;

        if ((Input.GetMouseButton(primaryBtn) || Input.GetMouseButtonDown(primaryBtn)) && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            FirePulse();
        }

        if (Input.GetMouseButtonDown(secondaryBtn))
        {
            NeuralTether tether = GetComponent<NeuralTether>();
            if (tether != null)
            {
                tether.TryInitiateHijack();
            }
            EMPAbility emp = GetComponent<EMPAbility>();
            if (emp != null && emp.IsReady)
            {
                emp.TriggerEMP();
            }
        }

        if (Input.GetMouseButtonDown(specialBtn))
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.ToggleTacticalZoom();
            }
        }
    }

    private void FirePulse()
    {
        Camera cam = MainCam;
        if (cam == null) return;

        Vector2 playerPos = transform.position;
        Vector2 mousePos = cam.ScreenToWorldPoint(Input.mousePosition);

        PlayerController player = GetComponent<PlayerController>();
        if (player != null)
        {
            player.LookAt(mousePos);
        }

        Vector2 aimDirection = (mousePos - playerPos).normalized;
        if (aimDirection == Vector2.zero)
        {
            aimDirection = transform.up;
        }

        if (player != null)
        {
            player.TriggerRecoil(aimDirection);
        }

        // Offset origin from muzzlePoint or player center so player's own collider never blocks the raycast
        Vector2 rayOrigin = (player != null && player.muzzlePoint != null)
            ? (Vector2)player.muzzlePoint.position
            : playerPos + aimDirection * 0.45f;
        int targetMask = LayerMask.GetMask("Enemies");
        if (targetMask == 0) targetMask = enemyLayerMask.value != 0 ? enemyLayerMask.value : (1 << 11);

        int hitCount = Physics2D.RaycastNonAlloc(rayOrigin, aimDirection, hitBuffer, weaponRange, targetMask);
        RaycastHit2D closestHit = default;
        float closestDist = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D h = hitBuffer[i];
            if (h.collider != null && h.collider.gameObject != gameObject)
            {
                if (h.distance < closestDist)
                {
                    closestDist = h.distance;
                    closestHit = h;
                }
            }
        }

        Vector2 hitPoint = rayOrigin + aimDirection * weaponRange;

        if (closestHit.collider != null)
        {
            hitPoint = closestHit.point;
            Collider2D hitCol = closestHit.collider;

            EnemyChaser chaser = hitCol.GetComponent<EnemyChaser>() ?? hitCol.GetComponentInParent<EnemyChaser>();
            if (chaser != null)
            {
                chaser.TakeDamage(damage);
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.PlaySound(GameManager.Instance.enemyHitSFX);
                }
            }
            else
            {
                SniperNode sniper = hitCol.GetComponent<SniperNode>() ?? hitCol.GetComponentInParent<SniperNode>();
                if (sniper != null)
                {
                    sniper.TakeDamage(damage);
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.PlaySound(GameManager.Instance.enemyHitSFX);
                    }
                }
                else
                {
                    EnemyProjectile proj = hitCol.GetComponent<EnemyProjectile>() ?? hitCol.GetComponentInParent<EnemyProjectile>();
                    if (proj != null && !proj.IsAllied)
                    {
                        proj.TakeDamage(damage);
                    }
                }
            }

            PlayerController playerCtrl = GetComponent<PlayerController>();
            if (playerCtrl != null)
            {
                playerCtrl.OnHitEnemyLifeExtend();
            }
        }

        // Render laser beam
        if (beamRenderer != null)
        {
            if (beamCoroutine != null) StopCoroutine(beamCoroutine);
            beamCoroutine = StartCoroutine(ShowBeamRoutine(rayOrigin, hitPoint));
        }



        // Camera shake and sound
        CameraFollow2D.TriggerShake(0.06f, 0.12f);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlaySound(GameManager.Instance.shotSFX);
        }
    }

    private IEnumerator ShowBeamRoutine(Vector3 start, Vector3 end)
    {
        beamRenderer.enabled = true;
        beamRenderer.useWorldSpace = true;
        beamRenderer.positionCount = 2;
        beamRenderer.SetPosition(0, start);
        beamRenderer.SetPosition(1, end);

        ColorUtility.TryParseHtmlString("#FFE600", out Color amberYellow);

        float duration = 0.05f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(1f - (elapsed / duration));
            float currentWidth = 0.08f * t;
            beamRenderer.startWidth = currentWidth;
            beamRenderer.endWidth = currentWidth;

            Color fadeCol = new Color(amberYellow.r, amberYellow.g, amberYellow.b, t);
            beamRenderer.startColor = fadeCol;
            beamRenderer.endColor = fadeCol;
            yield return null;
        }

        beamRenderer.enabled = false;
        beamCoroutine = null;
    }

    private void OnDisable()
    {
        Cursor.visible = true;
        if (customCrosshairUI != null && customCrosshairUI.gameObject.activeSelf)
        {
            customCrosshairUI.gameObject.SetActive(false);
        }
        if (aimLaserRenderer != null && aimLaserRenderer.enabled)
        {
            aimLaserRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        Cursor.visible = true;
    }
}
