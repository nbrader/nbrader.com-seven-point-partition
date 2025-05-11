using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public enum SevenPointPartitionerPartType
{
    Point = 0,
}

/// <summary>
/// There is at most 1 point out of a pair of opposing points which has angle which would break the
/// constraint of being less than min distance imposed by the opposing lines and similarly for being
/// more than max distance imposed by the opposing lines.
/// 
/// Proof sketch:
/// For one, a pair of adjacent lines has a sum of lengths either less than, equal to or more than that
/// of the opposing pair of lines and in each of those cases the opposing lines therefore have a sum of
/// lengths that is more than, equal to or less than the first pair respectively.
/// 
/// Also, when an adjacent pair of lines are at a limit of an angular range, this can only
/// be because the other pair have reached a minim or maximum, which occur when they are collinear.
/// The fact that they've reached a collinear positioned demonstrates that they didn't themselves have an
/// angular restriction there and may pass through continuously the other half of their angular range.
/// 
/// Conversely, any point which allows a collinear angle is hitting a minimum or maximum distance of
/// endpoints and so if the opposing point isn't also hitting a collinear angle when the first point
/// does, then it won't for any angle because other angles can only result in less extreme distances
/// of endpoints and therefore there will be a (possibly zero) range of no solutions for the other
/// point beyond the angle it reached at that extreme.
/// </summary>
public class SevenPointPartitioner : MonoBehaviour
{
    public List<Point> points;
    public GameObject linePrefab;  // Prefab for a half line
    public static readonly float lineVisibleThickness = 0.1f;
    readonly float basePointColliderThickness = 0.1f; // Base collider thickness for points

    public List<Line> lines;

    private Point closestPoint;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false; // Flag to indicate if dragging is in progress
    private bool isCameraDragging = false; // Flag to indicate if camera dragging is in progress

    private Vector3 lastMousePosition; // To store the last mouse position for camera dragging

    float pointColliderThickness; // In fact isn't the collider thickness but that's how it appears provided it's smaller than the true collider thickness.

    private void Awake()
    {
        if (points == null || points.Count != 7)
        {
            Debug.LogError("7 points are required.");
            return;
        }

        foreach (var point in points)
        {
            point.parentSevenPointPartitioner = this;
        }

        if (lines == null || lines.Count != 3)
        {
            Debug.LogError("3 points are required.");
            return;
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
        lines[0].endPoint1.position = points[0].transform.position;
        lines[0].endPoint2.position = points[1].transform.position;
        lines[1].endPoint1.position = points[2].transform.position;
        lines[1].endPoint2.position = points[3].transform.position;
        lines[2].endPoint1.position = points[4].transform.position;
        lines[2].endPoint2.position = points[5].transform.position;
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

    public (bool solutionsExist, Vector3 oppPosition1, Vector3 oppPosition2) GetPossibleOppPositions(float adjTargetToAltDist, float minDistViaOpp, float maxDistViaOpp, float oppToAltDist, float adjToOppDist, Vector3 adjTarget, Vector3 adjTargetToAlt)
    {
        bool solutionsExist = adjTargetToAltDist >= minDistViaOpp && adjTargetToAltDist <= maxDistViaOpp;

        Vector3 oppTarget_1 = Vector3.zero;
        Vector3 oppTarget_2 = Vector3.zero;
        if (solutionsExist)
        {
            var intersectionData = Maths.CircleCircleIntersectionXAndY(adjTargetToAltDist, oppToAltDist, adjToOppDist);

            float x = intersectionData.IntersectionDistanceFromOriginAlongLineConnectingOrigins;
            float y = intersectionData.HalfSeparationOfIntersections;
            oppTarget_1 = adjTarget
                          + adjTargetToAlt.normalized * x
                          + new Vector3(-adjTargetToAlt.normalized.y, adjTargetToAlt.normalized.x, 0f) * y;
            oppTarget_2 = adjTarget
                          + adjTargetToAlt.normalized * x
                          - new Vector3(-adjTargetToAlt.normalized.y, adjTargetToAlt.normalized.x, 0f) * y;
        }

        return (solutionsExist: solutionsExist, oppPosition1: oppTarget_1, oppPosition2: oppTarget_2);
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
