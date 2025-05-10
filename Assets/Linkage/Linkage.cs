using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public enum LinkagePartType
{
    Joint = 0,
    HalfBar,
}

/// <summary>
/// There is at most 1 joint out of a pair of opposing joints which has angle which would break the
/// constraint of being less than min distance imposed by the opposing bars and similarly for being
/// more than max distance imposed by the opposing bars.
/// 
/// Proof sketch:
/// For one, a pair of adjacent bars has a sum of lengths either less than, equal to or more than that
/// of the opposing pair of bars and in each of those cases the opposing bars therefore have a sum of
/// lengths that is more than, equal to or less than the first pair respectively.
/// 
/// Also, when an adjacent pair of bars are at a limit of an angular range, this can only
/// be because the other pair have reached a minim or maximum, which occur when they are collinear.
/// The fact that they've reached a collinear positioned demonstrates that they didn't themselves have an
/// angular restriction there and may pass through continuously the other half of their angular range.
/// 
/// Conversely, any joint which allows a collinear angle is hitting a minimum or maximum distance of
/// endpoints and so if the opposing joint isn't also hitting a collinear angle when the first joint
/// does, then it won't for any angle because other angles can only result in less extreme distances
/// of endpoints and therefore there will be a (possibly zero) range of no solutions for the other
/// joint beyond the angle it reached at that extreme.
/// </summary>
public class Linkage : MonoBehaviour
{
    public List<Joint> joints;
    public GameObject halfBarPrefab;  // Prefab for a half bar
    readonly float barVisibleThickness = 0.1f;
    readonly float barColliderThicknessSize = 10f;
    readonly float baseJointColliderThickness = 0.1f; // Base collider thickness for joints
    readonly float baseBarColliderThickness = 0.1f; // Base collider thickness for bars

    private HalfBar[] halfBars;

    private Joint closestJoint;
    private HalfBar closestHalfBar;
    private LinkagePartType latestDraggedPartType = LinkagePartType.Joint;
    private bool isDragging = false; // Flag to indicate if dragging is in progress
    private bool isCameraDragging = false; // Flag to indicate if camera dragging is in progress

    private Vector3 lastMousePosition; // To store the last mouse position for camera dragging

    float jointColliderThickness; // In fact isn't the collider thickness but that's how it appears provided it's smaller than the true collider thickness.
    float barColliderThickness;
    private void Awake()
    {
        if (joints == null || joints.Count != 4)
        {
            Debug.LogError("4 joints are required.");
            return;
        }

        foreach (var joint in joints)
        {
            joint.parentLinkage = this;
        }

        // Initialize halfBars arrays
        halfBars = new HalfBar[joints.Count * 2];

        // Create Bar objects
        for (int i = 0; i < joints.Count; i++)
        {
            // Create half bars
            GameObject halfBarObj1 = Instantiate(halfBarPrefab, transform);
            GameObject halfBarObj2 = Instantiate(halfBarPrefab, transform);
            halfBars[i * 2] = halfBarObj1.GetComponent<HalfBar>();
            halfBars[i * 2 + 1] = halfBarObj2.GetComponent<HalfBar>();

            // Assign DraggableHalfBar components and their references
            HalfBar halfBar1 = halfBarObj1.GetComponent<HalfBar>();
            HalfBar halfBar2 = halfBarObj2.GetComponent<HalfBar>();

            // Ensure half bars are created properly with the necessary components
            if (halfBar1 != null && halfBar2 != null)
            {
                halfBar1.Initialize(this, joints[i], joints[Maths.mod(i + 1, joints.Count)], joints[Maths.mod(i + 2, joints.Count)], joints[Maths.mod(i + 3, joints.Count)]);
                halfBar2.Initialize(this, joints[i], joints[Maths.mod(i - 1, joints.Count)], joints[Maths.mod(i - 2, joints.Count)], joints[Maths.mod(i - 3, joints.Count)]);
            }
            else
            {
                Debug.LogError("DraggableHalfBar component missing on half bar prefab.");
            }
        }

        UpdateBars();
        UpdateSelectionRadius();
    }

