using UnityEngine;
using UnityEngine.UI;

public class WorldServePowerBar : MonoBehaviour
{
    public Vector3 worldOffset = new Vector3(0.85f, 1.9f, 0f);

    private Transform target;
    private RectTransform fillRect;
    private Canvas canvas;

    public static WorldServePowerBar Create()
    {
        GameObject root = new GameObject("WorldServePowerBar");
        var bar = root.AddComponent<WorldServePowerBar>();
        bar.Build();
        bar.Hide();
        return bar;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        UpdateTransform();
    }

    public void SetPower(float normalizedPower)
    {
        if (fillRect == null) return;
        float width = Mathf.Lerp(0f, 1.4f, Mathf.Clamp01(normalizedPower));
        fillRect.sizeDelta = new Vector2(width, fillRect.sizeDelta.y);
    }

    public void Show()
    {
        if (canvas != null)
            canvas.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (canvas != null)
            canvas.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1.6f, 0.22f);

        gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 120f;

        GameObject background = new GameObject("Background");
        background.transform.SetParent(transform, false);
        RectTransform bgRect = background.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = new Vector2(1.5f, 0.16f);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.04f, 0.04f, 0.04f, 0.82f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(background.transform, false);
        fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(0f, 0.5f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(0f, 0.12f);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
    }

    private void UpdateTransform()
    {
        if (target == null) return;

        transform.position = target.position + worldOffset;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 dir = transform.position - cam.transform.position;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
