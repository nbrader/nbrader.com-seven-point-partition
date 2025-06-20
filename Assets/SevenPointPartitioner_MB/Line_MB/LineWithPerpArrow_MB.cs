using UnityEngine;

public class LineWithPerpArrow_MB : Line_MB
{
    [Header("Line With Perp Arrow Settings")]
    public Transform arrowTransform;
    public SpriteRenderer arrowSpriteRenderer;

    [Tooltip("Which side of the line is included in the line with perp arrow")]
    public bool interiorOnLeft = true;

    [Tooltip("Arrow size in world units at default camera size")]
    public float arrowBaseSize = 0.5f;

    [Tooltip("Distance from line center to arrow in world units")]
    public float arrowDistance = 1.0f;

    private Camera cachedCamera;

    private void Start()
    {
        cachedCamera = Camera.main;

        // Ensure arrow components exist
        if (arrowTransform == null || arrowSpriteRenderer == null)
        {
            Debug.LogWarning("LineWithPerpArrowLine: Arrow components not assigned!");
        }
    }

    new public void Update()
    {
        // Call base Update to handle line rendering
        base.Update();

        // Update arrow position and rotation
        UpdateArrow();
    }

    public override void SetVisibility(bool targetVisibility) { base.SetVisibility(targetVisibility); arrowSpriteRenderer.enabled = targetVisibility; }

    private void UpdateArrow()
    {
        if (arrowTransform == null || arrowSpriteRenderer == null || cachedCamera == null)
            return;

        // Calculate line center and direction
        Vector3 lineCenter = (endPoint1.position + endPoint2.position) * 0.5f;
        Vector2 lineDirection = (endPoint2.position - endPoint1.position).normalized;

        // Calculate perpendicular direction (90 degrees to the line)
        Vector2 perpendicular = new(-lineDirection.y, lineDirection.x);

        // Flip direction if interior is on the right side
        if (!interiorOnLeft)
            perpendicular = -perpendicular;

        // Position arrow at specified distance from line center
        Vector3 arrowPosition = lineCenter + (Vector3)(perpendicular * arrowDistance);
        arrowTransform.position = arrowPosition;

        // Rotate arrow to point in the perpendicular direction
        float arrowAngle = Mathf.Atan2(perpendicular.y, perpendicular.x) * Mathf.Rad2Deg;
        arrowTransform.rotation = Quaternion.Euler(0f, 0f, arrowAngle);

        // Scale arrow to maintain constant screen size regardless of camera zoom
        float cameraSize = cachedCamera.orthographicSize;
        float scaleFactor = cameraSize / 5f; // Assuming default camera size of 5
        float finalScale = arrowBaseSize * scaleFactor;
        arrowTransform.localScale = Vector3.one * finalScale;

        // Match arrow color to line color
        arrowSpriteRenderer.color = colour;
    }
}