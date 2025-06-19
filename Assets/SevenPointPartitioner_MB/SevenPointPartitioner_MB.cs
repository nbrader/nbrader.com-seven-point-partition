using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using Unity.VisualScripting;

public enum DragState
{
    None = 0,
    DraggingPoint = 1,
    DraggingCamera = 2
}

[System.Serializable]
public struct HalfPlaneTriple
{
    public HalfPlane_MB halfPlaneA;
    public HalfPlane_MB halfPlaneB;
    public HalfPlane_MB halfPlaneC;

    public HalfPlaneTriple(HalfPlane_MB a, HalfPlane_MB b, HalfPlane_MB c)
    {
        halfPlaneA = a;
        halfPlaneB = b;
        halfPlaneC = c;
    }
}   

public class SevenPointPartitioner_MB : MonoBehaviour
{
    /// <summary>
    /// The input points to the partition finding problem.
    /// </summary>
    public List<Point_MB> points;

    public GameObject halfPlanePrefab;

    public TextMeshProUGUI warningText; // UI Text component to display warnings
    public TextMeshProUGUI solutionCountText; // UI Text component to current selected solution out of how many
    public static readonly float lineVisibleThickness = 0.1f;
    readonly float basePointColliderThickness = 0.1f;
    float pointColliderThickness;

    List<HalfPlane_MB> halfPlanes;

    // Half-plane inclusion coloring
    [Header("Half-Plane Inclusion Coloring")]
    public HalfPlaneTriple coloringTriple;
    public bool enableHalfPlaneColoring = true;

    // Point inclusion coloring for lines
    [Header("Point Inclusion Line Coloring")]
    public bool enablePointInclusionColoring = true;

    // Debug lines toggle
    [Header("Debug Lines Control")]
    public bool hideNonDebugLines = true;

    // Valid partition triangles
    [Header("Valid Partition Triangles")]
    public bool showValidPartitionTriangles = true;
    readonly float validTriangleThickness = 0.1f;

    private readonly List<HalfPlaneTriple> validPartitionTriangles = new();

    // Triangle cycling variables
    private int currentTriangleIndex = 0;
    private bool hasValidTriangles = false;

    // Colors for valid partition triangles
    private static readonly Color[] pointInclusionColors = new Color[]
    {
        new Color(1f, 0f, 0f, 1f),      // Red
        new Color(0f, 1f, 0f, 1f),      // Green
        new Color(0f, 0f, 1f, 1f),      // Blue
        new Color(1f, 1f, 0f, 1f),      // Yellow
        new Color(1f, 0f, 1f, 1f),      // Magenta
        new Color(0f, 1f, 1f, 1f),      // Cyan
        new Color(1f, 0.5f, 0f, 1f),    // Orange
        new Color(0.5f, 0f, 1f, 1f),    // Purple
        new Color(0f, 1f, 0.5f, 1f),    // Spring Green
        new Color(1f, 0f, 0.5f, 1f),    // Rose
        new Color(0.5f, 1f, 0f, 1f),    // Chartreuse
        new Color(0f, 0.5f, 1f, 1f),    // Azure
        new Color(1f, 1f, 1f, 1f),    // White
    };

    private bool hasCollinearPoints = false;

    private Vector3 lastMousePosition;

    // With this single enum:
    private DragState currentDragState = DragState.None;

    private void Awake()
    {
        foreach (var point in points)
        {
            point.parentSevenPointPartitioner = this;
        }

        InitializeHalfPlanesFromPoints();
        UpdateSelectionRadius();
        FindValidPartitionTriangles();
    }

