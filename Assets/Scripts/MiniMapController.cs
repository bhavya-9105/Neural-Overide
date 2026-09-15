using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// High-performance, zero-allocation Canvas UI Mini-Map / Vector Radar.
/// Mathematically projects active enemies and projectiles relative to the player
/// onto a circular HUD radar using pre-pooled UI Image dots.
/// </summary>
public class MiniMapController : MonoBehaviour
{
    public static MiniMapController Instance { get; private set; }

    [Header("Container & References")]
    [Tooltip("Container RectTransform holding all dynamic blip Image dots.")]
    public RectTransform blipsContainer;
    [Tooltip("Reference to the player transform. Auto-cached if null.")]
    public Transform playerTransform;

    [Header("Radar Mapping Settings")]
    [Tooltip("World-to-radar scale factor. Distance in world units * radarScale = blip pixel distance from center.")]
    public float radarScale = 4.5f;
    [Tooltip("Maximum pixel radius from radar center before blips are clamped to the radar perimeter.")]
    public float maxRadius = 82f;

    [Header("Blip Colors & Visuals")]
    public Color playerColor = new Color(0f, 1f, 1f, 1f);          // Cyan #00FFFF
    public Color chaserColor = new Color(1f, 0.2f, 0.2f, 1f);        // Crimson #FF3333
    public Color sniperColor = new Color(1f, 0.8f, 0f, 1f);          // Amber Gold #FFCC00
    public Color projectileColor = new Color(0.8f, 0.2f, 1f, 1f);    // Magenta #CC33FF
    public Color alliedColor = new Color(0.1f, 0.9f, 1f, 1f);        // Bright Electric Cyan

    [Header("Pooling")]
    [SerializeField] private int initialPoolSize = 64;

    private readonly List<Image> blipPool = new List<Image>();
    private Image playerBlipImage;
    private static Sprite cachedCircleSprite;

    private void Awake()
    {
        Instance = this;

        if (blipsContainer == null)
        {
            Transform container = transform.Find("MiniMap_BlipsContainer");
            if (container != null)
            {
                blipsContainer = container as RectTransform;
            }
            if (blipsContainer == null)
            {
                blipsContainer = GetComponent<RectTransform>();
            }
        }

        EnsurePlayerBlip();
        InitializeBlipPool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        CachePlayer();
    }

