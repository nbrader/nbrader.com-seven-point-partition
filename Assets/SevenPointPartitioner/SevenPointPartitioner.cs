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
    public List<Line> debugLines;

    private int? closestPointIndex;
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
        //lines[0].inputPoint1.position = points[7].transform.position;
        //lines[0].inputPoint2.position = points[8].transform.position;
        //lines[1].inputPoint1.position = points[9].transform.position;
        //lines[1].inputPoint2.position = points[10].transform.position;
        //lines[2].inputPoint1.position = points[11].transform.position;
        //lines[2].inputPoint2.position = points[12].transform.position;

        //debugLines[0].inputPoint1.position = points[0].transform.position;
        //debugLines[1].inputPoint1.position = points[0].transform.position;
        //debugLines[2].inputPoint1.position = points[0].transform.position;
        //debugLines[3].inputPoint1.position = points[0].transform.position;
        //debugLines[4].inputPoint1.position = points[0].transform.position;
        //debugLines[5].inputPoint1.position = points[0].transform.position;
        //debugLines[0].inputPoint2.position = points[1].transform.position;
        //debugLines[1].inputPoint2.position = points[2].transform.position;
        //debugLines[2].inputPoint2.position = points[3].transform.position;
        //debugLines[3].inputPoint2.position = points[4].transform.position;
        //debugLines[4].inputPoint2.position = points[5].transform.position;
        //debugLines[5].inputPoint2.position = points[6].transform.position;
    }

    private void CheckForPossibleCentres()
    {
        List<int> sortedIndices = Enumerable.Range(0, points.Count).ToList();

        // Sort indices by Y descending, then X ascending, then Z ascending
        sortedIndices.Sort((i, j) =>
        {
            Vector3 posA = points[i].Position;
            Vector3 posB = points[j].Position;

            if (!Mathf.Approximately(posA.y, posB.y))
                return posB.y.CompareTo(posA.y); // Descending Y

            if (!Mathf.Approximately(posA.x, posB.x))
                return posA.x.CompareTo(posB.x); // Ascending X

            return posA.z.CompareTo(posB.z); // Ascending Z
        });

        List<int> validCentreIndices = new List<int>();

        foreach (int index in sortedIndices)
        {
            if (CanBeCentre(index))
            {
                validCentreIndices.Add(index);
            }
        }

        Debug.Log("Possible centres: " + string.Join(", ", validCentreIndices));
    }

    private bool CanBeCentre(int pointIndex)
    {
        Vector3 candidate = points[pointIndex].Position;
        List<(float projectedY, bool isLeft)> projections = new List<(float, bool)>();

        for (int i = 0; i < points.Count; i++)
        {
            if (i == pointIndex) continue;

            Vector3 other = points[i].Position;
            Vector3 direction = other - candidate;

            if (Mathf.Approximately(direction.x, 0f))
                continue; // Skip vertical lines which do not intersect the projection line uniquely

            float t = 10f / direction.x; // Projection to x = candidate.x + 10
            float projectedY = candidate.y + direction.y * t;
            bool isLeft = other.x < candidate.x;

            projections.Add((projectedY, isLeft));
        }

        // Sort by projected Y
        projections.Sort((a, b) => a.projectedY.CompareTo(b.projectedY));

        int count = projections.Count;

        // Check for 3 consecutive same-side values (regular)
        for (int i = 0; i <= count - 3; i++)
        {
            bool side1 = projections[i].isLeft;
            bool side2 = projections[i + 1].isLeft;
            bool side3 = projections[i + 2].isLeft;

            if (side1 == side2 && side2 == side3)
                return false;
        }

        // Check wraparound triplets with side flipping
        if (count >= 3)
        {
            // Wrap: last, first, second
            bool side1 = !projections[count - 1].isLeft; // flipped
            bool side2 = projections[0].isLeft;
            bool side3 = projections[1].isLeft;
            if (side1 == side2 && side2 == side3)
                return false;

            // Wrap: second-last, last, first
            side1 = projections[count - 2].isLeft;
            side2 = projections[count - 1].isLeft;
            side3 = !projections[0].isLeft; // flipped
            if (side1 == side2 && side2 == side3)
                return false;
        }

        return true;
    }

    public void MovePoint(int pointIndex, Vector3 targetPosition)
    {
        Vector3 corrected = ResolveConstraints(pointIndex, targetPosition);
        points[pointIndex].Position = corrected;
        UpdateLines();
    }

    private Vector3 ResolveConstraints(int movingIndex, Vector3 target)
    {
        const float a = 0.05f;     // initial radius
        const float b = 0.25f;     // exponential growth rate
        const float angleStep = 0.1f;

        // First attempt: use target directly
        if (!HitsAnyLine(movingIndex, target))
            return target;

        int i = 0;
        while (true)
        {
            float angle = i * angleStep;
            float r = a * Mathf.Exp(b * angle);
            float xOffset = r * Mathf.Cos(angle);
            float zOffset = r * Mathf.Sin(angle);

            Vector3 attempt = new Vector3(target.x + xOffset, target.y, target.z + zOffset);

            if (!HitsAnyLine(movingIndex, attempt))
                return attempt;

            i++;
        }
    }

    private bool HitsAnyLine(int movingIndex, Vector3 position)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (i == movingIndex) continue;
            Vector3 pi = points[i].Position;

            // Check vertical line: same x and z
            if (Mathf.Approximately(position.x, pi.x) &&
                Mathf.Approximately(position.z, pi.z))
                return true;

            for (int j = i + 1; j < points.Count; j++)
            {
                if (j == movingIndex) continue;
                Vector3 pj = points[j].Position;

                if (IsOnLine(pi, pj, position))
                    return true;
            }
        }

        return false;
    }

    private bool IsOnLine(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        Vector3 ap = p - a;
        Vector3 cross = Vector3.Cross(ab, ap);
        return cross.sqrMagnitude < 1e-8f;
    }

    public (int closestPoint, float closestDistance) FindClosestPoint(Vector3 position)
    {
        Point point = points[0];
        float minDistance = Vector3.Distance(position, point.transform.position);
        int closest = 0;

        for (int i = 1; i < points.Count; i++)
        {
            point = points[i];
            float distance = Vector3.Distance(position, point.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = i;
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
        CheckForPossibleCentres();

        if (isDragging) return;

        var pointerPos = Camera.main.ScreenToWorldPoint(Input.mousePosition) + Vector3.forward * 10;

        var closestPointData = FindClosestPoint(pointerPos);
        var closestPointDistance = closestPointData.closestDistance;

        if (closestPointDistance < pointColliderThickness)
        {
            closestPointIndex = closestPointData.closestPoint;
        }
        else
        {
            closestPointIndex = null;
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
        if (Input.GetMouseButtonDown(0) && closestPointIndex == null)
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
        if (closestPointIndex != null)
        {
            points[closestPointIndex.Value].Highlight(true);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true; // Set the dragging flag to true
        if (closestPointIndex != null)
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
        if (closestPointIndex != null)
        {
            // Convert screen position to world position
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0; // Ensure the point stays on the z = 0 plane

            // Move the point via the SevenPointPartitionerManager
            MovePoint(closestPointIndex.Value, worldPoint);
        }
    }

    public void OnEndDragPoint(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestPointIndex = null;
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
