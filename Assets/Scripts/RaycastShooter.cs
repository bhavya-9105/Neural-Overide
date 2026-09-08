using System.Collections;
using UnityEngine;

public class RaycastShooter : MonoBehaviour
{
    [Header("Weapon Stats")]
    public float fireRate = 0.2f;
    public float weaponRange = 15f;
    public int damage = 1;
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

        int enemyLayerIndex = LayerMask.NameToLayer("Enemy");
        int enemiesLayerIndex = LayerMask.NameToLayer("Enemies");
        int mask = enemyLayer.value;
        if (enemyLayerIndex != -1) mask |= (1 << enemyLayerIndex);
        if (enemiesLayerIndex != -1) mask |= (1 << enemiesLayerIndex);
        enemyLayer = mask != 0 ? mask : ~0;

        if (beamRenderer != null)
        {
            beamRenderer.sortingOrder = 10;
            beamRenderer.enabled = false;
        }
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

        // Offset origin slightly so player's own collider never blocks the raycast
        Vector2 rayOrigin = playerPos + aimDirection * 0.45f;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, aimDirection, weaponRange, enemyLayer);
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
            beamCoroutine = StartCoroutine(ShowBeamRoutine(playerPos, hitPoint));
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
