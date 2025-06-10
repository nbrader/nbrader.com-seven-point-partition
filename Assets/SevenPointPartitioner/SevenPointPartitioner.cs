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
    /// <summary>
    /// The input points to the partition finding problem.
    /// </summary>
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

    // Point inclusion coloring for lines
    [Header("Point Inclusion Line Coloring")]
    public bool enablePointInclusionColoring = true;

    // Debug lines toggle
    [Header("Debug Lines Control")]
    public bool hideNonDebugLines = true;

    // Valid partition triangles
    [Header("Valid Partition Triangles")]
    public bool showValidPartitionTriangles = true;
    float validTriangleThickness = 0.1f;

    private List<HalfPlaneTriple> validPartitionTriangles = new List<HalfPlaneTriple>();
    private List<Color> triangleColors = new List<Color>();

    // Triangle cycling variables
    private int currentTriangleIndex = 0;
    private bool hasValidTriangles = false;

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

    // Color mapping for 128 possible combinations of 7 points (2^7 = 128)
    // We'll use a hash-based coloring system for the 128 combinations
    private static readonly Color[] pointInclusionColors = GeneratePointInclusionColors();

    // Colors for valid partition triangles
    private static readonly Color[] validTriangleColors = new Color[]
    {
        new Color(1f, 0f, 0f, 0.8f),      // Red
        new Color(0f, 1f, 0f, 0.8f),      // Green
        new Color(0f, 0f, 1f, 0.8f),      // Blue
        new Color(1f, 1f, 0f, 0.8f),      // Yellow
        new Color(1f, 0f, 1f, 0.8f),      // Magenta
        new Color(0f, 1f, 1f, 0.8f),      // Cyan
        new Color(1f, 0.5f, 0f, 0.8f),    // Orange
        new Color(0.5f, 0f, 1f, 0.8f),    // Purple
        new Color(0f, 1f, 0.5f, 0.8f),    // Spring Green
        new Color(1f, 0f, 0.5f, 0.8f),    // Rose
        new Color(0.5f, 1f, 0f, 0.8f),    // Chartreuse
        new Color(0f, 0.5f, 1f, 0.8f),    // Azure
    };

    private int? closestPointIndex;
    private SevenPointPartitionerPartType latestDraggedPartType = SevenPointPartitionerPartType.Point;
    private bool isDragging = false;
    private bool isCameraDragging = false;
    private bool hasCollinearPoints = false;

    private Vector3 lastMousePosition;

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
        triangleColors.Clear();

        if (points.Count != 7 || hasCollinearPoints)
        {
            hasValidTriangles = false;
            return;
        }

        // Step 1: Find all qualifying lines (2-3 or 1-4 splits, nudged to 3-4 splits)
        List<HalfPlane> qualifyingLines = new List<HalfPlane>();

        foreach (var halfPlane in halfPlanes)
        {
            if (IsQualifyingLine(halfPlane))
            {
                qualifyingLines.Add(halfPlane);
            }
        }

        // Step 2: Find valid pairs that satisfy LEMMA 2
        List<(HalfPlane, HalfPlane)> validPairs = new List<(HalfPlane, HalfPlane)>();

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
        int colorIndex = 0;
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
                            var triangleHalfPlanes = CreateTriangleHalfPlanes(line1, line2, line3, colorIndex);

                            var triple = new HalfPlaneTriple(triangleHalfPlanes.line1, triangleHalfPlanes.line2, triangleHalfPlanes.line3);
                            validPartitionTriangles.Add(triple);

                            // Assign unique color to this triangle
                            Color triangleColor = validTriangleColors[colorIndex % validTriangleColors.Length];
                            triangleColors.Add(triangleColor);

                            colorIndex++;
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
    private bool IsQualifyingLine(HalfPlane halfPlane)
    {
        Vector2 a = halfPlane.inputPoint1.position;
        Vector2 b = halfPlane.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;
        int onLineCount = 0;

        // Count points on each side of the line (excluding the two points defining the line)
        foreach (Point p in points)
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
            // Try moving points on the line to each side and see if we get a valid split
            int totalPoints = leftCount + rightCount + onLineCount;

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
    private bool SatisfiesLemma2(HalfPlane line1, HalfPlane line2)
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
        Vector2 intersection;
        if (GetLineIntersection(line1, line2, out intersection))
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
    private bool CreatesUniquePartitions(HalfPlane line1, HalfPlane line2, HalfPlane line3)
    {
        HashSet<int> partitionCodes = new HashSet<int>();

        foreach (Point point in points)
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
    private bool GetLineIntersection(HalfPlane line1, HalfPlane line2, out Vector2 intersection)
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
        foreach (Point p in points)
        {
            float dist = p.Position.magnitude;
            if (dist > maxDist) maxDist = dist;
        }
        return maxDist;
    }

    /// <summary>
    /// Helper method to get a description of a line for debugging
    /// </summary>
    private string GetLineDescription(HalfPlane line)
    {
        int index1 = points.FindIndex(p => p.transform == line.inputPoint1);
        int index2 = points.FindIndex(p => p.transform == line.inputPoint2);
        return $"P{index1}-P{index2}";
    }

    // List to keep track of instantiated triangle half-planes for cleanup
    private List<HalfPlane> triangleHalfPlanes = new List<HalfPlane>();

    /// <summary>
    /// Creates new half-plane instances for a triangle with proper visual styling
    /// </summary>
    /// <param name="originalLine1">Original qualifying line 1</param>
    /// <param name="originalLine2">Original qualifying line 2</param>
    /// <param name="originalLine3">Original qualifying line 3</param>
    /// <param name="colorIndex">Index for triangle color</param>
    /// <returns>Tuple of the three new half-plane instances</returns>
    private (HalfPlane line1, HalfPlane line2, HalfPlane line3) CreateTriangleHalfPlanes(
        HalfPlane originalLine1, HalfPlane originalLine2, HalfPlane originalLine3, int colorIndex)
    {
        Color triangleColor = validTriangleColors[colorIndex % validTriangleColors.Length];

        // Create first half-plane
        GameObject halfPlaneObj1 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane halfPlane1 = halfPlaneObj1.GetComponent<HalfPlane>();
        SetupTriangleHalfPlane(halfPlane1, originalLine1, triangleColor);

        // Create second half-plane
        GameObject halfPlaneObj2 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane halfPlane2 = halfPlaneObj2.GetComponent<HalfPlane>();
        SetupTriangleHalfPlane(halfPlane2, originalLine2, triangleColor);

        // Create third half-plane
        GameObject halfPlaneObj3 = Instantiate(halfPlanePrefab, Vector3.zero, Quaternion.identity);
        HalfPlane halfPlane3 = halfPlaneObj3.GetComponent<HalfPlane>();
        SetupTriangleHalfPlane(halfPlane3, originalLine3, triangleColor);

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
    /// <param name="triangleColor">The color for this triangle</param>
    private void SetupTriangleHalfPlane(HalfPlane halfPlane, HalfPlane originalLine, Color triangleColor)
    {
        // Copy the endpoints from the original line
        halfPlane.inputPoint1 = originalLine.inputPoint1;
        halfPlane.inputPoint2 = originalLine.inputPoint2;
        halfPlane.parentSevenPointPartitioner = this;

        // Set visual properties for triangle display
        halfPlane.colour = triangleColor;
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
    /// Generates a diverse set of colors for the 128 possible point inclusion combinations
    /// </summary>
    /// <returns>Array of 128 distinct colors</returns>
    private static Color[] GeneratePointInclusionColors()
    {
        Color[] colors = new Color[128];

        for (int i = 0; i < 128; i++)
        {
            // Use different approaches to generate visually distinct colors
            // We'll use HSV color space for better distribution

            // Method 1: Use bit patterns to influence hue, saturation, and value
            float hue = (i * 137.508f) % 360f / 360f; // Golden angle for better distribution
            float saturation = 0.6f + 0.4f * ((i % 3) / 2f); // Vary saturation
            float value = 0.7f + 0.3f * ((i % 5) / 4f); // Vary brightness

            colors[i] = Color.HSVToRGB(hue, saturation, value);
        }

        // Ensure some key combinations have recognizable colors
        colors[0] = Color.black;    // No points included
        colors[127] = Color.white;  // All points included

        return colors;
    }

    /// <summary>
    /// Computes the point inclusion for a given half-plane and the ordered set of 7 points
    /// </summary>
    /// <param name="halfPlane">The half-plane to test against</param>
    /// <returns>A tuple of 7 booleans indicating inclusion for each point (p1, p2, p3, p4, p5, p6, p7)</returns>
    public (bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) PointInclusions(HalfPlane halfPlane)
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

        return (p1, p2, p3, p4, p5, p6, p7);
    }

    /// <summary>
    /// Checks if a point is in the right half-plane of a line (including the line itself)
    /// "Right" is defined by treating the second point as the "forward" direction from the first
    /// </summary>
    /// <param name="point">The point to test</param>
    /// <param name="halfPlane">The half-plane defined by two points</param>
    /// <returns>True if the point is on the right side of the line or on the line</returns>
    private bool IsPointInHalfPlaneRight(Vector2 point, HalfPlane halfPlane)
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

        return pointInclusionColors[colorIndex];
    }

    /// <summary>
    /// Updates the colors of all half-planes based on their point inclusions
    /// </summary>
    public void UpdateHalfPlaneColorsFromPointInclusions()
    {
        if (!enablePointInclusionColoring || points.Count < 7)
        {
            return;
        }

        // Update colors for main half-planes (skip those that are part of valid triangles)
        foreach (HalfPlane halfPlane in halfPlanes)
        {
            // Check if this half-plane is part of a valid triangle
            bool isPartOfValidTriangle = validPartitionTriangles.Any(triangle =>
                triangle.halfPlaneA == halfPlane ||
                triangle.halfPlaneB == halfPlane ||
                triangle.halfPlaneC == halfPlane);

            if (!isPartOfValidTriangle)
            {
                var inclusions = PointInclusions(halfPlane);
                Color newColor = GetColorFromPointInclusions(inclusions);

                // Set alpha to make the half-planes semi-transparent
                newColor.a = 0.3f;
                halfPlane.colour = newColor;

                // Reset thickness for non-triangle lines
                halfPlane.Thickness = -1f; // Use default thickness
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
    /// This version uses left-side inclusion for the original half-plane coloring system
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
        List<Point> allPoints = points;
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

        // Reset highlights
        foreach (Point point in points)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            points[closestPointIndexInAllPoints.Value].Highlight(true);

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

        // Handle triangle cycling
        if (hasValidTriangles && validPartitionTriangles.Count > 1)
        {
            // Handle spacebar input for toggling debug lines
            if (Input.GetKeyDown(KeyCode.Space))
            {
                currentTriangleIndex = (currentTriangleIndex + 1) % validPartitionTriangles.Count;
                UpdateTriangleVisibility();
            }
        }

        // Reset highlights
        foreach (Point point in points)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            points[closestPointIndexInAllPoints.Value].Highlight(true);
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

        //UpdatePointColorsFromHalfPlaneInclusions();
        //UpdateHalfPlaneColorsFromPointInclusions();
        FindValidPartitionTriangles();
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