    public void UpdateBars()
    {
        if (joints == null || joints.Count < 2) return;

        for (int i = 0; i < joints.Count; i++)
        {
            int nextIndex = (i + 1) % joints.Count;
            int prevIndex = (i - 1 + joints.Count) % joints.Count;
            Transform currentJoint = joints[i].transform;
            Transform nextJoint = joints[nextIndex].transform;
            Transform prevJoint = joints[prevIndex].transform;

            // Calculate the position and scale for the full bar
            Vector3 direction = nextJoint.position - currentJoint.position;

            // Calculate and update half bar positions, rotations, and scales
            Vector3 nextDirection = (nextJoint.position - currentJoint.position) / 2;
            Vector3 prevDirection = (prevJoint.position - currentJoint.position) / 2;
            float nextHalfDistance = nextDirection.magnitude;
            float prevHalfDistance = prevDirection.magnitude;

            halfBars[i * 2].transform.position = currentJoint.position;
            halfBars[i * 2].transform.right = nextDirection;
            halfBars[i * 2].transform.localScale = new Vector3(nextHalfDistance, barVisibleThickness, 1);
            halfBars[i * 2].GetComponent<BoxCollider2D>().size = new Vector2(1, barColliderThicknessSize);

            halfBars[i * 2 + 1].transform.position = currentJoint.position;
            halfBars[i * 2 + 1].transform.right = prevDirection;
            halfBars[i * 2 + 1].transform.localScale = new Vector3(prevHalfDistance, barVisibleThickness, 1);
            halfBars[i * 2 + 1].GetComponent<BoxCollider2D>().size = new Vector2(1, barColliderThicknessSize);
        }
    }

    public void MoveJoint(Joint joint, Vector3 position)
    {
        if (joint != null)
        {
            joint.transform.position = position;
            UpdateBars();
        }
    }

    public (Joint closestJoint, float closestDistance) FindClosestJoint(Vector3 position)
    {
        Joint closest = null;
        float minDistance = float.MaxValue;

        foreach (Joint joint in joints)
        {
            float distance = Vector3.Distance(position, joint.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = joint;
            }
        }

        return (closestJoint: closest, closestDistance: minDistance);
    }

