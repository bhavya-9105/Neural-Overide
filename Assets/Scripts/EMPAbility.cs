using TMPro;
using UnityEngine;

public class EMPAbility : MonoBehaviour
{
    [Header("EMP Settings")]
    public float cooldown = 10f;
    public TextMeshProUGUI statusText;

    private float nextEMPTime = 0f;

    public float CooldownRemaining => Mathf.Max(0f, nextEMPTime - Time.time);
    public bool IsReady => Time.time >= nextEMPTime;

    private void Awake()
    {
        if (statusText == null)
        {
            GameObject badgeObj = GameObject.Find("EMPStatus");
            if (badgeObj != null)
            {
                statusText = badgeObj.GetComponent<TextMeshProUGUI>();
            }
        }
    }

    private void Start()
    {
        UpdateStatusUI();
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

        if (Input.GetMouseButtonDown(1) && Time.time >= nextEMPTime)
        {
            TriggerEMP();
        }

        UpdateStatusUI();
    }

    public void TriggerEMP()
    {
        nextEMPTime = Time.time + cooldown;

#pragma warning disable 0618
        // 1. Find all active EnemyChaser and SniperNode instances and call TakeDamage(999) on each
        EnemyChaser[] chasers = Object.FindObjectsByType<EnemyChaser>(FindObjectsSortMode.None);
        foreach (EnemyChaser chaser in chasers)
        {
            if (chaser != null && chaser.gameObject.activeInHierarchy)
            {
                chaser.TakeDamage(999);
            }
        }

        SniperNode[] snipers = Object.FindObjectsByType<SniperNode>(FindObjectsSortMode.None);
        foreach (SniperNode sniper in snipers)
        {
            if (sniper != null && sniper.gameObject.activeInHierarchy)
            {
                sniper.TakeDamage(999);
            }
        }

        // Also destroy any rogue projectiles in flight
        EnemyProjectile[] projs = Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None);
        foreach (EnemyProjectile proj in projs)
        {
            if (proj != null && proj.gameObject.activeInHierarchy)
            {
                Destroy(proj.gameObject);
            }
        }
#pragma warning restore 0618

        // 2. Camera shake
        CameraFollow2D.TriggerShake(0.35f, 0.4f);

        // 3. Play EMP / Victory audio cue
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayVictorySound();
        }

        // 4. Update HUD
        UpdateStatusUI();
    }

    private void UpdateStatusUI()
    {
        if (statusText == null) return;

        if (Time.time >= nextEMPTime)
        {
            statusText.text = "EMP: [READY] (R-CLICK)";
            ColorUtility.TryParseHtmlString("#00FFFF", out Color cyan);
            statusText.color = cyan;
        }
        else
        {
            float remaining = nextEMPTime - Time.time;
            statusText.text = $"EMP COOLDOWN: {remaining:F1}s";
            ColorUtility.TryParseHtmlString("#FFE600", out Color yellow);
            statusText.color = yellow;
        }
    }
}
