using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public enum SevenPointPartitionerPartType
{
    Point = 0,
}

public class SevenPointPartitioner : MonoBehaviour
{
    public List<Point> points;
    public GameObject linePrefab;
    public static readonly float lineVisibleThickness = 0.1f;
    readonly float basePointColliderThickness = 0.1f;
    float pointColliderThickness;

    public List<Line> lines;

    private Point closestPoint;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false;
    private bool isCameraDragging = false;

    private Vector3 lastMousePosition;

    private void Awake()
    {
        foreach (var point in points)
        {
            point.parentSevenPointPartitioner = this;
        }

        foreach (var line in lines)
        {
            line.parentSevenPointPartitioner = this;
        }

        UpdateLines();
        UpdateSelectionRadius();
    }

    public void UpdateLines()
    {
        lines[0].inputPoint1.position = points[0].transform.position;
        lines[0].inputPoint2.position = points[1].transform.position;
        lines[1].inputPoint1.position = points[2].transform.position;
        lines[1].inputPoint2.position = points[3].transform.position;
        lines[2].inputPoint1.position = points[4].transform.position;
        lines[2].inputPoint2.position = points[5].transform.position;
    }

    public void MovePoint(Point point, Vector3 position)
    {
        if (point != null)
        {
            point.transform.position = position;
            UpdateLines();
        }
    }

    public (Point closestPoint, float closestDistance) FindClosestPoint(Vector3 position)
    {
        Point closest = null;
        float minDistance = float.MaxValue;

        foreach (Point point in points)
        {
            float distance = Vector3.Distance(position, point.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = point;
            }
        }

        return (closestPoint: closest, closestDistance: minDistance);
    }

    public (Line closestLine, float closestDistance)? FindClosestLine(Vector3 inputPosition)
    {
        Edge3D[] edges = lines.Select(line => new Edge3D("", line.endPoint1.position, line.endPoint2.position)).ToArray();

        // Check _EDGES_ for nearest point
        Vector3 nearestPointOnEdge = Vector3.zero;
        float nearestPointDistanceOnEdge = float.PositiveInfinity;
        bool nearestPointFoundOnEdge = false;
        int? nearestEdgeIndex = null;
        for (int i = 0; i < edges.Count(); i++)
        {
            Edge3D edge = edges[i];
            Geometry.BasisDir projectionDir = Geometry.BasisDir.Y;
            Maybe<Geometry.BasisDir> maybePreferredProjectionDir = edge.GetBestProjectionDir();
            if (maybePreferredProjectionDir.exists)
            {
                projectionDir = maybePreferredProjectionDir.value;
            }
            else
            {
                Debug.LogError("Degenerate edge found.");
            }

            Vector3 inputPointPosToEdge = Geometry.NearestPointOfLineFromPoints(inputPosition, edge.p1, edge.p2);
            Interval projectedInterval = edge.ProjectToDir(projectionDir);

            float inputPointPosToDir = Geometry.ProjectToDir(inputPointPosToEdge, projectionDir);

            bool edgeContainsInputPointPosToEdge = projectedInterval.Contains(inputPointPosToDir);

            if (edgeContainsInputPointPosToEdge)
            {
                float thisPointDistance = (inputPointPosToEdge - inputPosition).magnitude;

                if (nearestPointFoundOnEdge)
                {
                    if (thisPointDistance < nearestPointDistanceOnEdge)
                    {
                        nearestPointOnEdge = inputPointPosToEdge;
                        nearestPointDistanceOnEdge = thisPointDistance;
                        nearestEdgeIndex = i;
                    }
                }
                else
                {
                    nearestPointFoundOnEdge = true;
                    nearestPointOnEdge = inputPointPosToEdge;
                    nearestPointDistanceOnEdge = thisPointDistance;
                    nearestEdgeIndex = i;
                }
            }
        }

        if (nearestEdgeIndex.HasValue)
        {
            return (closestLine: lines[nearestEdgeIndex.Value], closestDistance: nearestPointDistanceOnEdge);
        }
        else
        {
            return null;
        }
    }

    float scrollAmount = 0f;
    private void Update()
    {
        if (isDragging) return;

        var pointerPos = Camera.main.ScreenToWorldPoint(Input.mousePosition) + Vector3.forward * 10;

        var closestPointData = FindClosestPoint(pointerPos);
        var closestPointDistance = closestPointData.closestDistance;

        if (closestPointDistance < pointColliderThickness)
        {
            closestPoint = closestPointData.closestPoint;
        }
        else
        {
            closestPoint = null;
        }

        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            // Handle camera zooming
            float scrollDelta = scrollInput;
            scrollAmount -= scrollDelta;
            scrollAmount = Mathf.Clamp(scrollAmount, -5f, 5f);
            Camera.main.orthographicSize = Mathf.Pow(5, 1+scrollAmount/5f);
            Camera.main.orthographicSize = Mathf.Clamp(Camera.main.orthographicSize, -50f, 50f);
        }

        // Handle camera dragging
        if (Input.GetMouseButtonDown(0) && closestPoint == null)
        {
            isCameraDragging = true;
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isCameraDragging = false;
        }

        if (isCameraDragging)
        {
            Vector3 delta = Camera.main.ScreenToWorldPoint(Input.mousePosition) - Camera.main.ScreenToWorldPoint(lastMousePosition);
            Camera.main.transform.position -= delta;
            lastMousePosition = Input.mousePosition;
        }

        // Reset highlights
        foreach (Point point in points)
        {
            point.Highlight(false);
        }

        // Set latest closest highlight
        if (closestPoint != null)
        {
            closestPoint.Highlight(true);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true; // Set the dragging flag to true
        if (closestPoint != null)
        {
            latestDraggedPartType = SevenPointPartitionerPartType.Point;
            OnBeginDragPoint(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (latestDraggedPartType == SevenPointPartitionerPartType.Point)
        {
            OnDragPoint(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false; // Reset the dragging flag to false
        if (latestDraggedPartType == SevenPointPartitionerPartType.Point)
        {
            OnEndDragPoint(eventData);
        }
    }

    public void OnBeginDragPoint(PointerEventData eventData)
    {

    }

    public void OnDragPoint(PointerEventData eventData)
    {
        if (closestPoint != null)
        {
            // Convert screen position to world position
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0; // Ensure the point stays on the z = 0 plane

            // Move the point via the SevenPointPartitionerManager
            MovePoint(closestPoint, worldPoint);
        }
    }

    public void OnEndDragPoint(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestPoint = null;
    }

    private Vector3 ScreenToWorldPoint(Vector2 screenPosition)
    {
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -Camera.main.transform.position.z);
        return Camera.main.ScreenToWorldPoint(screenPoint);
    }

    public void StartDragging(SevenPointPartitionerPartType partType)
    {
        latestDraggedPartType = partType;
        isDragging = true;
    }

    public void StopDragging()
    {
        isDragging = false;
    }

    private void UpdateSelectionRadius()
    {
        float zoomFactor = Camera.main.orthographicSize;
        pointColliderThickness = basePointColliderThickness * zoomFactor;
    }
}
