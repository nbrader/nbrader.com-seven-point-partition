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
public struct LineWithPerpArrowTriple
{
    public LineWithPerpArrow_MB lineWithPerpArrowA;
    public LineWithPerpArrow_MB lineWithPerpArrowB;
    public LineWithPerpArrow_MB lineWithPerpArrowC;

    public LineWithPerpArrowTriple(LineWithPerpArrow_MB a, LineWithPerpArrow_MB b, LineWithPerpArrow_MB c)
    {
        lineWithPerpArrowA = a;
        lineWithPerpArrowB = b;
        lineWithPerpArrowC = c;
    }
}

public class SevenPointPartitioner_MB : MonoBehaviour
{
    /// <summary>
    /// The input points to the partition finding problem.
    /// </summary>
    public List<Point_MB> points;

    public GameObject lineWithPerpArrowPrefab;

    public TextMeshProUGUI warningText; // UI Text component to display warnings
    public TextMeshProUGUI solutionCountText; // UI Text component to current selected solution out of how many
    public static readonly float lineVisibleThickness = 0.1f;
    readonly float basePointColliderThickness = 0.1f;
    float pointColliderThickness;

    // Add a base scale for the points when the camera is at its default orthographic size
    [Header("Point Scaling")] //
    public float basePointVisualScale = 0.5f; //

    List<LineWithPerpArrow_MB> linesWithPerpArrows;

    // Half-plane inclusion coloring
    [Header("Line With Perp Arrow Inclusion Coloring")]
    public LineWithPerpArrowTriple coloringTriple;
    public bool enableLineWithPerpArrowColoring = true;

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

    private readonly List<LineWithPerpArrowTriple> validPartitionTriangles = new();

    // Triangle cycling variables
    private int currentTriangleIndex = 0;
    private bool hasValidTriangles = false;

    private bool isPinching = false;
    private float lastPinchDistance = 0f;

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

