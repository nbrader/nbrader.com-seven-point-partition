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

    List<Line> lines;

    private int? closestPointIndex;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false;
    private bool isCameraDragging = false;

    private Vector3 lastMousePosition;

    private List<Point> AllPoints => points
    .Concat(debugLinePairs.Select(p => p.pointA))
    .Concat(debugLinePairs.Select(p => p.pointB))
    .Where(p => p != null)
    .Distinct()
    .ToList();

    private void Awake()
    {
        foreach (var point in points)
        {
            point.parentSevenPointPartitioner = this;
        }

        // Also set the parent for points used only in debugLinePairs
        foreach (var pair in debugLinePairs)
        {
            if (pair.pointA != null && !points.Contains(pair.pointA))
                pair.pointA.parentSevenPointPartitioner = this;

            if (pair.pointB != null && !points.Contains(pair.pointB))
                pair.pointB.parentSevenPointPartitioner = this;

            pair.pointA.normalColour = Color.white;
            pair.pointB.normalColour = Color.white;
        }

        InitializeLinesFromPoints();
        InitializeDebugLines();
        UpdateSelectionRadius();
    }

    List<Line> debugLines;

    private static readonly Color[] debugColors = new Color[]
    {
    Color.magenta, Color.cyan, Color.yellow,
    new Color(1f, 0.5f, 0f), // orange
    new Color(0.5f, 0f, 1f), // violet
    new Color(0f, 1f, 0.5f), // spring green
    new Color(0.5f, 1f, 0f), // chartreuse
    new Color(1f, 0f, 0.5f), // rose
    new Color(0f, 0.5f, 1f), // azure
                             // Add more if needed
    };

    [System.Serializable]
    public struct PointPair
    {
        public Point pointA;
        public Point pointB;
    }

    public List<PointPair> debugLinePairs;

    private void InitializeDebugLines()
    {
        debugLines = new List<Line>();
        int colorIndex = 0;

        foreach (var pair in debugLinePairs)
        {
            if (pair.pointA == null || pair.pointB == null) continue;

            GameObject lineObj = Instantiate(linePrefab, Vector3.zero, Quaternion.identity);
            Line line = lineObj.GetComponent<Line>();

            line.inputPoint1 = pair.pointA.transform;
            line.inputPoint2 = pair.pointB.transform;
            line.parentSevenPointPartitioner = this;

            Color debugColor = debugColors[colorIndex % debugColors.Length];
            debugColor.a = 0.9f;
            line.colour = debugColor;
            colorIndex++;

            line.ShouldBeVisible += _ => true;

            debugLines.Add(line);
        }
    }

    private void InitializeLinesFromPoints()
    {
        lines = new List<Line>();

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                GameObject lineObj = Instantiate(linePrefab, Vector3.zero, Quaternion.identity);
                Line line = lineObj.GetComponent<Line>();

                line.inputPoint1 = points[i].transform;
                line.inputPoint2 = points[j].transform;
                line.parentSevenPointPartitioner = this;

                // Assign debug color
                Color color = Color.green;
                color.a = 0.5f;
                line.colour = color;

                // Register visibility rule
                line.ShouldBeVisible += ShouldShowLine;

                lines.Add(line);
            }
        }
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
    }

    public void MovePoint(int pointIndex, Vector3 targetPosition)
    {
        points[pointIndex].Position = targetPosition;
    }

    public (int closestPoint, float closestDistance) FindClosestPoint(Vector3 position)
    {
        List<Point> allPoints = AllPoints;
        Point point = allPoints[0];
        float minDistance = Vector3.Distance(position, point.transform.position);
        int closest = 0;

        for (int i = 1; i < allPoints.Count; i++)
        {
            point = allPoints[i];
            float distance = Vector3.Distance(position, point.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = i;
            }
        }

        return (closestPoint: closest, closestDistance: minDistance);
    }

    private int? closestPointIndexInAllPoints;

    float scrollAmount = 0f;
    private void Update()
    {
        CheckForPossibleCentres();

        if (isDragging) return;

        var pointerPos = Camera.main.ScreenToWorldPoint(Input.mousePosition) + Vector3.forward * 10;

        var closestPointData = FindClosestPoint(pointerPos);
        closestPointIndexInAllPoints = closestPointData.closestPoint;

        if (closestPointData.closestDistance < pointColliderThickness)
        {
            closestPointIndexInAllPoints = closestPointData.closestPoint;
        }
        else
        {
            closestPointIndexInAllPoints = null;
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
        foreach (Point point in AllPoints)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            AllPoints[closestPointIndexInAllPoints.Value].Highlight(true);
    }

    private bool ShouldShowLine(Line line)
    {
        Vector2 a = line.inputPoint1.position;
        Vector2 b = line.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;

        foreach (Point p in points)
        {
            if (p.transform == line.inputPoint1 || p.transform == line.inputPoint2)
                continue;

            Vector2 pt = p.transform.position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            if (Mathf.Approximately(cross, 0f))
                continue; // Point is on the line

            if (cross > 0)
                leftCount++;
            else
                rightCount++;
        }

        return leftCount <= 3 && rightCount <= 3;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true; // Set the dragging flag to true
        if (closestPointIndexInAllPoints != null)
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
        if (closestPointIndexInAllPoints != null)
        {
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0;
            AllPoints[closestPointIndexInAllPoints.Value].Position = worldPoint;
        }
    }

    public void OnEndDragPoint(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestPointIndexInAllPoints = null;
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
