using System.Collections;
using UnityEngine;

public class MiniMapMarker : MonoBehaviour
{
    public enum MarkerType
    {
        Player,
        Chaser,
        Sniper,
        Grenade
    }

    public MarkerType markerType;

    [Header("Local Offset")]
    public Vector3 localOffset = Vector3.zero;

    private SpriteRenderer iconRenderer;
    private Transform iconTransform;

    private SpriteRenderer coneRenderer;
    private Transform coneTransform;

    private SpriteRenderer pulseRenderer;
    private Transform pulseTransform;

    private float targetScale = 1.0f;
    private MarkerType lastMarkerType;

    private static Sprite smoothCircleSprite;
    private static Sprite ringSprite;
    private static Sprite coneSprite;

    private void Awake()
    {
        InitializeMarker();
    }

    private void Start()
    {
        InitializeMarker();
    }

    public void InitializeMarker()
    {
        int miniMapLayer = LayerMask.NameToLayer("MiniMap");
        if (miniMapLayer == -1) miniMapLayer = gameObject.layer;

        // 1. Setup Base Icon (Ping dot)
        if (iconTransform == null)
        {
            Transform existing = transform.Find("MiniMap_Icon");
            if (existing != null)
            {
                iconTransform = existing;
                iconRenderer = existing.GetComponent<SpriteRenderer>();
            }
            else
            {
                GameObject iconObj = new GameObject("MiniMap_Icon");
                iconTransform = iconObj.transform;
                iconTransform.SetParent(transform, false);
                iconTransform.localPosition = localOffset;
                iconRenderer = iconObj.AddComponent<SpriteRenderer>();
            }
        }

        if (iconRenderer != null)
        {
            iconRenderer.sprite = GetOrCreateCircleSprite();
            iconRenderer.sortingOrder = 100;
            iconRenderer.gameObject.layer = miniMapLayer;
        }

        // 2. Setup Directional Cone (Player only)
        if (coneTransform == null)
        {
            Transform existingCone = transform.Find("MiniMap_Cone");
            if (existingCone != null)
            {
                coneTransform = existingCone;
                coneRenderer = existingCone.GetComponent<SpriteRenderer>();
            }
            else
            {
                GameObject coneObj = new GameObject("MiniMap_Cone");
                coneTransform = coneObj.transform;
                coneTransform.SetParent(transform, false);
                coneTransform.localPosition = localOffset;
                coneRenderer = coneObj.AddComponent<SpriteRenderer>();
            }
        }

        if (coneRenderer != null)
        {
            coneRenderer.sprite = GetOrCreateConeSprite();
            coneRenderer.sortingOrder = 99;
            coneRenderer.gameObject.layer = miniMapLayer;
        }

        // 3. Setup Pulsing Outline (Sniper only)
        if (pulseTransform == null)
        {
            Transform existingPulse = transform.Find("MiniMap_Pulse");
            if (existingPulse != null)
            {
                pulseTransform = existingPulse;
                pulseRenderer = existingPulse.GetComponent<SpriteRenderer>();
            }
            else
            {
                GameObject pulseObj = new GameObject("MiniMap_Pulse");
                pulseTransform = pulseObj.transform;
                pulseTransform.SetParent(transform, false);
                pulseTransform.localPosition = localOffset;
                pulseRenderer = pulseObj.AddComponent<SpriteRenderer>();
            }
        }

        if (pulseRenderer != null)
        {
            pulseRenderer.sprite = GetOrCreateRingSprite();
            pulseRenderer.sortingOrder = 98;
            pulseRenderer.gameObject.layer = miniMapLayer;
        }

        lastMarkerType = markerType;
        ApplyMarkerConfig();
    }

