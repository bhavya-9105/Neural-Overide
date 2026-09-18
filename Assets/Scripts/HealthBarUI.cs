using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [Header("Image References")]
    public Image backgroundImage;
    public Image fillImage;

    [Header("Text Reference")]
    public TextMeshProUGUI hpText;
    public string hpPrefix = "HP: ";

    [Header("Player Reference")]
    public PlayerController player;

    private static Sprite whiteSprite;

    // Public properties for backwards compatibility
    public Image BackgroundImage { get => backgroundImage; set => backgroundImage = value; }
    public Image FillImage { get => fillImage; set => fillImage = value; }
    public TextMeshProUGUI TmpText { get => hpText; set => hpText = value; }
    public TextMeshProUGUI HpText { get => hpText; set => hpText = value; }
    public PlayerController Player { get => player; set => player = value; }

    private void Awake()
    {
        FindPlayer();
        CacheComponents();
        EnsureWhiteSprites();
        ApplyDefaultDarkTealTheme();
    }

    private int lastHp = -1;
    private int lastMaxHp = -1;

    private void Start()
    {
        FindPlayer();
        CacheComponents();
        EnsureWhiteSprites();
        ApplyDefaultDarkTealTheme();
        UpdateUI(true);
    }

    private void ApplyDefaultDarkTealTheme()
    {
        if (fillImage != null && (fillImage.color == Color.white || fillImage.color == new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f)))
        {
            ColorUtility.TryParseHtmlString("#14B8A6", out Color tealFill);
            fillImage.color = tealFill;
        }
        if (backgroundImage != null && (backgroundImage.color == Color.white || backgroundImage.color == new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f)))
        {
            ColorUtility.TryParseHtmlString("#061424", out Color darkBg);
            backgroundImage.color = darkBg;
        }
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
        }

        if (player != null)
        {
            if (player.currentHealth != lastHp || player.maxHealth != lastMaxHp)
            {
                UpdateUI();
            }
        }
    }

    public void SetThemeColors(Color fillColor, Color bgColor, string prefix = "HP: ")
    {
        hpPrefix = prefix;

        if (fillImage != null)
        {
            fillImage.color = fillColor;
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = bgColor;
        }

        UpdateUI(true);
    }

    public void UpdateUI(bool force = false)
    {
        if (player != null)
        {
            if (!force && player.currentHealth == lastHp && player.maxHealth == lastMaxHp)
            {
                return;
            }

            lastHp = player.currentHealth;
            lastMaxHp = player.maxHealth;

            int maxHp = player.maxHealth > 0 ? player.maxHealth : 1;

            if (fillImage != null)
            {
                fillImage.fillAmount = (float)player.currentHealth / maxHp;
            }

            if (hpText != null)
            {
                hpText.text = $"{hpPrefix}{player.currentHealth} / {player.maxHealth}";
            }
        }
    }

    public void SetHealth(int current, int max)
    {
        if (player != null)
        {
            player.currentHealth = current;
            player.maxHealth = max;
        }

        lastHp = current;
        lastMaxHp = max;

        int safeMax = max > 0 ? max : 1;
        if (fillImage != null)
        {
            fillImage.fillAmount = Mathf.Clamp01((float)current / safeMax);
        }

        if (hpText != null)
        {
            hpText.text = $"{hpPrefix}{current} / {max}";
        }
    }

    private void FindPlayer()
    {
        if (player != null) return;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            playerObj = GameObject.Find("Player");
        }

        if (playerObj != null)
        {
            player = playerObj.GetComponent<PlayerController>();
        }

        if (player == null)
        {
            player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        }
    }

    private void CacheComponents()
    {
        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
        }

        if (fillImage == null)
        {
            Transform fillTrans = transform.Find("FillImage") ?? transform.Find("Fill");
            if (fillTrans != null)
            {
                fillImage = fillTrans.GetComponent<Image>();
            }
        }

        if (hpText == null)
        {
            Transform textTrans = transform.Find("HP_Text") ?? transform.Find("HPText") ?? transform.Find("Text");
            if (textTrans != null)
            {
                hpText = textTrans.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                hpText = GetComponentInChildren<TextMeshProUGUI>();
            }
        }
    }

    private void EnsureWhiteSprites()
    {
        Sprite solidWhite = GetOrCreateWhiteSprite();

        if (backgroundImage != null && backgroundImage.sprite == null)
        {
            backgroundImage.sprite = solidWhite;
        }

        if (fillImage != null)
        {
            if (fillImage.sprite == null)
            {
                fillImage.sprite = solidWhite;
            }
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
    }

    public static Sprite GetOrCreateWhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
        }
        return whiteSprite;
    }

    private Coroutine pulseCoroutine;

    public void TriggerLifeExtendPulse()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
        }
        pulseCoroutine = StartCoroutine(LifeExtendPulseRoutine());
    }

    private System.Collections.IEnumerator LifeExtendPulseRoutine()
    {
        Vector3 originalScale = Vector3.one;
        Vector3 punchScale = originalScale * 1.2f;

        float duration = 0.35f;
        float elapsed = 0f;

        Transform targetTrans = transform;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            targetTrans.localScale = Vector3.Lerp(punchScale, originalScale, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        targetTrans.localScale = originalScale;
        pulseCoroutine = null;
    }
}
