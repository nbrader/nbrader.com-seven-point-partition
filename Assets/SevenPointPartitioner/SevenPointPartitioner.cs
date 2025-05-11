using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public enum SevenPointPartitionerPartType
{
    Point = 0,
    HalfLine,
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
    public GameObject halfLinePrefab;  // Prefab for a half line
    readonly float lineVisibleThickness = 0.1f;
    readonly float lineColliderThicknessSize = 10f;
    readonly float basePointColliderThickness = 0.1f; // Base collider thickness for points
    readonly float baseLineColliderThickness = 0.1f; // Base collider thickness for lines

    private HalfLine[] halfLines;

    private Point closestPoint;
    private HalfLine closestHalfLine;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false; // Flag to indicate if dragging is in progress
    private bool isCameraDragging = false; // Flag to indicate if camera dragging is in progress

    private Vector3 lastMousePosition; // To store the last mouse position for camera dragging

    float pointColliderThickness; // In fact isn't the collider thickness but that's how it appears provided it's smaller than the true collider thickness.
    float lineColliderThickness;
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

        // Initialize halfLines arrays
        halfLines = new HalfLine[points.Count * 2];

        // Create Line objects
        for (int i = 0; i < points.Count; i++)
        {
            // Create half lines
            GameObject halfLineObj1 = Instantiate(halfLinePrefab, transform);
            GameObject halfLineObj2 = Instantiate(halfLinePrefab, transform);
            halfLines[i * 2] = halfLineObj1.GetComponent<HalfLine>();
            halfLines[i * 2 + 1] = halfLineObj2.GetComponent<HalfLine>();

            // Assign DraggableHalfLine components and their references
            HalfLine halfLine1 = halfLineObj1.GetComponent<HalfLine>();
            HalfLine halfLine2 = halfLineObj2.GetComponent<HalfLine>();

            // Ensure half lines are created properly with the necessary components
            if (halfLine1 != null && halfLine2 != null)
            {
                halfLine1.Initialize(this, points[i], points[Maths.mod(i + 1, points.Count)], points[Maths.mod(i + 2, points.Count)], points[Maths.mod(i + 3, points.Count)]);
                halfLine2.Initialize(this, points[i], points[Maths.mod(i - 1, points.Count)], points[Maths.mod(i - 2, points.Count)], points[Maths.mod(i - 3, points.Count)]);
            }
            else
            {
                Debug.LogError("DraggableHalfLine component missing on half line prefab.");
            }
        }

        UpdateLines();
        UpdateSelectionRadius();
    }

    public void UpdateLines()
    {
        if (points == null || points.Count < 2) return;

        for (int i = 0; i < points.Count; i++)
        {
            int nextIndex = (i + 1) % points.Count;
            int prevIndex = (i - 1 + points.Count) % points.Count;
            Transform currentPoint = points[i].transform;
            Transform nextPoint = points[nextIndex].transform;
            Transform prevPoint = points[prevIndex].transform;

            // Calculate the position and scale for the full line
            Vector3 direction = nextPoint.position - currentPoint.position;

            // Calculate and update half line positions, rotations, and scales
            Vector3 nextDirection = (nextPoint.position - currentPoint.position) / 2;
            Vector3 prevDirection = (prevPoint.position - currentPoint.position) / 2;
            float nextHalfDistance = nextDirection.magnitude;
            float prevHalfDistance = prevDirection.magnitude;

            halfLines[i * 2].transform.position = currentPoint.position;
            halfLines[i * 2].transform.right = nextDirection;
            halfLines[i * 2].transform.localScale = new Vector3(nextHalfDistance, lineVisibleThickness, 1);
            halfLines[i * 2].GetComponent<BoxCollider2D>().size = new Vector2(1, lineColliderThicknessSize);

            halfLines[i * 2 + 1].transform.position = currentPoint.position;
            halfLines[i * 2 + 1].transform.right = prevDirection;
            halfLines[i * 2 + 1].transform.localScale = new Vector3(prevHalfDistance, lineVisibleThickness, 1);
            halfLines[i * 2 + 1].GetComponent<BoxCollider2D>().size = new Vector2(1, lineColliderThicknessSize);
        }
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

    public (HalfLine closestHalfLine, float closestDistance)? FindClosestHalfLine(Vector3 inputPosition)
    {
        Edge3D[] edges = halfLines.Select(line => new Edge3D("", line.pivotPoint.transform.position, (line.pivotPoint.transform.position + line.adjacentPoint.transform.position) / 2)).ToArray();

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
            return (closestHalfLine: halfLines[nearestEdgeIndex.Value], closestDistance: nearestPointDistanceOnEdge);
        }
        else
        {
            return null;
        }
    }

    // This update function could be made to calculate less often.
    Vector3 pivotBeforeDrag = Vector3.zero;
    Vector3 adjBeforeDrag = Vector3.zero;
    Vector3 altBeforeDrag = Vector3.zero;
    Vector3 oppBeforeDrag = Vector3.zero;
    float pivotToAdjDist;
    float adjToOppDist;
    float oppToAltDist;
    float altToPivotDist;

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
            closestHalfLine = null;
        }
        else
        {
            var closestHalfLineData = FindClosestHalfLine(pointerPos);
            if (closestHalfLineData.HasValue && closestHalfLineData.Value.closestDistance <= lineColliderThickness)
            {
                closestPoint = null;
                closestHalfLine = closestHalfLineData.Value.closestHalfLine;
            }
            else
            {
                closestPoint = null;
                closestHalfLine = null;
            }
        }

        // Handle mouse wheel input to extend or contract the hovered half line
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (closestHalfLine != null && Mathf.Abs(scrollInput) > 0.01f)
        {
            AdjustHalfLineLength(closestHalfLine, scrollInput);
        }
        else if (Mathf.Abs(scrollInput) > 0.01f)
        {
            // Handle camera zooming
            float scrollDelta = scrollInput;
            scrollAmount -= scrollDelta;
            scrollAmount = Mathf.Clamp(scrollAmount, -5f, 5f);
            Camera.main.orthographicSize = Mathf.Pow(5, 1+scrollAmount/5f);
            Camera.main.orthographicSize = Mathf.Clamp(Camera.main.orthographicSize, -50f, 50f);
        }

        // Handle camera dragging
        if (Input.GetMouseButtonDown(0) && closestPoint == null && closestHalfLine == null)
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
        foreach (HalfLine halfLine in halfLines)
        {
            halfLine.Highlight(false);
        }

        // Set latest closest highlight
        if (closestPoint != null)
        {
            closestPoint.Highlight(true);
        }

        if (closestHalfLine != null)
        {
            closestHalfLine.Highlight(true);

            pivotBeforeDrag = closestHalfLine.pivotPoint.transform.position;
            adjBeforeDrag = closestHalfLine.adjacentPoint.transform.position;
            altBeforeDrag = closestHalfLine.alternativeAdjacentPoint.transform.position;
            oppBeforeDrag = closestHalfLine.oppositePoint.transform.position;

            // Calculate angle ranges to display to user
            pivotToAdjDist = (adjBeforeDrag - pivotBeforeDrag).magnitude;
            adjToOppDist = (oppBeforeDrag - adjBeforeDrag).magnitude;
            oppToAltDist = (altBeforeDrag - oppBeforeDrag).magnitude;
            altToPivotDist = (pivotBeforeDrag - altBeforeDrag).magnitude;
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
        else if (closestHalfLine != null)
        {
            latestDraggedPartType = SevenPointPartitionerPartType.HalfLine;
            OnBeginDragHalfLine(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (latestDraggedPartType == SevenPointPartitionerPartType.Point)
        {
            OnDragPoint(eventData);
        }
        else
        {
            OnDragHalfLine(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false; // Reset the dragging flag to false
        if (latestDraggedPartType == SevenPointPartitionerPartType.Point)
        {
            OnEndDragPoint(eventData);
        }
        else
        {
            OnEndDragHalfLine(eventData);
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

    public void OnBeginDragHalfLine(PointerEventData eventData)
    {
        lastAdj = adjBeforeDrag;
        lastOpp = oppBeforeDrag;
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

    Vector3 lastAdj = Vector3.zero;
    Vector3 lastOpp = Vector3.zero;
    public void OnDragHalfLine(PointerEventData eventData)
    {
        if (closestHalfLine != null)
        {
            // Calculate new SevenPointPartitioner position
            // First calculate possible positions for opposite point
            float minDistViaOpp = Mathf.Abs(adjToOppDist - oppToAltDist);
            float maxDistViaOpp = adjToOppDist + oppToAltDist;
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0; // Ensure the point stays on the z = 0 plane
            Vector3 direction = (worldPoint - closestHalfLine.pivotPoint.transform.position).normalized;
            Vector3 adjTarget = pivotBeforeDrag + direction * Vector3.Distance(pivotBeforeDrag, adjBeforeDrag);
            Vector3 adjTargetToAlt = altBeforeDrag - adjTarget;
            float adjTargetToAltDist = adjTargetToAlt.magnitude;
            var (solutionsExist, oppTarget_1, oppTarget_2) = GetPossibleOppPositions(adjTargetToAltDist, minDistViaOpp, maxDistViaOpp, oppToAltDist, adjToOppDist, adjTarget, adjTargetToAlt);

            // If solutions don't exist then show SevenPointPartitioner before dragging
            // If solutions do exist, pick one closest to previously calculated for continuity of motion
            Vector3 newOpp;
            if (!solutionsExist)
            {
                adjTarget = lastAdj;
                newOpp = lastOpp;
            }
            else
            {
                newOpp = oppTarget_1;

                float oppTarget_1_distFromLast = (oppTarget_1 - lastOpp).magnitude;
                float oppTarget_2_distFromLast = (oppTarget_2 - lastOpp).magnitude;
                if (oppTarget_2_distFromLast <= oppTarget_1_distFromLast)
                {
                    newOpp = oppTarget_2;
                }
            }

            lastAdj = adjTarget;
            lastOpp = newOpp;
            closestHalfLine.adjacentPoint.transform.position = adjTarget;
            closestHalfLine.oppositePoint.transform.position = newOpp;
            UpdateLines();
        }
    }

    public void OnEndDragHalfLine(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestHalfLine = null;
    }

    private Vector3 ScreenToWorldPoint(Vector2 screenPosition)
    {
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -Camera.main.transform.position.z);
        return Camera.main.ScreenToWorldPoint(screenPoint);
    }

    private void AdjustHalfLineLength(HalfLine halfLine, float scrollAmount)
    {
        float lengthChange = scrollAmount * 0.1f; // Adjust the 0.1f to control the rate of length change
        Vector3 direction = (halfLine.adjacentPoint.transform.position - halfLine.pivotPoint.transform.position).normalized;
        halfLine.adjacentPoint.transform.position += direction * lengthChange;

        // Update all lines after changing the length
        UpdateLines();
    }

    public void StartDragging(SevenPointPartitionerPartType partType)
    {
        latestDraggedPartType = partType;
        isDragging = true;

        pivotBeforeDrag = closestHalfLine.pivotPoint.transform.position;
        adjBeforeDrag = closestHalfLine.adjacentPoint.transform.position;
        altBeforeDrag = closestHalfLine.alternativeAdjacentPoint.transform.position;
        oppBeforeDrag = closestHalfLine.oppositePoint.transform.position;
        pivotToAdjDist = Vector3.Distance(pivotBeforeDrag, adjBeforeDrag);
        adjToOppDist = Vector3.Distance(adjBeforeDrag, oppBeforeDrag);
        oppToAltDist = Vector3.Distance(altBeforeDrag, oppBeforeDrag);
        altToPivotDist = Vector3.Distance(altBeforeDrag, pivotBeforeDrag);
    }

    public void StopDragging()
    {
        isDragging = false;
    }

    public void RestorePointsToBeforeDrag()
    {
        closestHalfLine.pivotPoint.transform.position = pivotBeforeDrag;
        closestHalfLine.adjacentPoint.transform.position = adjBeforeDrag;
        closestHalfLine.alternativeAdjacentPoint.transform.position = altBeforeDrag;
        closestHalfLine.oppositePoint.transform.position = oppBeforeDrag;
    }

    private void UpdateSelectionRadius()
    {
        float zoomFactor = Camera.main.orthographicSize;
        pointColliderThickness = basePointColliderThickness * zoomFactor;
        lineColliderThickness = baseLineColliderThickness * zoomFactor;
    }
}