    public void ApplyMarkerConfig()
    {
        Color color = Color.white;
        float size = 1.0f;

        switch (markerType)
        {
            case MarkerType.Player:
                ColorUtility.TryParseHtmlString("#06B6D4", out color); // Vivid Cyan
                size = 1.2f;
                if (coneRenderer != null)
                {
                    coneRenderer.gameObject.SetActive(true);
                    ColorUtility.TryParseHtmlString("#06B6D4", out Color coneCol);
                    coneCol.a = 0.85f;
                    coneRenderer.color = coneCol;
                }
                if (pulseRenderer != null) pulseRenderer.gameObject.SetActive(false);
                break;

            case MarkerType.Chaser:
                ColorUtility.TryParseHtmlString("#FF3333", out color); // Red radar ping
                size = 0.8f;
                if (coneRenderer != null) coneRenderer.gameObject.SetActive(false);
                if (pulseRenderer != null) pulseRenderer.gameObject.SetActive(false);
                break;

            case MarkerType.Sniper:
                ColorUtility.TryParseHtmlString("#FFCC00", out color); // Amber Gold
                size = 1.0f;
                if (coneRenderer != null) coneRenderer.gameObject.SetActive(false);
                if (pulseRenderer != null)
                {
                    pulseRenderer.gameObject.SetActive(true);
                    pulseRenderer.color = color;
                }
                break;

            case MarkerType.Grenade:
                ColorUtility.TryParseHtmlString("#CC33FF", out color); // Bright Magenta
                size = 0.5f;
                if (coneRenderer != null) coneRenderer.gameObject.SetActive(false);
                if (pulseRenderer != null) pulseRenderer.gameObject.SetActive(false);
                break;
        }

        targetScale = size;

        if (iconRenderer != null)
        {
            iconRenderer.color = color;
        }

        UpdateIconTransform();
    }

    private void LateUpdate()
    {
        if (markerType != lastMarkerType)
        {
            lastMarkerType = markerType;
            ApplyMarkerConfig();
        }

        UpdateIconTransform();
    }

    private void UpdateIconTransform()
    {
        if (iconTransform == null) return;

        // Maintain absolute upright orientation for the base circle dot
        iconTransform.rotation = Quaternion.identity;
        iconTransform.position = transform.position + localOffset;

        Vector3 parentLossy = transform.lossyScale;
        float parentScaleX = Mathf.Abs(parentLossy.x) > 0.0001f ? Mathf.Abs(parentLossy.x) : 1f;
        float parentScaleY = Mathf.Abs(parentLossy.y) > 0.0001f ? Mathf.Abs(parentLossy.y) : 1f;

        iconTransform.localScale = new Vector3(targetScale / parentScaleX, targetScale / parentScaleY, 1f);

        // Dynamic Player Forward Cone (aiming & firing indicator)
        if (markerType == MarkerType.Player && coneTransform != null && coneTransform.gameObject.activeSelf)
        {
            coneTransform.position = iconTransform.position;
            coneTransform.localScale = new Vector3(1.4f / parentScaleX, 1.4f / parentScaleY, 1f);

            Vector2 aimDir = Vector2.zero;
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 mouseWorld = mainCam.ScreenToWorldPoint(Input.mousePosition);
                aimDir = ((Vector2)mouseWorld - (Vector2)transform.position).normalized;
            }

            if (aimDir.sqrMagnitude < 0.001f)
            {
                aimDir = transform.up;
            }

            float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg - 90f;
            coneTransform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // Dynamic Sniper Pulsing Sonar Outline
        if (markerType == MarkerType.Sniper && pulseTransform != null && pulseTransform.gameObject.activeSelf)
        {
            pulseTransform.position = iconTransform.position;
            pulseTransform.rotation = Quaternion.identity;

            float t = (Time.time * 2.2f) % 1f;
            float pulseScale = (1.0f + t * 0.9f) * targetScale;
            pulseTransform.localScale = new Vector3(pulseScale / parentScaleX, pulseScale / parentScaleY, 1f);

            if (pulseRenderer != null)
            {
                float alpha = (1f - t) * 0.75f;
                ColorUtility.TryParseHtmlString("#FFCC00", out Color goldColor);
                goldColor.a = alpha;
                pulseRenderer.color = goldColor;
            }
        }
    }

    /// <summary>
    /// Triggers a brief directional ray indicator on the radar grid fading over 0.2s to show active line of fire.
    /// </summary>
    public void TriggerFiringRay(Vector2 direction, float range = 15f, Color? rayColor = null)
    {
        Color colorToUse;
        if (rayColor.HasValue)
        {
            colorToUse = rayColor.Value;
        }
        else
        {
            switch (markerType)
            {
                case MarkerType.Player:
                    ColorUtility.TryParseHtmlString("#06B6D4", out colorToUse); // Vivid Cyan
                    break;
                case MarkerType.Sniper:
                    ColorUtility.TryParseHtmlString("#FFCC00", out colorToUse); // Amber Gold
                    break;
                case MarkerType.Grenade:
                    ColorUtility.TryParseHtmlString("#CC33FF", out colorToUse); // Magenta
                    break;
                default:
                    ColorUtility.TryParseHtmlString("#FF3333", out colorToUse); // Red
                    break;
            }
        }

        TriggerRay(transform.position + localOffset, direction, range, colorToUse);
    }

