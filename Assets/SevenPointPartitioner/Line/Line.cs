using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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

    public bool IsVisible
    {
        get { return lineTransform.gameObject.activeInHierarchy; }
        set { lineTransform.gameObject.SetActive(value); }
    }

    private void Update()
    {
        UpdateLineEndpoints();

        Vector2 disp = Maths.ProjectVec3DownZ(endPoint2.position - endPoint1.position);
        float degrees = Maths.Rad2Deg(Maths.AngleFromVec2(disp));
        float dist = disp.magnitude;

        lineTransform.position = endPoint1.position;
        lineTransform.rotation = Quaternion.Euler(0f, 0f, degrees);
        lineTransform.transform.localScale = new Vector3(dist, SevenPointPartitioner.lineVisibleThickness, 1);

        spriteRenderer.color = colour;
    }

    public void UpdateLineEndpoints()
    {
        Vector2 p1 = inputPoint1.position;
        Vector2 p2 = inputPoint2.position;

        List<Vector2> intersections = GetLineCameraViewIntersections(p1, p2);

        if (intersections.Count >= 2)
        {
            endPoint1.position = new Vector3(intersections[0].x, intersections[0].y, 0);
            endPoint2.position = new Vector3(intersections[1].x, intersections[1].y, 0);
        }
        else
        {
            // Fallback in case intersections are invalid
            endPoint1.position = p1;
            endPoint2.position = p2;
        }
    }

    private List<Vector2> GetLineCameraViewIntersections(Vector2 a, Vector2 b)
    {
        if (a == b)
        {
            b = a + Vector2.right;
        }

        Camera cam = Camera.main;
        float camHeight = 2f * cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;
        Vector2 camCenter = cam.transform.position;

        // Double the width and height
        camWidth *= 2f;
        camHeight *= 2f;

        float left = camCenter.x - camWidth / 2f;
        float right = camCenter.x + camWidth / 2f;
        float bottom = camCenter.y - camHeight / 2f;
        float top = camCenter.y + camHeight / 2f;

        Vector2 topLeft = new Vector2(left, top);
        Vector2 topRight = new Vector2(right, top);
        Vector2 bottomRight = new Vector2(right, bottom);
        Vector2 bottomLeft = new Vector2(left, bottom);

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
            {
                intersections.Add(intersection.Value);
            }
        }

        Vector2 dir = (b - a).normalized;
        return intersections
            .Distinct()
            .OrderBy(p => Vector2.Dot(p - a, dir))
            .Skip(1)
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

        float t = ((q1 - p1).x * s.y - (q1 - p1).y * s.x) / rxs;
        return p1 + t * r;
    }
}
