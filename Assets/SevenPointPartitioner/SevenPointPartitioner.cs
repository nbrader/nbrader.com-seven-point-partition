using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public enum SevenPointPartitionerPartType
{
    Point = 0,
}

[System.Serializable]
public struct HalfPlaneTriple
{
    public HalfPlane halfPlaneA;
    public HalfPlane halfPlaneB;
    public HalfPlane halfPlaneC;

    public HalfPlaneTriple(HalfPlane a, HalfPlane b, HalfPlane c)
    {
        halfPlaneA = a;
        halfPlaneB = b;
        halfPlaneC = c;
    }
}

public class SevenPointPartitioner : MonoBehaviour
{
    public List<Point> points;

    public GameObject halfPlanePrefab;
    public TextMeshProUGUI warningText; // UI Text component to display warnings
    public static readonly float lineVisibleThickness = 0.1f;
    readonly float basePointColliderThickness = 0.1f;
    float pointColliderThickness;

    List<HalfPlane> halfPlanes;

    // Half-plane inclusion coloring
    [Header("Half-Plane Inclusion Coloring")]
    public HalfPlaneTriple coloringTriple;
    public bool enableHalfPlaneColoring = true;

    // Color mapping for the 8 possible combinations (false,false,false) to (true,true,true)
    private static readonly Color[] inclusionColors = new Color[]
    {
        Color.grey,             // (false, false, false) = 000
        Color.red,              // (true, false, false)  = 001
        Color.green,            // (false, true, false)  = 010
        Color.blue,             // (true, true, false)   = 011
        Color.yellow,           // (false, false, true)  = 100
        Color.magenta,          // (true, false, true)   = 101
        Color.cyan,             // (false, true, true)   = 110
        Color.white             // (true, true, true)    = 111
    };

    private int? closestPointIndex;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false;
    private bool isCameraDragging = false;
    private bool hasCollinearPoints = false;

    private Vector3 lastMousePosition;

    private List<Point> AllPoints => points
    .Concat(debugHalfPlanePairs.Select(p => p.pointA))
    .Concat(debugHalfPlanePairs.Select(p => p.pointB))
    .Where(p => p != null)
    .Distinct()
    .ToList();

    private void Awake()
    {
        foreach (var point in points)
        {
            point.parentSevenPointPartitioner = this;
        }

        // Also set the parent for points used only in debugHalfPlanePairs
        foreach (var pair in debugHalfPlanePairs)
        {
            if (pair.pointA != null && !points.Contains(pair.pointA))
                pair.pointA.parentSevenPointPartitioner = this;

            if (pair.pointB != null && !points.Contains(pair.pointB))
                pair.pointB.parentSevenPointPartitioner = this;

            pair.pointA.normalColour = Color.white;
            pair.pointB.normalColour = Color.white;
        }

        InitializeHalfPlanesFromPoints();
        InitializeDebugHalfPlanes();
        UpdateSelectionRadius();
    }

    List<HalfPlane> debugHalfPlanes;

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

    public List<PointPair> debugHalfPlanePairs;

    private void InitializeDebugHalfPlanes()
    {
        debugHalfPlanes = new List<HalfPlane>();
        int colorIndex = 0;

        foreach (var pair in debugHalfPlanePairs)
        {
            if (pair.pointA == null || pair.pointB == null) continue;

            GameObject halfPlaneObj = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
            HalfPlane halfPlane = halfPlaneObj.GetComponent<HalfPlane>();

            halfPlane.inputPoint1 = pair.pointA.transform;
            halfPlane.inputPoint2 = pair.pointB.transform;
            halfPlane.parentSevenPointPartitioner = this;

            Color debugColor = debugColors[colorIndex % debugColors.Length];
            debugColor.a = 0.9f;
            halfPlane.colour = debugColor;
            colorIndex++;

            halfPlane.ShouldBeVisible += _ => !hasCollinearPoints;

            debugHalfPlanes.Add(halfPlane);
        }

        coloringTriple.halfPlaneA = debugHalfPlanes[0];
        coloringTriple.halfPlaneB = debugHalfPlanes[1];
        coloringTriple.halfPlaneC = debugHalfPlanes[2];
        UpdatePointColorsFromHalfPlaneInclusions();
    }