    private void InitializeHalfPlanesFromPoints()
    {
        halfPlanes = new List<HalfPlane_MB>();

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < points.Count; j++)
            {
                if (i != j)
                {
                    GameObject halfPlaneObj = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
                    HalfPlane_MB halfPlane = halfPlaneObj.GetComponent<HalfPlane_MB>();

                    halfPlane.inputPoint1 = points[i].transform;
                    halfPlane.inputPoint2 = points[j].transform;
                    halfPlane.parentSevenPointPartitioner = this;

                    // Assign debug color
                    Color color = Color.green;
                    color.a = 0.18f;
                    halfPlane.colour = color;

                    // Register visibility rule - hide if collinear points exist
                    halfPlane.ShouldBeVisible += line => !hasCollinearPoints && ShouldShowHalfPlane(line);
                    halfPlane.ForceHidden += _ => hideNonDebugLines;

                    halfPlanes.Add(halfPlane);
                }
            }
        }
    }

    /// <summary>
    /// Finds all valid partition triangles that create unique regions for the 7 points
    /// Following the algorithm: enumerate qualifying lines, test pairs with LEMMA 2, find valid triples
    /// </summary>
    private void FindValidPartitionTriangles()
    {
        validPartitionTriangles.Clear();

        if (points.Count != 7 || hasCollinearPoints)
        {
            hasValidTriangles = false;
            return;
        }

        // Step 1: Find all qualifying lines (2-3 or 1-4 splits, nudged to 3-4 splits)
        List<HalfPlane_MB> qualifyingLines = new();
        List<(bool, bool, bool, bool, bool, bool, bool)> qualifyingInclusions = new();

        foreach (var halfPlane in halfPlanes)
        {
            if (IsQualifyingLine(halfPlane))
            {
                (bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) inclusions = PointInclusions(halfPlane);
                if (!qualifyingInclusions.Contains(inclusions))
                {
                    qualifyingLines.Add(halfPlane);
                    qualifyingInclusions.Add(inclusions);
                }
            }
        }

        // Step 2: Find valid pairs that satisfy LEMMA 2
        List<(HalfPlane_MB, HalfPlane_MB)> validPairs = new();

        for (int i = 0; i < qualifyingLines.Count; i++)
        {
            for (int j = i + 1; j < qualifyingLines.Count; j++)
            {
                if (SatisfiesLemma2(qualifyingLines[i], qualifyingLines[j]))
                {
                    validPairs.Add((qualifyingLines[i], qualifyingLines[j]));
                }
            }
        }

        // Step 3: Find triples where every pair is valid and instantiate triangle half-planes
        int validTripleCount = 0;

        // Clean up any previously created triangle half-planes
        CleanupTriangleHalfPlanes();

        for (int i = 0; i < qualifyingLines.Count; i++)
        {
            for (int j = i + 1; j < qualifyingLines.Count; j++)
            {
                for (int k = j + 1; k < qualifyingLines.Count; k++)
                {
                    var line1 = qualifyingLines[i];
                    var line2 = qualifyingLines[j];
                    var line3 = qualifyingLines[k];

                    // Check if all three pairs are valid
                    bool pair12Valid = validPairs.Contains((line1, line2)) || validPairs.Contains((line2, line1));
                    bool pair13Valid = validPairs.Contains((line1, line3)) || validPairs.Contains((line3, line1));
                    bool pair23Valid = validPairs.Contains((line2, line3)) || validPairs.Contains((line3, line2));

                    if (pair12Valid && pair13Valid && pair23Valid)
                    {
                        validTripleCount++;

                        // Check if this triple creates unique partitions for all 7 points (1/1/1/1/1/1/1)
                        if (CreatesUniquePartitions(line1, line2, line3))
                        {
                            // Create new half-plane instances for this triangle
                            var triangleHalfPlanes = CreateTriangleHalfPlanes(line1, line2, line3);

                            var triple = new HalfPlaneTriple(triangleHalfPlanes.line1, triangleHalfPlanes.line2, triangleHalfPlanes.line3);
                            validPartitionTriangles.Add(triple);
                        }
                    }
                }
            }
        }

        // Update triangle cycling state
        hasValidTriangles = validPartitionTriangles.Count > 0;
        if (hasValidTriangles)
        {
            currentTriangleIndex = 0;
        }

        // Initial visibility setup
        UpdateTriangleVisibility();
    }

    /// <summary>
    /// Updates the visibility of triangles based on cycling
    /// </summary>
    private void UpdateTriangleVisibility()
    {
        if (!hasValidTriangles || validPartitionTriangles.Count == 0)
            return;

        // Hide all triangles first
        for (int i = 0; i < validPartitionTriangles.Count; i++)
        {
            var triangle = validPartitionTriangles[i];
            SetTriangleVisibility(triangle, false);
        }

        // Show only the current triangle
        if (currentTriangleIndex < validPartitionTriangles.Count)
        {
            var currentTriangle = validPartitionTriangles[currentTriangleIndex];
            SetTriangleVisibility(currentTriangle, true);
        }
    }

    /// <summary>
    /// Sets the visibility of a specific triangle
    /// </summary>
    private void SetTriangleVisibility(HalfPlaneTriple triangle, bool visible)
    {
        if (triangle.halfPlaneA != null)
            triangle.halfPlaneA.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
        if (triangle.halfPlaneB != null)
            triangle.halfPlaneB.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
        if (triangle.halfPlaneC != null)
            triangle.halfPlaneC.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
    }

    /// <summary>
    /// Enhanced version that properly implements the nudging concept from the algorithm
    /// </summary>
    private bool IsQualifyingLine(HalfPlane_MB halfPlane)
    {
        Vector2 a = halfPlane.inputPoint1.position;
        Vector2 b = halfPlane.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;
        int onLineCount = 0;

        // Count points on each side of the line (excluding the two points defining the line)
        foreach (Point_MB p in points)
        {
            if (p.transform == halfPlane.inputPoint1 || p.transform == halfPlane.inputPoint2)
                continue;

            Vector2 pt = p.transform.position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            const float epsilon = 1e-6f;
            if (Mathf.Abs(cross) < epsilon)
            {
                onLineCount++;
            }
            else if (cross > 0)
            {
                leftCount++;
            }
            else
            {
                rightCount++;
            }
        }

        // Original splits we're looking for: 2-3 or 1-4
        // After nudging (moving points on line to the side with fewer points), we get 3-4 splits
        bool isValid = false;

        // Case 1: 2-3 split with possible points on the line
        if ((leftCount == 2 && rightCount == 3) || (leftCount == 3 && rightCount == 2))
        {
            isValid = true;
        }
        // Case 2: 1-4 split with possible points on the line  
        else if ((leftCount == 1 && rightCount == 4) || (leftCount == 4 && rightCount == 1))
        {
            isValid = true;
        }
        // Case 3: Account for nudging - if we have points on the line, they can be moved to create valid splits
        else if (onLineCount > 0)
        {
            // Check if moving all points on line to left side gives 2-3 or 1-4 split
            int newLeftCount = leftCount + onLineCount;
            int newRightCount = rightCount;
            if ((newLeftCount == 2 && newRightCount == 3) || (newLeftCount == 3 && newRightCount == 2) ||
                (newLeftCount == 1 && newRightCount == 4) || (newLeftCount == 4 && newRightCount == 1))
            {
                isValid = true;
            }

            // Check if moving all points on line to right side gives 2-3 or 1-4 split
            newLeftCount = leftCount;
            newRightCount = rightCount + onLineCount;
            if ((newLeftCount == 2 && newRightCount == 3) || (newLeftCount == 3 && newRightCount == 2) ||
                (newLeftCount == 1 && newRightCount == 4) || (newLeftCount == 4 && newRightCount == 1))
            {
                isValid = true;
            }
        }

        return isValid;
    }

    /// <summary>
    /// Placeholder for LEMMA 2 implementation - needs to be implemented based on your specific requirements
    /// For now, this ensures lines are not identical and checks for basic geometric constraints
    /// </summary>
    private bool SatisfiesLemma2(HalfPlane_MB line1, HalfPlane_MB line2)
    {
        // Basic check: lines must be different
        if (line1 == line2) return false;

        // Check that the lines are not identical (same endpoints)
        bool sameEndpoints = (line1.inputPoint1 == line2.inputPoint1 && line1.inputPoint2 == line2.inputPoint2) ||
                            (line1.inputPoint1 == line2.inputPoint2 && line1.inputPoint2 == line2.inputPoint1);

        if (sameEndpoints) return false;

        // TODO: Implement the actual LEMMA 2 conditions based on your geometric requirements
        // This might involve checking intersection properties, orientation, or other geometric constraints

        // For now, we'll use a basic heuristic: the lines should intersect within a reasonable region
        // and create meaningful partitions
        if (GetLineIntersection(line1, line2, out Vector2 intersection))
        {
            // Check if intersection is reasonable (not too far from the point cloud)
            float maxDistance = GetMaxDistanceFromOrigin() * 2; // Reasonable bounds
            if (intersection.magnitude > maxDistance)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Enhanced version that properly checks for unique 1/1/1/1/1/1/1 partitioning
    /// </summary>
    private bool CreatesUniquePartitions(HalfPlane_MB line1, HalfPlane_MB line2, HalfPlane_MB line3)
    {
        HashSet<int> partitionCodes = new();

        foreach (Point_MB point in points)
        {
            Vector2 pos = point.Position;

            // Get inclusion in each half-plane (using consistent orientation)
            bool in1 = IsPointInHalfPlaneRight(pos, line1);
            bool in2 = IsPointInHalfPlaneRight(pos, line2);
            bool in3 = IsPointInHalfPlaneRight(pos, line3);

            // Create a unique code for this combination (3-bit binary number)
            int code = (in1 ? 1 : 0) + (in2 ? 2 : 0) + (in3 ? 4 : 0);

            // If we've seen this combination before, partitions are not unique
            if (partitionCodes.Contains(code))
            {
                return false;
            }

            partitionCodes.Add(code);
        }

        // Should have exactly 7 unique partitions for 7 points (1/1/1/1/1/1/1)
        bool isUnique = partitionCodes.Count == 7;

        return isUnique;
    }

    /// <summary>
    /// Helper method to get line intersection point
    /// </summary>
    private bool GetLineIntersection(HalfPlane_MB line1, HalfPlane_MB line2, out Vector2 intersection)
    {
        Vector2 p1 = line1.inputPoint1.position;
        Vector2 p2 = line1.inputPoint2.position;
        Vector2 p3 = line2.inputPoint1.position;
        Vector2 p4 = line2.inputPoint2.position;

        Vector2 d1 = p2 - p1;
        Vector2 d2 = p4 - p3;

        float denominator = d1.x * d2.y - d1.y * d2.x;

        intersection = Vector2.zero;

        if (Mathf.Abs(denominator) < 1e-6f)
        {
            return false; // Lines are parallel
        }

        float t = ((p3.x - p1.x) * d2.y - (p3.y - p1.y) * d2.x) / denominator;
        intersection = p1 + t * d1;

        return true;
    }

    /// <summary>
    /// Helper method to get maximum distance from origin for bounds checking
    /// </summary>
    private float GetMaxDistanceFromOrigin()
    {
        float maxDist = 0f;
        foreach (Point_MB p in points)
        {
            float dist = p.Position.magnitude;
            if (dist > maxDist) maxDist = dist;
        }
        return maxDist;
    }

    // List to keep track of instantiated triangle half-planes for cleanup
    private readonly List<HalfPlane_MB> triangleHalfPlanes = new();

    /// <summary>
    /// Creates new half-plane instances for a triangle with proper visual styling
    /// </summary>
    /// <param name="originalLine1">Original qualifying line 1</param>
    /// <param name="originalLine2">Original qualifying line 2</param>
    /// <param name="originalLine3">Original qualifying line 3</param>
    /// <param name="colorIndex">Index for triangle color</param>
    /// <returns>Tuple of the three new half-plane instances</returns>
    private (HalfPlane_MB line1, HalfPlane_MB line2, HalfPlane_MB line3) CreateTriangleHalfPlanes(
        HalfPlane_MB originalLine1, HalfPlane_MB originalLine2, HalfPlane_MB originalLine3)
    {
        // Create first half-plane
        GameObject halfPlaneObj1 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane_MB halfPlane1 = halfPlaneObj1.GetComponent<HalfPlane_MB>();
        var inclusions1 = PointInclusions(originalLine1);
        Color newColor1 = GetColorFromPointInclusions(inclusions1);
        SetupTriangleHalfPlane(halfPlane1, originalLine1, newColor1);

        // Create second half-plane
        GameObject halfPlaneObj2 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane_MB halfPlane2 = halfPlaneObj2.GetComponent<HalfPlane_MB>();
        var inclusions2 = PointInclusions(originalLine2);
        Color newColor2 = GetColorFromPointInclusions(inclusions2);
        SetupTriangleHalfPlane(halfPlane2, originalLine2, newColor2);

        // Create third half-plane
        GameObject halfPlaneObj3 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane_MB halfPlane3 = halfPlaneObj3.GetComponent<HalfPlane_MB>();
        var inclusions3 = PointInclusions(originalLine3);
        Color newColor3 = GetColorFromPointInclusions(inclusions3);
        SetupTriangleHalfPlane(halfPlane3, originalLine3, newColor3);

        // Add to our tracking list for cleanup
        triangleHalfPlanes.Add(halfPlane1);
        triangleHalfPlanes.Add(halfPlane2);
        triangleHalfPlanes.Add(halfPlane3);

        return (halfPlane1, halfPlane2, halfPlane3);
    }

    /// <summary>
    /// Sets up a triangle half-plane with proper configuration
    /// </summary>
    /// <param name="halfPlane">The half-plane to configure</param>
    /// <param name="originalLine">The original line to copy configuration from</param>
    /// <param name="color">The color for this triangle</param>
    private void SetupTriangleHalfPlane(HalfPlane_MB halfPlane, HalfPlane_MB originalLine, Color color)
    {
        // Copy the endpoints from the original line
        halfPlane.inputPoint1 = originalLine.inputPoint1;
        halfPlane.inputPoint2 = originalLine.inputPoint2;
        halfPlane.parentSevenPointPartitioner = this;

        // Set visual properties for triangle display
        halfPlane.colour = color;
        halfPlane.Thickness = validTriangleThickness;

        // Triangle visibility will be controlled by the cycling system
        halfPlane.ShouldBeVisible += _ => true; // Always allow visibility (controlled by SetActive)
        halfPlane.ForceHidden += _ => false; // Don't force hide triangle lines
    }

    /// <summary>
    /// Cleans up previously created triangle half-planes
    /// </summary>
    private void CleanupTriangleHalfPlanes()
    {
        foreach (var halfPlane in triangleHalfPlanes)
        {
            if (halfPlane != null && halfPlane.gameObject != null)
            {
                DestroyImmediate(halfPlane.gameObject);
            }
        }
        triangleHalfPlanes.Clear();
    }

    /// <summary>
    /// Call this method when the component is destroyed or when you need to clean up all triangles
    /// </summary>
    private void OnDestroy()
    {
        CleanupTriangleHalfPlanes();
    }

    /// <summary>
    /// Computes the point inclusion for a given half-plane and the ordered set of 7 points
    /// </summary>
    /// <param name="halfPlane">The half-plane to test against</param>
    /// <returns>A tuple of 7 booleans indicating inclusion for each point (p1, p2, p3, p4, p5, p6, p7)</returns>
    public (bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) PointInclusions(HalfPlane_MB halfPlane)
    {
        if (points.Count < 7)
        {
            Debug.LogWarning("PointInclusions requires exactly 7 points, but only " + points.Count + " are available.");
            return (false, false, false, false, false, false, false);
        }

        bool p1 = IsPointInHalfPlaneRight(points[0].Position, halfPlane);
        bool p2 = IsPointInHalfPlaneRight(points[1].Position, halfPlane);
        bool p3 = IsPointInHalfPlaneRight(points[2].Position, halfPlane);
        bool p4 = IsPointInHalfPlaneRight(points[3].Position, halfPlane);
        bool p5 = IsPointInHalfPlaneRight(points[4].Position, halfPlane);
        bool p6 = IsPointInHalfPlaneRight(points[5].Position, halfPlane);
        bool p7 = IsPointInHalfPlaneRight(points[6].Position, halfPlane);

        var all_ps = new List<bool>() { p1, p2, p3, p4, p5, p6, p7 };

        if (all_ps.Where(x => x).Count() > 3)
        {
            all_ps = all_ps.Select(x => !x).ToList();
        }

        return (all_ps[0], all_ps[1], all_ps[2], all_ps[3], all_ps[4], all_ps[5], all_ps[6]);
    }

    /// <summary>
    /// Checks if a point is in the right half-plane of a line (including the line itself)
    /// "Right" is defined by treating the second point as the "forward" direction from the first
    /// </summary>
    /// <param name="point">The point to test</param>
    /// <param name="halfPlane">The half-plane defined by two points</param>
    /// <returns>True if the point is on the right side of the line or on the line</returns>
    private bool IsPointInHalfPlaneRight(Vector2 point, HalfPlane_MB halfPlane)
    {
        if (halfPlane == null || halfPlane.inputPoint1 == null || halfPlane.inputPoint2 == null)
            return false;

        Vector2 a = halfPlane.inputPoint1.position; // First point
        Vector2 b = halfPlane.inputPoint2.position; // Second point (defines "forward")

        // Calculate the cross product to determine which side of the line the point is on
        // For a line from A to B, and point P:
        // cross = (B.x - A.x) * (P.y - A.y) - (B.y - A.y) * (P.x - A.x)
        // If cross <= 0, point is on the right side or on the line (when looking from A towards B)
        // If cross > 0, point is on the left side
        float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

        // For the right half-plane (including boundary), we want cross <= 0
        return cross <= 0;
    }

    /// <summary>
    /// Maps a point inclusion tuple to a color using the 7-point inclusion system
    /// </summary>
    /// <param name="inclusions">The inclusion tuple for 7 points</param>
    /// <returns>The corresponding color</returns>
    public Color GetColorFromPointInclusions((bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) inclusions)
    {
        // Convert boolean tuple to binary index
        int colorIndex = 0;
        if (inclusions.p1) colorIndex |= 1;
        if (inclusions.p2) colorIndex |= 2;
        if (inclusions.p3) colorIndex |= 4;
        if (inclusions.p4) colorIndex |= 8;
        if (inclusions.p5) colorIndex |= 16;
        if (inclusions.p6) colorIndex |= 32;
        if (inclusions.p7) colorIndex |= 64;

        var col = pointInclusionColors[colorIndex % pointInclusionColors.Length];
        col.a = Mathf.Sqrt(Mathf.Sqrt(1 / Mathf.Pow(2, colorIndex / pointInclusionColors.Length)));

        return col;
    }

    ///// <summary>
    ///// Computes the half-plane inclusion for a given point and ordered triple of half-planes
    ///// </summary>
    ///// <param name="point">The point to test</param>
    ///// <param name="triple">The ordered triple of half-planes (h_a, h_b, h_c)</param>
    ///// <returns>A tuple (inA, inB, inC) indicating inclusion in each half-plane</returns>
    //public (bool inA, bool inB, bool inC) HalfPlaneInclusions(Vector2 point, HalfPlaneTriple triple)
    //{
    //    bool inA = IsPointInHalfPlane(point, triple.halfPlaneA);
    //    bool inB = IsPointInHalfPlane(point, triple.halfPlaneB);
    //    bool inC = IsPointInHalfPlane(point, triple.halfPlaneC);

    //    return (inA, inB, inC);
    //}

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

            if (posA.y != posB.y)
                return posB.y.CompareTo(posA.y); // Descending Y

            if (posA.x != posB.x)
                return posA.x.CompareTo(posB.x); // Ascending X

            return posA.z.CompareTo(posB.z); // Ascending Z
        });
    }

    public void MovePoint(int pointIndex, Vector3 targetPosition)
    {
        points[pointIndex].Position = targetPosition;

        // Update colors when points move
        FindValidPartitionTriangles();
    }

    public (int closestPoint, float closestDistance) FindClosestPoint(Vector3 position)
    {
        List<Point_MB> allPoints = points;
        Point_MB point = allPoints[0];
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

    // Alternative: Add debug visualization to see the detection areas
    // (Add this to help you understand what's happening - remove in production)
    private void OnDrawGizmos()
    {
        if (points != null)
        {
            Gizmos.color = Color.yellow;
            foreach (var point in points)
            {
                // Draw a wire sphere to represent the detection area
                Gizmos.DrawWireSphere(point.Position, pointColliderThickness);
            }
        }
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

        // Reset highlights
        foreach (Point_MB point in points)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            points[closestPointIndexInAllPoints.Value].Highlight(true);

        CheckForPossibleCentres();

        if (IsDraggingPoint) return;

        var pointerPos = Camera.main.ScreenToWorldPoint(Input.mousePosition) + Vector3.forward * 10;

        var (closestPoint, closestDistance) = FindClosestPoint(pointerPos);
        closestPointIndexInAllPoints = closestPoint;

        if (closestDistance < pointColliderThickness)
        {
            closestPointIndexInAllPoints = closestPoint;
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
        if (Input.GetMouseButtonDown((int)MouseButton.Left))
        {
            // Only start camera dragging if we're not close to any point and not already dragging
            if (closestPointIndexInAllPoints == null && currentDragState == DragState.None)
            {
                currentDragState = DragState.DraggingCamera;
                lastMousePosition = Input.mousePosition;
            }
        }

        if (Input.GetMouseButtonUp((int)MouseButton.Left))
        {
            if (IsDraggingCamera)
            {
                currentDragState = DragState.None;
            }
        }

        // Only drag camera if we're in camera dragging state
        if (IsDraggingCamera)
        {
            Vector3 delta = Camera.main.ScreenToWorldPoint(Input.mousePosition) - Camera.main.ScreenToWorldPoint(lastMousePosition);
            Camera.main.transform.position -= delta;
            lastMousePosition = Input.mousePosition;
        }

        // Handle triangle cycling
        if (hasValidTriangles && validPartitionTriangles.Count > 1)
        {
            // Handle spacebar input for toggling debug lines
            if (Input.GetMouseButtonDown((int)MouseButton.Middle))
            {
                currentTriangleIndex = Maths.mod(currentTriangleIndex + 1, validPartitionTriangles.Count);
                UpdateTriangleVisibility();
            }
            else if (Input.GetMouseButtonDown((int)MouseButton.Right))
            {
                currentTriangleIndex = Maths.mod(currentTriangleIndex - 1, validPartitionTriangles.Count);
                UpdateTriangleVisibility();
            }
        }

        if (!hasValidTriangles)
        {
            solutionCountText.text = "No Solutions.";
        }
        else
        {
            solutionCountText.text = string.Format("Solution {0} out of {1}.", currentTriangleIndex+1, validPartitionTriangles.Count);
        }

        // Reset highlights
        foreach (Point_MB point in points)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            points[closestPointIndexInAllPoints.Value].Highlight(true);
    }

    private bool ShouldShowHalfPlane(Line_MB halfPlane)
    {
        // Don't show half-planes if we have collinear points
        if (hasCollinearPoints) return false;

        Vector2 a = halfPlane.inputPoint1.position;
        Vector2 b = halfPlane.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;

        foreach (Point_MB p in points)
        {
            if (p.transform == halfPlane.inputPoint1 || p.transform == halfPlane.inputPoint2)
                continue;

            Vector2 pt = p.transform.position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            if (cross != 0f)
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
        // Only allow left mouse button to start dragging
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (closestPointIndexInAllPoints != null && currentDragState == DragState.None)
        {
            currentDragState = DragState.DraggingPoint;
            OnBeginDragPoint(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Only allow left mouse button to continue dragging
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (IsDraggingPoint)
        {
            OnDragPoint(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Only allow left mouse button to end dragging
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (IsDraggingPoint)
        {
            OnEndDragPoint(eventData);

            currentDragState = DragState.None;
            FindValidPartitionTriangles();
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
            points[closestPointIndexInAllPoints.Value].Position = worldPoint;
        }
    }

    public void OnEndDragPoint(PointerEventData eventData)
    {
        closestPointIndexInAllPoints = null;
    }

    public bool IsDraggingPoint => currentDragState == DragState.DraggingPoint;
    public bool IsDraggingCamera => currentDragState == DragState.DraggingCamera;

    private Vector3 ScreenToWorldPoint(Vector2 screenPosition)
    {
        Vector3 screenPoint = new(screenPosition.x, screenPosition.y, -Camera.main.transform.position.z);
        return Camera.main.ScreenToWorldPoint(screenPoint);
    }

    private void UpdateSelectionRadius()
    {
        float zoomFactor = Camera.main.orthographicSize;
        pointColliderThickness = basePointColliderThickness * zoomFactor;
    }
}
