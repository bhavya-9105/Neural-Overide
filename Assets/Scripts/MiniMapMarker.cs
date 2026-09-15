using UnityEngine;

/// <summary>
/// Lightweight metadata marker for entities displayed on the Canvas UI Mini-Map.
/// Allows zero-overhead identification of blip type, color, and dimensions.
/// </summary>
public class MiniMapMarker : MonoBehaviour
{
    public enum MarkerType
    {
        Player,
        Chaser,
        Sniper,
        Grenade
    }

    [Header("Marker Configuration")]
    public MarkerType markerType = MarkerType.Chaser;
    public Color customColor = new Color(1f, 0.2f, 0.2f, 1f); // Crimson Red #FF3333
    public Vector2 blipSize = new Vector2(8f, 8f);

    private void Awake()
    {
        ApplyDefaultConfiguration();
    }

    private void Reset()
    {
        ApplyDefaultConfiguration();
    }

    public void ApplyDefaultConfiguration()
    {
        switch (markerType)
        {
            case MarkerType.Player:
                customColor = new Color(0f, 1f, 1f, 1f);       // Cyan #00FFFF
                blipSize = new Vector2(10f, 10f);
                break;
            case MarkerType.Chaser:
                customColor = new Color(1f, 0.2f, 0.2f, 1f);   // Crimson Red #FF3333
                blipSize = new Vector2(8f, 8f);
                break;
            case MarkerType.Sniper:
                customColor = new Color(1f, 0.8f, 0f, 1f);     // Amber Gold #FFCC00
                blipSize = new Vector2(10f, 10f);
                break;
            case MarkerType.Grenade:
                customColor = new Color(0.8f, 0.2f, 1f, 1f);   // Magenta #CC33FF
                blipSize = new Vector2(6f, 6f);
                break;
        }
    }
}