    /// <summary>
    /// Static helper to spawn a tactical directional firing ray on the MiniMap layer fading over 0.2 seconds.
    /// </summary>
    public static void TriggerRay(Vector3 origin, Vector2 direction, float range, Color color)
    {
        if (direction.sqrMagnitude < 0.001f) return;

        int miniMapLayer = LayerMask.NameToLayer("MiniMap");
        if (miniMapLayer == -1) miniMapLayer = 0;

        GameObject rayObj = new GameObject("Radar_FireRay");
        rayObj.layer = miniMapLayer;
        rayObj.transform.position = origin;

        LineRenderer lr = rayObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, origin);
        lr.SetPosition(1, origin + (Vector3)(direction.normalized * range));

        lr.startWidth = 0.14f;
        lr.endWidth = 0.04f;
        lr.sortingOrder = 130;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            lr.material = new Material(spriteShader);
        }

        color.a = 1.0f;
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0.4f);

        RadarRayFade fade = rayObj.AddComponent<RadarRayFade>();
        fade.Init(lr, color, 0.2f);
    }

    public static Sprite GetOrCreateCircleSprite()
    {
        if (smoothCircleSprite != null) return smoothCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "MiniMap_CircleSprite";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float radius = size * 0.46f;
        Color[] cols = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01((radius - dist) + 0.5f);
                cols[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        smoothCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return smoothCircleSprite;
    }

    public static Sprite GetOrCreateRingSprite()
    {
        if (ringSprite != null) return ringSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "MiniMap_RingSprite";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float outerRadius = size * 0.46f;
        float innerRadius = size * 0.34f;
        Color[] cols = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alphaOuter = Mathf.Clamp01((outerRadius - dist) + 0.5f);
                float alphaInner = Mathf.Clamp01((dist - innerRadius) + 0.5f);
                float alpha = Mathf.Min(alphaOuter, alphaInner);
                cols[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return ringSprite;
    }

    public static Sprite GetOrCreateConeSprite()
    {
        if (coneSprite != null) return coneSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "MiniMap_ConeSprite";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float originX = 31.5f;
        float originY = 16f; // Pivot near circle center
        Vector2 pivot = new Vector2(originX, originY);
        Color[] cols = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                cols[y * size + x] = Color.clear;

                if (y < originY) continue;

                Vector2 delta = new Vector2(x - originX, y - originY);
                float dist = delta.magnitude;
                if (dist > 44f) continue;

                float angle = Vector2.Angle(Vector2.up, delta);
                if (angle > 24f) continue;

                // Bright central laser line
                float centralBeam = Mathf.Abs(delta.x) < 1.4f ? 1.0f : 0f;

                // Soft fanning cone alpha
                float angleAlpha = Mathf.Clamp01((24f - angle) / 24f);
                float distAlpha = Mathf.Clamp01((44f - dist) / 44f);
                float coneAlpha = angleAlpha * distAlpha * 0.75f;

                float finalAlpha = Mathf.Max(centralBeam * distAlpha, coneAlpha);
                cols[y * size + x] = new Color(1f, 1f, 1f, finalAlpha);
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        coneSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, originY / size), size);
        return coneSprite;
    }
}

/// <summary>
/// Handles smooth 0.2s fade out and auto-destruction for active line-of-fire radar indicators.
/// </summary>
public class RadarRayFade : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private Color baseColor;
    private float duration = 0.2f;
    private float elapsed = 0f;

    public void Init(LineRenderer lr, Color col, float fadeDuration = 0.2f)
    {
        lineRenderer = lr;
        baseColor = col;
        duration = fadeDuration;
        elapsed = 0f;
    }

    private void Update()
    {
        if (lineRenderer == null)
        {
            Destroy(gameObject);
            return;
        }

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(1f - (elapsed / duration));

        Color cStart = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * t);
        Color cEnd = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * t * 0.35f);

        lineRenderer.startColor = cStart;
        lineRenderer.endColor = cEnd;

        float currentWidth = 0.14f * t;
        lineRenderer.startWidth = currentWidth;
        lineRenderer.endWidth = currentWidth * 0.3f;

        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}
