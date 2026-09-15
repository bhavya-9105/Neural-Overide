using TMPro;
using UnityEngine;

/// <summary>
/// Common interface for enemy nodes capable of being hijacked via Puppet Protocol.
/// </summary>
public interface IHijackable
{
    bool IsHijacked { get; }
    float HijackRemainingTime { get; }
    void OverrideControl(float duration);
    void OverloadDetonation();
    Transform transform { get; }
    GameObject gameObject { get; }
}

/// <summary>
/// Neural Tether / Puppet Protocol ability controller.
/// Allows the player to remotely hijack an enemy node via Right-Click or Middle-Mouse raycast,
/// tethering them with a pulsing cyan line and turning them against their allies.
/// Directly drives the HUD Tether Status indicator.
/// </summary>
public class NeuralTether : MonoBehaviour
{
    public static NeuralTether Instance { get; private set; }

    [Header("Puppet Protocol Settings")]
    public float hijackRange = 12f;
    public float hijackDuration = 6f;
    public LineRenderer tetherLine;
    public LayerMask enemyLayer;

    [Header("Cooldown & Feedback")]
    public float cooldownDuration = 4f;
    [SerializeField] private AudioClip hijackSFX;

    [Header("UI Reference")]
    public TextMeshProUGUI statusText;

    private float cooldownTimer = 0f;
    private MonoBehaviour currentMinionMono = null;
    private IHijackable currentMinionHijackable = null;
    private PlayerController playerController;

    public float CooldownTimer => cooldownTimer;
    public float CooldownDuration => cooldownDuration;
    public bool IsOnCooldown => cooldownTimer > 0f;

    public bool HasActiveMinion
    {
        get
        {
            try
            {
                return currentMinionMono != null 
                    && !currentMinionMono.Equals(null) 
                    && currentMinionMono.gameObject != null;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }
    }

    public float MinionRemainingTime
    {
        get
        {
            try
            {
                return HasActiveMinion && currentMinionHijackable != null 
                    ? currentMinionHijackable.HijackRemainingTime 
                    : 0f;
            }
            catch (MissingReferenceException)
            {
                return 0f;
            }
        }
    }

    public IHijackable ActiveMinion => HasActiveMinion ? currentMinionHijackable : null;

    private void Awake()
    {
        Instance = this;
        playerController = GetComponent<PlayerController>();

        int layer = LayerMask.NameToLayer("Enemies");
        enemyLayer = (layer != -1) ? (1 << layer) : LayerMask.GetMask("Enemies");

        if (statusText == null)
        {
            GameObject hijackObj = GameObject.Find("Hijack_Text") ?? GameObject.Find("TetherStatus");
            if (hijackObj != null)
            {
                statusText = hijackObj.GetComponent<TextMeshProUGUI>();
            }
        }

        if (tetherLine == null)
        {
            Transform child = transform.Find("TetherLine");
            if (child != null)
            {
                tetherLine = child.GetComponent<LineRenderer>();
            }

            if (tetherLine == null)
            {
                GameObject tetherObj = new GameObject("TetherLine");
                tetherObj.transform.SetParent(transform, false);
                tetherLine = tetherObj.AddComponent<LineRenderer>();
            }
        }

        SetupTetherVisuals(tetherLine);

        if (tetherLine != null)
        {
            tetherLine.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void SetupTetherVisuals(LineRenderer lr)
    {
        if (lr == null) return;
        lr.useWorldSpace = true;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.positionCount = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            lr.material = new Material(shader);
        }

        Color cyan = new Color(0f, 1f, 1f, 1f);
        lr.startColor = cyan;
        lr.endColor = cyan;
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;
        }

        if (currentMinionMono != null && !HasActiveMinion)
        {
            ReleaseMinion();
        }

        if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.E))
        {
            TryInitiateHijack();
        }

