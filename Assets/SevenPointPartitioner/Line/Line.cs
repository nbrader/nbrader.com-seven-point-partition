using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class Line : MonoBehaviour
{
    [HideInInspector]
    public SevenPointPartitioner parentSevenPointPartitioner;
    public SpriteRenderer spriteRenderer;

    public Transform lineTransform;
    public Transform inputPoint1;
    public Transform inputPoint2;
    public Transform endPoint1;
    public Transform endPoint2;

    public Color colour;

    // Event for allowing external scripts to request visibility.
    public event Func<Line, bool> ShouldBeVisible;

    // Event for allowing external scripts to force the line hidden.
    public event Func<Line, bool> ForceHidden;

    public bool IsVisible
    {
        get => spriteRenderer.enabled;
        private set => SetVisibility(value);
    }

    public virtual void SetVisibility(bool targetVisibility) { spriteRenderer.enabled = targetVisibility; }

    protected void Update()
    {
        UpdateLineEndpoints();

        Vector2 disp = Maths.ProjectVec3DownZ(endPoint2.position - endPoint1.position);
        float degrees = Maths.Rad2Deg(Maths.AngleFromVec2(disp));
        float dist = disp.magnitude;

        lineTransform.position = endPoint1.position;
        lineTransform.rotation = Quaternion.Euler(0f, 0f, degrees);
        lineTransform.localScale = new Vector3(dist, SevenPointPartitioner.lineVisibleThickness, 1);

        spriteRenderer.color = colour;

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        // Ask all subscribers if they want the line shown
        bool requestedVisible = ShouldBeVisible?.GetInvocationList()
            .Cast<Func<Line, bool>>()
            .Any(callback => callback(this)) ?? false;

        // Ask all subscribers if they want the line forcibly hidden
        bool forceHidden = ForceHidden?.GetInvocationList()
            .Cast<Func<Line, bool>>()
            .Any(callback => callback(this)) ?? false;

        IsVisible = requestedVisible && !forceHidden;
    }

    public void UpdateLineEndpoints()
    {
        Vector2 p1 = inputPoint1.position;
        Vector2 p2 = inputPoint2.position;

        List<Vector2> intersections = GetLineCameraViewIntersections(p1, p2);

        if (intersections.Count == 2)
        {
            endPoint1.position = new Vector3(intersections[0].x, intersections[0].y, 0);
            endPoint2.position = new Vector3(intersections[1].x, intersections[1].y, 0);
        }
        else
        {
            // Move endpoints off-screen to avoid drawing garbage if not visible
            endPoint1.position = Vector3.one * 99999f;
            endPoint2.position = Vector3.one * 99999f;
        }
    }

    private List<Vector2> GetLineCameraViewIntersections(Vector2 a, Vector2 b)
    {
        if (a == b) b = a + Vector2.right;

        Camera cam = Camera.main;
        float camHeight = 2f * cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;
        Vector2 camCenter = cam.transform.position;

        float left = camCenter.x - camWidth / 2f;
        float right = camCenter.x + camWidth / 2f;
        float bottom = camCenter.y - camHeight / 2f;
        float top = camCenter.y + camHeight / 2f;

        Vector2 topLeft = new(left, top);
        Vector2 topRight = new(right, top);
        Vector2 bottomRight = new(right, bottom);
        Vector2 bottomLeft = new(left, bottom);

        (Vector2, Vector2)[] edges =
        {
            (topLeft, topRight),
            (topRight, bottomRight),
            (bottomRight, bottomLeft),
            (bottomLeft, topLeft),
        };

        List<Vector2> intersections = new();

        foreach (var edge in edges)
        {
            Vector2? intersection = LineLineIntersection(a, b, edge.Item1, edge.Item2);
            if (intersection.HasValue)
                intersections.Add(intersection.Value);
        }

        Vector2 dir = (b - a).normalized;
        return intersections
            .Distinct()
            .OrderBy(p => Vector2.Dot(p - a, dir))
            .Take(2)
            .ToList();
    }

    private Vector2? LineLineIntersection(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        Vector2 r = p2 - p1;
        Vector2 s = q2 - q1;

        float rxs = r.x * s.y - r.y * s.x;
        if (Mathf.Approximately(rxs, 0f))
            return null;

        Vector2 qp = q1 - p1;
        float t = (qp.x * s.y - qp.y * s.x) / rxs;
        float u = (qp.x * r.y - qp.y * r.x) / rxs;

        if (u >= 0f && u <= 1f)
            return p1 + t * r;

        return null;
    }
}