    public (HalfBar closestHalfBar, float closestDistance)? FindClosestHalfBar(Vector3 inputPosition)
    {
        Edge3D[] edges = halfBars.Select(bar => new Edge3D("", bar.pivotJoint.transform.position, (bar.pivotJoint.transform.position + bar.adjacentJoint.transform.position) / 2)).ToArray();

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
            return (closestHalfBar: halfBars[nearestEdgeIndex.Value], closestDistance: nearestPointDistanceOnEdge);
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

        var closestJointData = FindClosestJoint(pointerPos);
        var closestJointDistance = closestJointData.closestDistance;

        if (closestJointDistance < jointColliderThickness)
        {
            closestJoint = closestJointData.closestJoint;
            closestHalfBar = null;
        }
        else
        {
            var closestHalfBarData = FindClosestHalfBar(pointerPos);
            if (closestHalfBarData.HasValue && closestHalfBarData.Value.closestDistance <= barColliderThickness)
            {
                closestJoint = null;
                closestHalfBar = closestHalfBarData.Value.closestHalfBar;
            }
            else
            {
                closestJoint = null;
                closestHalfBar = null;
            }
        }

        // Handle mouse wheel input to extend or contract the hovered half bar
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (closestHalfBar != null && Mathf.Abs(scrollInput) > 0.01f)
        {
            AdjustHalfBarLength(closestHalfBar, scrollInput);
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
        if (Input.GetMouseButtonDown(0) && closestJoint == null && closestHalfBar == null)
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
        foreach (Joint joint in joints)
        {
            joint.Highlight(false);
            joint.SetAngleRanges(0, 0, false, Color.red, 0, 0, false, Color.yellow);
        }
        foreach (HalfBar halfBar in halfBars)
        {
            halfBar.Highlight(false);
        }

        // Set latest closest highlight
        if (closestJoint != null)
        {
            closestJoint.Highlight(true);
        }

        if (closestHalfBar != null)
        {
            closestHalfBar.Highlight(true);

            pivotBeforeDrag = closestHalfBar.pivotJoint.transform.position;
            adjBeforeDrag = closestHalfBar.adjacentJoint.transform.position;
            altBeforeDrag = closestHalfBar.alternativeAdjacentJoint.transform.position;
            oppBeforeDrag = closestHalfBar.oppositeJoint.transform.position;

            // Calculate angle ranges to display to user
            pivotToAdjDist = (adjBeforeDrag - pivotBeforeDrag).magnitude;
            adjToOppDist = (oppBeforeDrag - adjBeforeDrag).magnitude;
            oppToAltDist = (altBeforeDrag - oppBeforeDrag).magnitude;
            altToPivotDist = (pivotBeforeDrag - altBeforeDrag).magnitude;
            UpdateAngleRanges(altToPivotDist, oppToAltDist, adjToOppDist, pivotToAdjDist);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true; // Set the dragging flag to true
        if (closestJoint != null)
        {
            latestDraggedPartType = LinkagePartType.Joint;
            OnBeginDragJoint(eventData);
        }
        else if (closestHalfBar != null)
        {
            latestDraggedPartType = LinkagePartType.HalfBar;
            OnBeginDragHalfBar(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (latestDraggedPartType == LinkagePartType.Joint)
        {
            OnDragJoint(eventData);
        }
        else
        {
            OnDragHalfBar(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false; // Reset the dragging flag to false
        if (latestDraggedPartType == LinkagePartType.Joint)
        {
            OnEndDragJoint(eventData);
        }
        else
        {
            OnEndDragHalfBar(eventData);
        }
    }

    public void OnBeginDragJoint(PointerEventData eventData)
    {

    }

    public void OnDragJoint(PointerEventData eventData)
    {
        if (closestJoint != null)
        {
            // Convert screen position to world position
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0; // Ensure the joint stays on the z = 0 plane

            // Move the joint via the LinkageManager
            MoveJoint(closestJoint, worldPoint);
        }
    }

    public void OnEndDragJoint(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestJoint = null;
    }

    public void OnBeginDragHalfBar(PointerEventData eventData)
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

    public void UpdateAngleRanges(float altToPivotDist, float oppToAltDist, float adjToOppDist, float pivotToAdjDist)
    {
        var intersectionData1 = Maths.CircleCircleIntersectionXAndY(altToPivotDist, oppToAltDist + adjToOppDist, pivotToAdjDist);
        var intersectionData2 = Maths.CircleCircleIntersectionXAndY(altToPivotDist, Mathf.Abs(oppToAltDist - adjToOppDist), pivotToAdjDist);

        bool showAngle1 = intersectionData1.IntersectionsExist;
        bool showAngle2 = intersectionData2.IntersectionsExist;
        float degreesBetweenExtremes1 = 0;
        float degreesBetweenExtremes2 = 0;
        if (showAngle1)
        {
            var x1 = intersectionData1.IntersectionDistanceFromOriginAlongLineConnectingOrigins;
            var y1 = intersectionData1.HalfSeparationOfIntersections;
            degreesBetweenExtremes1 = 360f - Mathf.Rad2Deg * 2 * Mathf.Atan2(y1, x1);
        }
        if (showAngle2)
        {
            var x2 = intersectionData2.IntersectionDistanceFromOriginAlongLineConnectingOrigins;
            var y2 = intersectionData2.HalfSeparationOfIntersections;
            degreesBetweenExtremes2 = Mathf.Rad2Deg * 2 * Mathf.Atan2(y2, x2);
        }

        Vector3 pivotToAlt = altBeforeDrag - pivotBeforeDrag;
        float degreesCCWFromDownOfCentre1 = Vector3.SignedAngle(-pivotToAlt, Vector3.down, Vector3.back);
        float degreesCCWFromDownOfCentre2 = Vector3.SignedAngle(pivotToAlt, Vector3.down, Vector3.back);
        closestHalfBar.pivotJoint.SetAngleRanges(degreesCCWFromDownOfCentre1, degreesBetweenExtremes1, showAngle1, Color.red, degreesCCWFromDownOfCentre2, degreesBetweenExtremes2, showAngle2, Color.yellow);
    }

    Vector3 lastAdj = Vector3.zero;
    Vector3 lastOpp = Vector3.zero;
    public void OnDragHalfBar(PointerEventData eventData)
    {
        if (closestHalfBar != null)
        {
            // Calculate new linkage position
            // First calculate possible positions for opposite joint
            float minDistViaOpp = Mathf.Abs(adjToOppDist - oppToAltDist);
            float maxDistViaOpp = adjToOppDist + oppToAltDist;
            Vector3 worldPoint = ScreenToWorldPoint(eventData.position);
            worldPoint.z = 0; // Ensure the joint stays on the z = 0 plane
            Vector3 direction = (worldPoint - closestHalfBar.pivotJoint.transform.position).normalized;
            Vector3 adjTarget = pivotBeforeDrag + direction * Vector3.Distance(pivotBeforeDrag, adjBeforeDrag);
            Vector3 adjTargetToAlt = altBeforeDrag - adjTarget;
            float adjTargetToAltDist = adjTargetToAlt.magnitude;
            var (solutionsExist, oppTarget_1, oppTarget_2) = GetPossibleOppPositions(adjTargetToAltDist, minDistViaOpp, maxDistViaOpp, oppToAltDist, adjToOppDist, adjTarget, adjTargetToAlt);

            // If solutions don't exist then show linkage before dragging
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
            closestHalfBar.adjacentJoint.transform.position = adjTarget;
            closestHalfBar.oppositeJoint.transform.position = newOpp;
            UpdateBars();
        }
    }

    public void OnEndDragHalfBar(PointerEventData eventData)
    {
        // Optional: Handle end drag logic if needed
        closestHalfBar.pivotJoint.SetAngleRanges(0f, 0f, false, Color.red, 0f, 0f, false, Color.yellow);
        closestHalfBar = null;
    }

    private Vector3 ScreenToWorldPoint(Vector2 screenPosition)
    {
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -Camera.main.transform.position.z);
        return Camera.main.ScreenToWorldPoint(screenPoint);
    }

    private void AdjustHalfBarLength(HalfBar halfBar, float scrollAmount)
    {
        float lengthChange = scrollAmount * 0.1f; // Adjust the 0.1f to control the rate of length change
        Vector3 direction = (halfBar.adjacentJoint.transform.position - halfBar.pivotJoint.transform.position).normalized;
        halfBar.adjacentJoint.transform.position += direction * lengthChange;

        // Update all bars after changing the length
        UpdateBars();
    }

    public void StartDragging(LinkagePartType partType)
    {
        latestDraggedPartType = partType;
        isDragging = true;

        pivotBeforeDrag = closestHalfBar.pivotJoint.transform.position;
        adjBeforeDrag = closestHalfBar.adjacentJoint.transform.position;
        altBeforeDrag = closestHalfBar.alternativeAdjacentJoint.transform.position;
        oppBeforeDrag = closestHalfBar.oppositeJoint.transform.position;
        pivotToAdjDist = Vector3.Distance(pivotBeforeDrag, adjBeforeDrag);
        adjToOppDist = Vector3.Distance(adjBeforeDrag, oppBeforeDrag);
        oppToAltDist = Vector3.Distance(altBeforeDrag, oppBeforeDrag);
        altToPivotDist = Vector3.Distance(altBeforeDrag, pivotBeforeDrag);
    }

    public void StopDragging()
    {
        isDragging = false;
    }

    public void RestoreJointsToBeforeDrag()
    {
        closestHalfBar.pivotJoint.transform.position = pivotBeforeDrag;
        closestHalfBar.adjacentJoint.transform.position = adjBeforeDrag;
        closestHalfBar.alternativeAdjacentJoint.transform.position = altBeforeDrag;
        closestHalfBar.oppositeJoint.transform.position = oppBeforeDrag;
    }

    private void UpdateSelectionRadius()
    {
        float zoomFactor = Camera.main.orthographicSize;
        jointColliderThickness = baseJointColliderThickness * zoomFactor;
        barColliderThickness = baseBarColliderThickness * zoomFactor;
    }
}
