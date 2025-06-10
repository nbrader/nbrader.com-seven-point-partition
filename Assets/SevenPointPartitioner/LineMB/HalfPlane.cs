using UnityEngine;

public class HalfPlane : Line
{
    [Header("Half-Plane Settings")]
    public Transform arrowTransform;
    public SpriteRenderer arrowSpriteRenderer;

    [Tooltip("Which side of the line is included in the half-plane")]
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
            Debug.LogWarning("HalfPlaneLine: Arrow components not assigned!");
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

    /// <summary>
    /// Check if a point is inside the closed half-plane (including points on the line)
    /// </summary>
    /// <param name="point">Point to test</param>
    /// <returns>True if point is in the half-plane, false otherwise</returns>
    public bool ContainsPoint(Vector2 point)
    {
        Vector2 p1 = inputPoint1.position;
        Vector2 p2 = inputPoint2.position;

        // Handle degenerate case where points are the same
        if (p1 == p2)
            return false;

        // Calculate which side of the line the point is on
        // Using the cross product method: (p2-p1) × (point-p1)
        Vector2 lineDir = p2 - p1;
        Vector2 pointDir = point - p1;
        float crossProduct = lineDir.x * pointDir.y - lineDir.y * pointDir.x;

        // Determine if point is on the correct side based on interior direction
        if (interiorOnLeft)
            return crossProduct >= 0f; // Point is to the left of the line
        else
            return crossProduct <= 0f; // Point is to the right of the line
    }

    /// <summary>
    /// Get the normal vector pointing into the half-plane interior
    /// </summary>
    /// <returns>Normalized vector pointing into the half-plane</returns>
    public Vector2 GetInteriorNormal()
    {
        Vector2 p1 = inputPoint1.position;
        Vector2 p2 = inputPoint2.position;
        Vector2 lineDirection = (p2 - p1).normalized;

        // Calculate perpendicular (90 degrees counterclockwise)
        Vector2 perpendicular = new(-lineDirection.y, lineDirection.x);

        // Return based on which side is interior
        return interiorOnLeft ? perpendicular : -perpendicular;
    }

    /// <summary>
    /// Get the distance from a point to the line (positive if inside half-plane, negative if outside)
    /// </summary>
    /// <param name="point">Point to measure distance from</param>
    /// <returns>Signed distance (positive = inside, negative = outside, 0 = on line)</returns>
    public float GetSignedDistanceToPoint(Vector2 point)
    {
        Vector2 p1 = inputPoint1.position;
        Vector2 p2 = inputPoint2.position;

        if (p1 == p2)
            return 0f;

        // Calculate distance using point-to-line formula
        Vector2 lineDir = (p2 - p1).normalized;
        Vector2 pointToP1 = point - p1;

        // Project onto perpendicular to get signed distance
        Vector2 perpendicular = new(-lineDir.y, lineDir.x);
        if (!interiorOnLeft)
            perpendicular = -perpendicular;

        return Vector2.Dot(pointToP1, perpendicular);
    }

    /// <summary>
    /// Toggle which side of the line is considered the interior of the half-plane
    /// </summary>
    public void FlipInteriorSide()
    {
        interiorOnLeft = !interiorOnLeft;
    }
}