using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Provides high-tech smooth hover scale transitions (Vector3 scale 1.05x on pointer enter/hover)
/// for UI buttons and interactive elements.
/// </summary>
public class UIHoverScaleEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float duration = 0.10f;

    private Vector3 originalScale = Vector3.one;
    private Coroutine scaleCoroutine;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    private void OnDisable()
    {
        if (scaleCoroutine != null)
        {
            StopCoroutine(scaleCoroutine);
            scaleCoroutine = null;
        }
        transform.localScale = originalScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        StartScale(originalScale * hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        StartScale(originalScale);
    }

    public void OnSelect(BaseEventData eventData)
    {
        StartScale(originalScale * hoverScale);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        StartScale(originalScale);
    }

    private void StartScale(Vector3 target)
    {
        if (!gameObject.activeInHierarchy) return;
        if (scaleCoroutine != null) StopCoroutine(scaleCoroutine);
        scaleCoroutine = StartCoroutine(ScaleRoutine(target));
    }

    private IEnumerator ScaleRoutine(Vector3 target)
    {
        Vector3 start = transform.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Smooth sine ease-out
            t = Mathf.Sin(t * Mathf.PI * 0.5f);
            transform.localScale = Vector3.Lerp(start, target, t);
            yield return null;
        }
        transform.localScale = target;
        scaleCoroutine = null;
    }
}
