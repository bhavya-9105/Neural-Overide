using System.Collections.Generic;
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

        // 1. Eliminate all active enemy entities tracked in WaveSpawner without scene scans
        List<GameObject> enemies = new List<GameObject>(WaveSpawner.activeEnemies);
        foreach (GameObject enemyObj in enemies)
        {
            if (enemyObj == null) continue;

            EnemyChaser chaser = enemyObj.GetComponent<EnemyChaser>();
            if (chaser != null)
            {
                chaser.TakeDamage(999);
                continue;
            }

            SniperNode sniper = enemyObj.GetComponent<SniperNode>();
            if (sniper != null)
            {
                sniper.TakeDamage(999);
                continue;
            }

            EnemyProjectile proj = enemyObj.GetComponent<EnemyProjectile>();
            if (proj != null)
            {
                Destroy(proj.gameObject);
            }
        }

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