    private void InitializeHalfPlanesFromPoints()
    {
        halfPlanes = new List<HalfPlane>();

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < points.Count; j++)
            {
                if (i != j)
                {
                    GameObject halfPlaneObj = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
                    HalfPlane halfPlane = halfPlaneObj.GetComponent<HalfPlane>();

                    halfPlane.inputPoint1 = points[i].transform;
                    halfPlane.inputPoint2 = points[j].transform;
                    halfPlane.parentSevenPointPartitioner = this;

                    // Assign debug color
                    Color color = Color.green;
                    color.a = 0.18f;
                    halfPlane.colour = color;

                    // Register visibility rule - hide if collinear points exist
                    halfPlane.ShouldBeVisible += line => !hasCollinearPoints && ShouldShowHalfPlane(line);

                    halfPlanes.Add(halfPlane);
                }
            }
        }
    }

    /// <summary>
    /// Computes the half-plane inclusion for a given point and ordered triple of half-planes
    /// </summary>
    /// <param name="point">The point to test</param>
    /// <param name="triple">The ordered triple of half-planes (h_a, h_b, h_c)</param>
    /// <returns>A tuple (inA, inB, inC) indicating inclusion in each half-plane</returns>
    public (bool inA, bool inB, bool inC) HalfPlaneInclusions(Vector2 point, HalfPlaneTriple triple)
    {
        bool inA = IsPointInHalfPlane(point, triple.halfPlaneA);
        bool inB = IsPointInHalfPlane(point, triple.halfPlaneB);
        bool inC = IsPointInHalfPlane(point, triple.halfPlaneC);

        return (inA, inB, inC);
    }

    /// <summary>
    /// Checks if a point is inside a half-plane (closed half-plane includes the boundary)
    /// </summary>
    /// <param name="point">The point to test</param>
    /// <param name="halfPlane">The half-plane to test against</param>
    /// <returns>True if the point is in the half-plane (including boundary)</returns>
    private bool IsPointInHalfPlane(Vector2 point, HalfPlane halfPlane)
    {
        if (halfPlane == null || halfPlane.inputPoint1 == null || halfPlane.inputPoint2 == null)
            return false;

        Vector2 a = halfPlane.inputPoint1.position;
        Vector2 b = halfPlane.inputPoint2.position;

        // Calculate the cross product to determine which side of the line the point is on
        // For a line from A to B, and point P:
        // cross = (B.x - A.x) * (P.y - A.y) - (B.y - A.y) * (P.x - A.x)
        // If cross >= 0, point is on the left side or on the line
        // If cross < 0, point is on the right side
        float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

        // For a closed half-plane, we include points on the boundary (cross == 0)
        // The convention here assumes the half-plane is to the left of the directed line A->B
        return cross >= 0;
    }

    /// <summary>
    /// Maps a half-plane inclusion tuple to a color
    /// </summary>
    /// <param name="inclusions">The inclusion tuple (inA, inB, inC)</param>
    /// <returns>The corresponding color</returns>
    public Color GetColorFromInclusions((bool inA, bool inB, bool inC) inclusions)
    {
        // Convert boolean tuple to binary index: inA*4 + inB*2 + inC*1
        int colorIndex = (inclusions.inA ? 4 : 0) + (inclusions.inB ? 2 : 0) + (inclusions.inC ? 1 : 0);
        return inclusionColors[colorIndex];
    }

    /// <summary>
    /// Updates the colors of all points based on their half-plane inclusions
    /// </summary>
    public void UpdatePointColorsFromHalfPlaneInclusions()
    {
        if (!enableHalfPlaneColoring ||
            coloringTriple.halfPlaneA == null ||
            coloringTriple.halfPlaneB == null ||
            coloringTriple.halfPlaneC == null)
        {
            return;
        }

        foreach (Point point in points)
        {
            var inclusions = HalfPlaneInclusions(point.Position, coloringTriple);
            Color newColor = GetColorFromInclusions(inclusions);

            // Update the point's color (assuming Point has a color property)
            point.normalColour = newColor;
        }
    }

    private bool CheckForCollinearPoints()
    {
        const float collinearityThreshold = 0.01f; // Tolerance for floating point comparison

        // Check all combinations of 3 points
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                for (int k = j + 1; k < points.Count; k++)
                {
                    Vector2 p1 = points[i].Position;
                    Vector2 p2 = points[j].Position;
                    Vector2 p3 = points[k].Position;

                    // Calculate cross product to check collinearity
                    // If three points are collinear, the cross product of vectors (p2-p1) and (p3-p1) will be zero
                    Vector2 v1 = p2 - p1;
                    Vector2 v2 = p3 - p1;
                    float crossProduct = v1.x * v2.y - v1.y * v2.x;

                    if (Mathf.Abs(crossProduct) < collinearityThreshold)
                    {
                        return true; // Found collinear points
                    }
                }
            }
        }

        return false;
    }

    private void UpdateWarningDisplay()
    {
        if (warningText != null)
        {
            if (hasCollinearPoints)
            {
                warningText.text = "WARNING: Three or more points are collinear. Move points to continue.";
                warningText.color = Color.red;
                warningText.gameObject.SetActive(true);
            }
            else
            {
                warningText.gameObject.SetActive(false);
            }
        }
    }

    private void CheckForPossibleCentres()
    {
        // Skip this logic if we have collinear points
        if (hasCollinearPoints) return;

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

        UpdatePointColorsFromHalfPlaneInclusions();
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
        // Check for collinear points first
        bool previousCollinearState = hasCollinearPoints;
        hasCollinearPoints = CheckForCollinearPoints();

        // Update warning display if collinearity state changed
        if (hasCollinearPoints != previousCollinearState)
        {
            UpdateWarningDisplay();
        }

        UpdatePointColorsFromHalfPlaneInclusions();

        // Reset highlights
        foreach (Point point in AllPoints)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            AllPoints[closestPointIndexInAllPoints.Value].Highlight(true);

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
            Camera.main.orthographicSize = Mathf.Pow(5, 1 + scrollAmount / 5f);
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
    }

    private bool ShouldShowHalfPlane(Line halfPlane)
    {
        // Don't show half-planes if we have collinear points
        if (hasCollinearPoints) return false;

        Vector2 a = halfPlane.inputPoint1.position;
        Vector2 b = halfPlane.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;

        foreach (Point p in points)
        {
            if (p.transform == halfPlane.inputPoint1 || p.transform == halfPlane.inputPoint2)
                continue;

            Vector2 pt = p.transform.position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            if (Mathf.Approximately(cross, 0f))
                continue; // Point is on the halfPlane

            if (cross > 0)
                leftCount++;

            if (cross < 0)
                rightCount++;
        }

        return (leftCount == 3 && rightCount == 2) || (leftCount == 4 && rightCount == 1);
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

        UpdatePointColorsFromHalfPlaneInclusions();
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