        UpdateStatusUI();
    }

    private void LateUpdate()
    {
        if (!HasActiveMinion)
        {
            if (tetherLine != null) tetherLine.enabled = false;
            currentMinionMono = null;
            currentMinionHijackable = null;
            return;
        }

        UpdateTetherLine();
    }

    private void UpdateStatusUI()
    {
        if (statusText == null) return;

        if (HasActiveMinion)
        {
            int remainingSecs = Mathf.CeilToInt(MinionRemainingTime);
            if (remainingSecs < 0) remainingSecs = 0;

            statusText.text = $"NODE SLAVED: {remainingSecs:D2}s (OVERLOAD IMMINENT)";
            float flash = Mathf.PingPong(Time.time * 6f, 1f);
            statusText.color = Color.Lerp(new Color(0f, 1f, 1f, 1f), Color.white, flash * 0.75f);
        }
        else if (IsOnCooldown)
        {
            statusText.text = $"HIJACK: [RECHARGING {cooldownTimer:0.0}s]";
            statusText.color = new Color(0.55f, 0.60f, 0.66f, 1f);
        }
        else
        {
            statusText.text = "HIJACK: [READY] (R-CLICK)";
            statusText.color = new Color(6f / 255f, 182f / 255f, 212f / 255f, 1f); // Electric Cyan #06B6D4
        }
    }

    public void TryInitiateHijack()
    {
        if (playerController != null && playerController.IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != State.Playing) return;

        if (HasActiveMinion || IsOnCooldown)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;

        Vector2 origin = transform.position;
        Vector2 toMouse = (Vector2)mouseWorld - origin;
        float distToMouse = toMouse.magnitude;
        Vector2 dir = distToMouse > 0.001f ? toMouse.normalized : (Vector2)transform.up;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, hijackRange, enemyLayer);
        Collider2D hitCollider = hit.collider;

        if (hitCollider == null && distToMouse <= hijackRange)
        {
            hitCollider = Physics2D.OverlapPoint(mouseWorld, enemyLayer);
        }

        if (hitCollider != null)
        {
            IHijackable target = hitCollider.GetComponent<IHijackable>() 
                              ?? hitCollider.GetComponentInParent<IHijackable>();

            if (target != null && !target.IsHijacked)
            {
                ExecuteHijack(target);
            }
        }
    }

    private void ExecuteHijack(IHijackable target)
    {
        currentMinionHijackable = target;
        currentMinionMono = target as MonoBehaviour;
        if (currentMinionMono == null && target != null)
        {
            currentMinionMono = target.gameObject.GetComponent<MonoBehaviour>();
        }

        target.OverrideControl(hijackDuration);
        PlayHijackChime();

        if (tetherLine != null && currentMinionMono != null)
        {
            tetherLine.enabled = true;
            tetherLine.positionCount = 2;
            tetherLine.SetPosition(0, transform.position);
            tetherLine.SetPosition(1, currentMinionMono.transform.position);
        }

        CameraFollow2D.TriggerShake(0.12f, 0.2f);
    }

    private void UpdateTetherLine()
    {
        if (!HasActiveMinion || tetherLine == null)
        {
            if (tetherLine != null) tetherLine.enabled = false;
            return;
        }

        try
        {
            tetherLine.enabled = true;
            tetherLine.positionCount = 2;
            tetherLine.SetPosition(0, transform.position);
            tetherLine.SetPosition(1, currentMinionMono.transform.position);

            float pulse = Mathf.Sin(Time.time * 24f);
            float width = 0.05f + 0.018f * pulse;
            tetherLine.startWidth = width;
            tetherLine.endWidth = width * 0.85f;

            Color baseColor = new Color(0f, 1f, 1f, 0.95f);
            Color glowColor = Color.Lerp(baseColor, Color.white, Mathf.Clamp01(pulse * 0.4f + 0.3f));
            tetherLine.startColor = glowColor;
            tetherLine.endColor = baseColor;
        }
        catch (MissingReferenceException)
        {
            ReleaseMinion();
        }
    }

    public void ReleaseMinion()
    {
        currentMinionMono = null;
        currentMinionHijackable = null;

        if (tetherLine != null)
        {
            tetherLine.enabled = false;
        }

        cooldownTimer = 0f;
    }

    private void PlayHijackChime()
    {
        if (GameManager.Instance == null) return;

        AudioClip clipToPlay = hijackSFX;
        if (clipToPlay == null)
        {
            clipToPlay = GameManager.Instance.victorySFX != null 
                ? GameManager.Instance.victorySFX 
                : GameManager.Instance.shotSFX;
        }

        if (clipToPlay != null)
        {
            GameManager.Instance.PlaySound(clipToPlay, 1.48f, 0.85f);
        }
    }
}
