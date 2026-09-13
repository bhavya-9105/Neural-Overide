using UnityEngine;

public class ChaserNode : MonoBehaviour
{
    private void Awake()
    {
        MiniMapMarker marker = GetComponent<MiniMapMarker>();
        if (marker == null)
        {
            marker = gameObject.AddComponent<MiniMapMarker>();
        }
        marker.markerType = MiniMapMarker.MarkerType.Chaser;
    }
}
