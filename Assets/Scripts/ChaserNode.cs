using UnityEngine;

public class ChaserNode : MonoBehaviour
{
    private Transform playerTransform;
    public Transform PlayerTransform => playerTransform;



    private void OnEnable()
    {
        WaveSpawner.RegisterEnemy(gameObject);
        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
        }
    }

    private void Start()
    {
        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
        }
    }

    private void OnDisable()
    {
        WaveSpawner.UnregisterEnemy(gameObject);
    }

    private void OnDestroy()
    {
        WaveSpawner.UnregisterEnemy(gameObject);
    }
}
