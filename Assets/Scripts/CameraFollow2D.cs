using System.Collections;
using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    public static CameraFollow2D Instance { get; private set; }

    [Header("Target Settings")]
    [Tooltip("Target Transform to follow (auto-finds GameObject tagged 'Player' if null)")]
    [SerializeField] private Transform target;

    [Header("Follow Settings")]
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private float lookAheadDistance = 1.8f;
    [SerializeField] private float smoothTime = 0.12f;
    [SerializeField] private bool clampToBounds = true;
    [SerializeField] private Vector2 minBounds = new Vector2(-12f, -6f);
    [SerializeField] private Vector2 maxBounds = new Vector2(12f, 6f);

    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 currentBasePosition;
    private Vector3 shakeOffset = Vector3.zero;
    private Coroutine shakeCoroutine;

    public Transform Target { get => target; set => target = value; }
    public float SmoothSpeed { get => smoothSpeed; set => smoothSpeed = value; }
    public float LookAheadDistance { get => lookAheadDistance; set => lookAheadDistance = value; }
    public float SmoothTime { get => smoothTime; set => smoothTime = value; }
    public bool ClampToBounds { get => clampToBounds; set => clampToBounds = value; }
    public Vector2 MinBounds { get => minBounds; set => minBounds = value; }
    public Vector2 MaxBounds { get => maxBounds; set => maxBounds = value; }

    private void Awake()
    {
        Instance = this;
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
        if (target == null && PlayerController.Instance != null)
        {
            target = PlayerController.Instance.transform;
        }
        else if (target == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
            }
        }

        if (target != null)
        {
            transform.position = new Vector3(target.position.x, target.position.y, -10f);
        }
        else
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, -10f);
        }
    }

    // Direct camera lock to player in LateUpdate to eliminate frame drops and physics jitter
    private void LateUpdate()
    {
        if (target == null)
        {
            if (PlayerController.Instance != null)
            {
                target = PlayerController.Instance.transform;
            }
            if (target == null) return;
        }

        // Lock camera position strictly to player without SmoothDamp or per-frame scene searches
        transform.position = new Vector3(target.position.x, target.position.y, -10f) + shakeOffset;
    }

    // Static trigger so scripts can call CameraFollow2D.TriggerShake(duration, magnitude) directly
    public static void TriggerShake(float duration = 0.15f, float magnitude = 0.2f)
    {
        if (Instance != null)
        {
            Instance.DoTriggerShake(duration, magnitude);
        }
        else
        {
            CameraFollow2D cam = Object.FindAnyObjectByType<CameraFollow2D>();
            if (cam != null)
            {
                cam.DoTriggerShake(duration, magnitude);
            }
        }
    }

    public void DoTriggerShake(float duration = 0.15f, float magnitude = 0.2f)
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeOffset = Vector3.zero;
        }
        shakeCoroutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            shakeOffset = new Vector3(x, y, 0f);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Cleanly returns to Vector3.zero when duration expires
        shakeOffset = Vector3.zero;
        shakeCoroutine = null;
    }
}

public static class CameraFollow2DExtensions
{
    // Extension method to maintain compatibility with any instance.TriggerShake() calls
    public static void TriggerShake(this CameraFollow2D cam, float duration = 0.15f, float magnitude = 0.2f)
    {
        CameraFollow2D.TriggerShake(duration, magnitude);
    }
}
