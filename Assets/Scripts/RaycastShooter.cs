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

    private float nextFireTime = 0f;
    private Coroutine beamCoroutine;

    private void Awake()
    {
        if (beamRenderer == null)
        {
            beamRenderer = GetComponent<LineRenderer>();
        }

        // Ensure triggers are queried by 2D Raycasts so projectiles are intercepted cleanly
        Physics2D.queriesHitTriggers = true;

        EnforceEnemyLayerMask();

        if (beamRenderer != null)
        {
            beamRenderer.sortingOrder = 10;
            beamRenderer.enabled = false;
        }
    }

    private void Start()
    {
        EnforceEnemyLayerMask();
    }

    /// <summary>
    /// Strictly locks enemyLayerMask to detect ONLY the "Enemies" layer.
    /// Explicitly removes the "Default" layer or any other fallback layers from raycast evaluations under all conditions.
    /// </summary>
    private void EnforceEnemyLayerMask()
    {
        int mask = LayerMask.GetMask("Enemy");
        if (mask == 0)
        {
            mask = LayerMask.GetMask("Enemy");
        }
        if (mask == 0)
        {
            int idx = LayerMask.NameToLayer("Enemy");
            if (idx == -1) idx = LayerMask.NameToLayer("Enemy");
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
        if (GameManager.Instance != null && !GameManager.Instance.isGameActive)
        {
            return;
        }

        if (Time.timeScale == 0f)
        {
            return;
        }

        if ((Input.GetMouseButton(0) || Input.GetMouseButtonDown(0)) && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            FirePulse();
        }
    }

    private void FirePulse()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        Vector2 playerPos = transform.position;
        Vector2 mousePos = mainCam.ScreenToWorldPoint(Input.mousePosition);

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

        // Offset origin from muzzlePoint or player center so player's own collider never blocks the raycast
        Vector2 rayOrigin = (player != null && player.muzzlePoint != null)
            ? (Vector2)player.muzzlePoint.position
            : playerPos + aimDirection * 0.45f;
        LayerMask targetMask = enemyLayerMask.value != 0 ? enemyLayerMask : enemyLayer;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, aimDirection, weaponRange, targetMask);
        Vector2 hitPoint = rayOrigin + aimDirection * weaponRange;

        if (hit.collider != null && hit.collider.gameObject != gameObject)
        {
            hitPoint = hit.point;

            EnemyChaser chaser = hit.collider.GetComponent<EnemyChaser>() ?? hit.collider.GetComponentInParent<EnemyChaser>();
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
                SniperNode sniper = hit.collider.GetComponent<SniperNode>() ?? hit.collider.GetComponentInParent<SniperNode>();
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
                    EnemyProjectile proj = hit.collider.GetComponent<EnemyProjectile>() ?? hit.collider.GetComponentInParent<EnemyProjectile>();
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

        // Radar line-of-fire indicator
        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker != null)
        {
            ColorUtility.TryParseHtmlString("#06B6D4", out Color cyanCol);
            marker.TriggerFiringRay(aimDirection, weaponRange, cyanCol);
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
}
