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

    private Vector3 currentBasePosition;
    private Vector3 shakeOffset = Vector3.zero;
    private Coroutine shakeCoroutine;

    public Transform Target { get => target; set => target = value; }
    public float SmoothSpeed { get => smoothSpeed; set => smoothSpeed = value; }

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
        // In Start(), if target == null, attempt GameObject.FindWithTag("Player").transform
        if (target == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
            }
        }

        if (target != null)
        {
            currentBasePosition = new Vector3(target.position.x, target.position.y, -10f);
        }
        else
        {
            currentBasePosition = new Vector3(transform.position.x, transform.position.y, -10f);
        }

        transform.position = currentBasePosition;
    }

    // Use LateUpdate() strictly for camera movement (never Update or FixedUpdate) to prevent physics-induced jitter
    private void LateUpdate()
    {
        if (target == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
            }
            else
            {
                return;
            }
        }

        // Smooth position interpolation towards the player target using Vector3.Lerp:
        Vector3 desiredPosition = new Vector3(target.position.x, target.position.y, -10f);
        Vector3 smoothedPosition = Vector3.Lerp(currentBasePosition, desiredPosition, smoothSpeed * Time.deltaTime);
        currentBasePosition = smoothedPosition;

        // Add shake offset as an independent additive Vector3, not overriding the base position:
        transform.position = smoothedPosition + shakeOffset;
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