    private void CachePlayer()
    {
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy) return;

        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }
    }

    private void EnsurePlayerBlip()
    {
        if (blipsContainer == null) return;

        Transform pb = blipsContainer.Find("Player_Blip");
        GameObject pbObj;
        if (pb == null)
        {
            pbObj = new GameObject("Player_Blip", typeof(RectTransform));
            pbObj.transform.SetParent(blipsContainer, false);
        }
        else
        {
            pbObj = pb.gameObject;
        }

        RectTransform rt = pbObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(10f, 10f);

        playerBlipImage = pbObj.GetComponent<Image>() ?? pbObj.AddComponent<Image>();
        playerBlipImage.sprite = GetOrCreateCircleSprite();
        playerBlipImage.color = playerColor;
        playerBlipImage.raycastTarget = false;
        pbObj.SetActive(true);
    }

    private void InitializeBlipPool()
    {
        if (blipsContainer == null) return;

        Sprite dotSprite = GetOrCreateCircleSprite();

        // Clear or reuse any existing pool objects
        for (int i = 0; i < blipsContainer.childCount; i++)
        {
            Transform child = blipsContainer.GetChild(i);
            if (child.name.StartsWith("Blip_"))
            {
                Image img = child.GetComponent<Image>();
                if (img != null && !blipPool.Contains(img))
                {
                    blipPool.Add(img);
                    child.gameObject.SetActive(false);
                }
            }
        }

        while (blipPool.Count < initialPoolSize)
        {
            CreatePooledBlip(dotSprite);
        }
    }

    private Image CreatePooledBlip(Sprite dotSprite)
    {
        int index = blipPool.Count;
        GameObject blipObj = new GameObject($"Blip_{index}", typeof(RectTransform));
        blipObj.transform.SetParent(blipsContainer, false);

        RectTransform rt = blipObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(8f, 8f);

        Image img = blipObj.AddComponent<Image>();
        img.sprite = dotSprite;
        img.raycastTarget = false;
        blipObj.SetActive(false);

        blipPool.Add(img);
        return img;
    }

    private Image GetBlip(int index)
    {
        if (index < blipPool.Count)
        {
            return blipPool[index];
        }

        return CreatePooledBlip(GetOrCreateCircleSprite());
    }

    public static Sprite GetOrCreateCircleSprite()
    {
        if (cachedCircleSprite != null) return cachedCircleSprite;

        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "MiniMapDotSprite";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = (size * 0.5f) - 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        cachedCircleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cachedCircleSprite;
    }

    private static Sprite cachedRadarBgSprite;

    public static Sprite GetOrCreateRadarBackgroundSprite()
    {
        if (cachedRadarBgSprite != null) return cachedRadarBgSprite;

        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "RadarBgTexture";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float outerRadius = 125f;
        float innerRadius = 122f;
        float midRadius = 62f;

        Color slateBg = new Color(10f / 255f, 15f / 255f, 29f / 255f, 0.95f); // #0A0F1D
        Color cyanBorder = new Color(6f / 255f, 182f / 255f, 212f / 255f, 1f); // #06B6D4
        Color crosshairColor = new Color(6f / 255f, 182f / 255f, 212f / 255f, 0.25f);
        Color ringColor = new Color(6f / 255f, 182f / 255f, 212f / 255f, 0.20f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);

                if (dist > outerRadius + 1f)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > innerRadius)
                {
                    // 2px Cyan Border Ring
                    float borderAlpha = Mathf.Clamp01(outerRadius + 1f - dist);
                    tex.SetPixel(x, y, new Color(cyanBorder.r, cyanBorder.g, cyanBorder.b, cyanBorder.a * borderAlpha));
                }
                else
                {
                    // Inside radar: solid slate background
                    Color pixelColor = slateBg;

                    // Subtle concentric range ring
                    float ringDist = Mathf.Abs(dist - midRadius);
                    if (ringDist <= 1.2f)
                    {
                        float blend = Mathf.Clamp01(1.2f - ringDist);
                        pixelColor = Color.Lerp(pixelColor, ringColor, blend);
                    }

                    // Subtle crosshairs
                    float distX = Mathf.Abs(x - center.x);
                    float distY = Mathf.Abs(y - center.y);
                    if (distX <= 1f || distY <= 1f)
                    {
                        float blend = Mathf.Clamp01(1f - Mathf.Min(distX, distY));
                        pixelColor = Color.Lerp(pixelColor, crosshairColor, blend);
                    }

                    tex.SetPixel(x, y, pixelColor);
                }
            }
        }
        tex.Apply();

        cachedRadarBgSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cachedRadarBgSprite;
    }

    private void LateUpdate()
    {
        if (playerTransform == null)
        {
            CachePlayer();
            if (playerTransform == null)
            {
                HideAllBlips();
                return;
            }
        }

        Vector2 playerPos = playerTransform.position;
        var enemies = WaveSpawner.activeEnemies;
        int activeBlipCount = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null || !enemy.activeInHierarchy) continue;

            // Determine blip properties
            Color dotColor;
            Vector2 dotSize;

            MiniMapMarker marker = enemy.GetComponent<MiniMapMarker>();
            if (marker != null)
            {
                dotColor = marker.customColor;
                dotSize = marker.blipSize;
            }
            else
            {
                EnemyChaser chaser = enemy.GetComponent<EnemyChaser>();
                if (chaser != null)
                {
                    if (chaser.isDead) continue;
                    dotColor = chaser.isHijacked ? alliedColor : chaserColor;
                    dotSize = new Vector2(8f, 8f);
                }
                else
                {
                    SniperNode sniper = enemy.GetComponent<SniperNode>();
                    if (sniper != null)
                    {
                        if (sniper.Health <= 0) continue;
                        dotColor = sniper.isHijacked ? alliedColor : sniperColor;
                        dotSize = new Vector2(10f, 10f);
                    }
                    else
                    {
                        EnemyProjectile proj = enemy.GetComponent<EnemyProjectile>();
                        if (proj != null)
                        {
                            dotColor = proj.IsAllied ? alliedColor : projectileColor;
                            dotSize = new Vector2(6f, 6f);
                        }
                        else
                        {
                            dotColor = chaserColor;
                            dotSize = new Vector2(8f, 8f);
                        }
                    }
                }
            }

            // Map entity world positions directly to local UI coordinates on the 180x180 canvas
            Vector2 localPos = ((Vector2)enemy.transform.position - playerPos) * radarScale;
            localPos = Vector2.ClampMagnitude(localPos, maxRadius);

            // Apply to pooled blip
            Image blip = GetBlip(activeBlipCount);
            activeBlipCount++;

            RectTransform rt = blip.rectTransform;
            rt.anchoredPosition = localPos;
            rt.sizeDelta = dotSize;
            blip.color = dotColor;

            if (!blip.gameObject.activeSelf)
            {
                blip.gameObject.SetActive(true);
            }
        }

        // Deactivate unused pooled blips
        for (int i = activeBlipCount; i < blipPool.Count; i++)
        {
            if (blipPool[i].gameObject.activeSelf)
            {
                blipPool[i].gameObject.SetActive(false);
            }
        }
    }

    public void HideAllBlips()
    {
        for (int i = 0; i < blipPool.Count; i++)
        {
            if (blipPool[i] != null && blipPool[i].gameObject.activeSelf)
            {
                blipPool[i].gameObject.SetActive(false);
            }
        }
    }
}