        InitializeLinesWithPerpArrowsFromPoints();
        UpdateSelectionRadius();
        UpdatePointVisualScale();
        FindValidPartitionTriangles();
    }

    private void InitializeLinesWithPerpArrowsFromPoints()
    {
        linesWithPerpArrows = new List<LineWithPerpArrow_MB>();

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < points.Count; j++)
            {
                if (i != j)
                {
                    GameObject lineWithPerpArrowObj = Instantiate(lineWithPerpArrowPrefab, Vector3.zero, Quaternion.identity);
                    LineWithPerpArrow_MB lineWithPerpArrow = lineWithPerpArrowObj.GetComponent<LineWithPerpArrow_MB>();

                    lineWithPerpArrow.inputPoint1 = points[i].transform;
                    lineWithPerpArrow.inputPoint2 = points[j].transform;
                    lineWithPerpArrow.parentSevenPointPartitioner = this;

                    // Assign debug color
                    Color color = Color.green;
                    color.a = 0.18f;
                    lineWithPerpArrow.colour = color;

                    // Register visibility rule - hide if collinear points exist
                    lineWithPerpArrow.ShouldBeVisible += line => !hasCollinearPoints && ShouldShowLineWithPerpArrow(line);
                    lineWithPerpArrow.ForceHidden += _ => hideNonDebugLines;

                    linesWithPerpArrows.Add(lineWithPerpArrow);
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
        List<LineWithPerpArrow_MB> qualifyingLines = new();
        List<(bool, bool, bool, bool, bool, bool, bool)> qualifyingInclusions = new();

        foreach (var lineWithPerpArrow in linesWithPerpArrows)
        {
            if (IsQualifyingLine(lineWithPerpArrow))
            {
                (bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) inclusions = PointInclusions(lineWithPerpArrow);
                if (!qualifyingInclusions.Contains(inclusions))
                {
                    qualifyingLines.Add(lineWithPerpArrow);
                    qualifyingInclusions.Add(inclusions);
                }
            }
        }

        // Step 2: Find valid pairs that satisfy LEMMA 2
        List<(LineWithPerpArrow_MB, LineWithPerpArrow_MB)> validPairs = new();

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

        // Clean up any previously created triangle line with perp arrows
        CleanupTriangleLinesWithPerpArrows();

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
                        // Check if this triple creates unique partitions for all 7 points (1/1/1/1/1/1/1)
                        if (CreatesUniquePartitions(line1, line2, line3))
                        {
                            // Create new line with perp arrow instances for this triangle
                            var triangleLinesWithPerpArrows = CreateTriangleLinesWithPerpArrows(line1, line2, line3);

                            var triple = new LineWithPerpArrowTriple(triangleLinesWithPerpArrows.line1, triangleLinesWithPerpArrows.line2, triangleLinesWithPerpArrows.line3);
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
    private void SetTriangleVisibility(LineWithPerpArrowTriple triangle, bool visible)
    {
        if (triangle.lineWithPerpArrowA != null)
            triangle.lineWithPerpArrowA.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
        if (triangle.lineWithPerpArrowB != null)
            triangle.lineWithPerpArrowB.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
        if (triangle.lineWithPerpArrowC != null)
            triangle.lineWithPerpArrowC.gameObject.SetActive(visible && showValidPartitionTriangles && !hasCollinearPoints);
    }

    /// <summary>
    /// Enhanced version that properly implements the nudging concept from the algorithm
    /// </summary>
    private bool IsQualifyingLine(LineWithPerpArrow_MB lineWithPerpArrow)
    {
        Vector2 a = lineWithPerpArrow.inputPoint1.position;
        Vector2 b = lineWithPerpArrow.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;
        int onLineCount = 0;

        // Count points on each side of the line (excluding the two points defining the line)
        foreach (Point_MB p in points)
        {
            if (p.transform == lineWithPerpArrow.inputPoint1 || p.transform == lineWithPerpArrow.inputPoint2)
                continue;

            Vector2 pt = p.Position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            if (cross == 0)
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
    private bool SatisfiesLemma2(LineWithPerpArrow_MB line1, LineWithPerpArrow_MB line2)
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
    private bool CreatesUniquePartitions(LineWithPerpArrow_MB line1, LineWithPerpArrow_MB line2, LineWithPerpArrow_MB line3)
    {
        HashSet<int> partitionCodes = new();

        foreach (Point_MB point in points)
        {
            Vector2 pos = point.Position;

            // Get inclusion in each line with perp arrow (using consistent orientation)
            bool in1 = IsPointInLineWithPerpArrowRight(pos, line1);
            bool in2 = IsPointInLineWithPerpArrowRight(pos, line2);
            bool in3 = IsPointInLineWithPerpArrowRight(pos, line3);

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
    private bool GetLineIntersection(LineWithPerpArrow_MB line1, LineWithPerpArrow_MB line2, out Vector2 intersection)
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

    // List to keep track of instantiated triangle line with perp arrows for cleanup
    private readonly List<LineWithPerpArrow_MB> triangleLinesWithPerpArrows = new();

    /// <summary>
    /// Creates new line with perp arrow instances for a triangle with proper visual styling
    /// </summary>
    /// <param name="originalLine1">Original qualifying line 1</param>
    /// <param name="originalLine2">Original qualifying line 2</param>
    /// <param name="originalLine3">Original qualifying line 3</param>
    /// <returns>Tuple of the three new line with perp arrow instances</returns>
    private (LineWithPerpArrow_MB line1, LineWithPerpArrow_MB line2, LineWithPerpArrow_MB line3) CreateTriangleLinesWithPerpArrows(
        LineWithPerpArrow_MB originalLine1, LineWithPerpArrow_MB originalLine2, LineWithPerpArrow_MB originalLine3)
    {
        // Create first line with perp arrow
        GameObject lineWithPerpArrowObj1 = Instantiate(lineWithPerpArrowPrefab, Vector3.zero, Quaternion.identity);
        LineWithPerpArrow_MB lineWithPerpArrow1 = lineWithPerpArrowObj1.GetComponent<LineWithPerpArrow_MB>();
        var inclusions1 = PointInclusions(originalLine1);
        Color newColor1 = GetColorFromPointInclusions(inclusions1);
        SetupTriangleLineWithPerpArrow(lineWithPerpArrow1, originalLine1, newColor1);

        // Create second line with perp arrow
        GameObject lineWithPerpArrowObj2 = Instantiate(lineWithPerpArrowPrefab, Vector3.zero, Quaternion.identity);
        LineWithPerpArrow_MB lineWithPerpArrow2 = lineWithPerpArrowObj2.GetComponent<LineWithPerpArrow_MB>();
        var inclusions2 = PointInclusions(originalLine2);
        Color newColor2 = GetColorFromPointInclusions(inclusions2);
        SetupTriangleLineWithPerpArrow(lineWithPerpArrow2, originalLine2, newColor2);

        // Create third line with perp arrow
        GameObject lineWithPerpArrowObj3 = Instantiate(lineWithPerpArrowPrefab, Vector3.zero, Quaternion.identity);
        LineWithPerpArrow_MB lineWithPerpArrow3 = lineWithPerpArrowObj3.GetComponent<LineWithPerpArrow_MB>();
        var inclusions3 = PointInclusions(originalLine3);
        Color newColor3 = GetColorFromPointInclusions(inclusions3);
        SetupTriangleLineWithPerpArrow(lineWithPerpArrow3, originalLine3, newColor3);

        // Add to our tracking list for cleanup
        triangleLinesWithPerpArrows.Add(lineWithPerpArrow1);
        triangleLinesWithPerpArrows.Add(lineWithPerpArrow2);
        triangleLinesWithPerpArrows.Add(lineWithPerpArrow3);

        return (lineWithPerpArrow1, lineWithPerpArrow2, lineWithPerpArrow3);
    }

    /// <summary>
    /// Sets up a triangle line with perp arrow with proper configuration
    /// </summary>
    /// <param name="lineWithPerpArrow">The line with perp arrow to configure</param>
    /// <param name="originalLine">The original line to copy configuration from</param>
    /// <param name="color">The color for this triangle</param>
    private void SetupTriangleLineWithPerpArrow(LineWithPerpArrow_MB lineWithPerpArrow, LineWithPerpArrow_MB originalLine, Color color)
    {
        // Copy the endpoints from the original line
        lineWithPerpArrow.inputPoint1 = originalLine.inputPoint1;
        lineWithPerpArrow.inputPoint2 = originalLine.inputPoint2;
        lineWithPerpArrow.parentSevenPointPartitioner = this;

        // Set visual properties for triangle display
        lineWithPerpArrow.colour = color;
        lineWithPerpArrow.Thickness = validTriangleThickness;

        // Triangle visibility will be controlled by the cycling system
        lineWithPerpArrow.ShouldBeVisible += _ => true; // Always allow visibility (controlled by SetActive)
        lineWithPerpArrow.ForceHidden += _ => false; // Don't force hide triangle lines
    }

    /// <summary>
    /// Cleans up previously created triangle line with perp arrows
    /// </summary>
    private void CleanupTriangleLinesWithPerpArrows()
    {
        foreach (var lineWithPerpArrow in triangleLinesWithPerpArrows)
        {
            if (lineWithPerpArrow != null && lineWithPerpArrow.gameObject != null)
            {
                DestroyImmediate(lineWithPerpArrow.gameObject);
            }
        }
        triangleLinesWithPerpArrows.Clear();
    }

    /// <summary>
    /// Call this method when the component is destroyed or when you need to clean up all triangles
    /// </summary>
    private void OnDestroy()
    {
        CleanupTriangleLinesWithPerpArrows();
    }

    /// <summary>
    /// Computes the point inclusion for a given line with perp arrow and the ordered set of 7 points
    /// </summary>
    /// <param name="lineWithPerpArrow">The line with perp arrow to test against</param>
    /// <returns>A tuple of 7 booleans indicating inclusion for each point (p1, p2, p3, p4, p5, p6, p7)</returns>
    public (bool p1, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7) PointInclusions(LineWithPerpArrow_MB lineWithPerpArrow)
    {
        if (points.Count < 7)
        {
            Debug.LogWarning("PointInclusions requires exactly 7 points, but only " + points.Count + " are available.");
            return (false, false, false, false, false, false, false);
        }

        bool p1 = IsPointInLineWithPerpArrowRight(points[0].Position, lineWithPerpArrow);
        bool p2 = IsPointInLineWithPerpArrowRight(points[1].Position, lineWithPerpArrow);
        bool p3 = IsPointInLineWithPerpArrowRight(points[2].Position, lineWithPerpArrow);
        bool p4 = IsPointInLineWithPerpArrowRight(points[3].Position, lineWithPerpArrow);
        bool p5 = IsPointInLineWithPerpArrowRight(points[4].Position, lineWithPerpArrow);
        bool p6 = IsPointInLineWithPerpArrowRight(points[5].Position, lineWithPerpArrow);
        bool p7 = IsPointInLineWithPerpArrowRight(points[6].Position, lineWithPerpArrow);

        var all_ps = new List<bool>() { p1, p2, p3, p4, p5, p6, p7 };

        if (p1)
        {
            all_ps = all_ps.Select(x => !x).ToList();
        }

        return (all_ps[0], all_ps[1], all_ps[2], all_ps[3], all_ps[4], all_ps[5], all_ps[6]);
    }

    /// <summary>
    /// Checks if a point is in the right line with perp arrow of a line (including the line itself)
    /// "Right" is defined by treating the second point as the "forward" direction from the first
    /// </summary>
    /// <param name="point">The point to test</param>
    /// <param name="lineWithPerpArrow">The line with perp arrow defined by two points</param>
    /// <returns>True if the point is on the right side of the line or on the line</returns>
    private bool IsPointInLineWithPerpArrowRight(Vector2 point, LineWithPerpArrow_MB lineWithPerpArrow)
    {
        if (lineWithPerpArrow == null || lineWithPerpArrow.inputPoint1 == null || lineWithPerpArrow.inputPoint2 == null)
            return false;

        Vector2 a = lineWithPerpArrow.inputPoint1.position; // First point
        Vector2 b = lineWithPerpArrow.inputPoint2.position; // Second point (defines "forward")

        // Calculate the cross product to determine which side of the line the point is on
        // For a line from A to B, and point P:
        // cross = (B.x - A.x) * (P.y - A.y) - (B.y - A.y) * (P.x - A.x)
        // If cross <= 0, point is on the right side or on the line (when looking from A towards B)
        // If cross > 0, point is on the left side
        float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

        // For the right line with perp arrow (including boundary), we want cross <= 0
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
    ///// Computes the line with perp arrow inclusion for a given point and ordered triple of line with perp arrows
    ///// </summary>
    ///// <param name="point">The point to test</param>
    ///// <param name="triple">The ordered triple of line with perp arrows (h_a, h_b, h_c)</param>
    ///// <returns>A tuple (inA, inB, inC) indicating inclusion in each line with perp arrow</returns>
    //public (bool inA, bool inB, bool inC) LineWithPerpArrowInclusions(Vector2 point, LineWithPerpArrowTriple triple)
    //{
    //    bool inA = IsPointInLineWithPerpArrow(point, triple.lineWithPerpArrowA);
    //    bool inB = IsPointInLineWithPerpArrow(point, triple.lineWithPerpArrowB);
    //    bool inC = IsPointInLineWithPerpArrow(point, triple.lineWithPerpArrowC);

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
        float minDistance = Vector3.Distance(position, point.Position);
        int closest = 0;

        for (int i = 1; i < allPoints.Count; i++)
        {
            point = allPoints[i];
            float distance = Vector3.Distance(position, point.Position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = i;
            }
        }

        return (closestPoint: closest, closestDistance: minDistance);
    }

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

        // Handle desktop scroll wheel zoom
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            HandleZoom(scrollInput);
        }

        // Handle mobile pinch zoom
        HandleMobilePinchZoom();

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
            // Handle spacebar input for cycling through solutions
            if (Input.GetMouseButtonDown((int)MouseButton.Middle)) // Middle mouse button for next
            {
                currentTriangleIndex = Maths.mod(currentTriangleIndex + 1, validPartitionTriangles.Count);
                UpdateTriangleVisibility();
            }
            else if (Input.GetMouseButtonDown((int)MouseButton.Right)) // Right mouse button for previous
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
            solutionCountText.text = string.Format("Solution {0} out of {1}.", currentTriangleIndex + 1, validPartitionTriangles.Count);
        }

        // Reset highlights
        foreach (Point_MB point in points)
            point.Highlight(false);

        if (closestPointIndexInAllPoints != null)
            points[closestPointIndexInAllPoints.Value].Highlight(true);
    }

    private void HandleZoom(float zoomDelta)
    {
        scrollAmount -= zoomDelta;
        scrollAmount = Mathf.Clamp(scrollAmount, -5f, 5f);
        Camera.main.orthographicSize = Mathf.Pow(5, 1 + scrollAmount / 5f);
        Camera.main.orthographicSize = Mathf.Clamp(Camera.main.orthographicSize, -50f, 50f);
        UpdateSelectionRadius();
        UpdatePointVisualScale();
    }

    private void HandleMobilePinchZoom()
    {
        // Only process touch input if we have exactly 2 touches
        if (Input.touchCount == 2)
        {
            Touch touch1 = Input.GetTouch(0);
            Touch touch2 = Input.GetTouch(1);

            // Calculate current distance between touches
            float currentPinchDistance = Vector2.Distance(touch1.position, touch2.position);

            if (!isPinching)
            {
                // Start pinching
                isPinching = true;
                lastPinchDistance = currentPinchDistance;
            }
            else
            {
                // Continue pinching - calculate zoom based on distance change
                float deltaDistance = currentPinchDistance - lastPinchDistance;

                // Convert distance change to zoom factor (adjust sensitivity as needed)
                float zoomSensitivity = 0.01f;
                float zoomDelta = deltaDistance * zoomSensitivity;

                HandleZoom(zoomDelta);

                lastPinchDistance = currentPinchDistance;
            }
        }
        else
        {
            // End pinching when we don't have exactly 2 touches
            isPinching = false;
        }
    }

    /// <summary>
    /// Updates the visual scale of all points based on the current camera orthographic size.
    /// This makes them appear to maintain a constant size on screen.
    /// </summary>
    private void UpdatePointVisualScale()
    {
        if (Camera.main != null && Camera.main.orthographic) // Ensure it's an orthographic camera
        {
            float orthographicSize = Camera.main.orthographicSize; //
            // The scale factor should be proportional to the orthographic size.
            // If orthographicSize is 1, the scale is basePointVisualScale.
            // If orthographicSize is 2, the scale is 2 * basePointVisualScale.
            float currentPointScale = basePointVisualScale * orthographicSize; //

            foreach (var point in points) //
            {
                point.SetSpriteScale(currentPointScale); // Use the new method in Point_MB
            }
        }
    }

    private bool ShouldShowLineWithPerpArrow(Line_MB lineWithPerpArrow)
    {
        // Don't show line with perp arrows if we have collinear points
        if (hasCollinearPoints) return false;

        Vector2 a = lineWithPerpArrow.inputPoint1.position;
        Vector2 b = lineWithPerpArrow.inputPoint2.position;

        int leftCount = 0;
        int rightCount = 0;

        foreach (Point_MB p in points)
        {
            if (p.transform == lineWithPerpArrow.inputPoint1 || p.transform == lineWithPerpArrow.inputPoint2)
                continue;

            Vector2 pt = p.Position;
            float cross = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);

            if (cross != 0f)
                continue; // Point is on the lineWithPerpArrow

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

            // Update solutions in real-time during dragging
            FindValidPartitionTriangles();
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
