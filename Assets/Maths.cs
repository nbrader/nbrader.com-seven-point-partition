using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;

public enum MotionType
{
    Linear, LeftTurn, RightTurn
}

public class Arc
{
    public MotionType motionType;

    public Vector2 p0;
    public Vector2 p1;

    public float r;

    public Vector2 c;

    public Arc(MotionType motionType_, Vector2 p0_, Vector2 p1_, float r_, Vector2 c_)
    {
        motionType = motionType_;
        p0 = p0_;
        p1 = p1_;
        r = r_;
        c = c_;
    }

    public float ArcLength()
    {
        float startAngle = 0.0f;
        float endAngle = 0.0f;

        switch (motionType)
        {
            case MotionType.Linear:
                return (p1 - p0).magnitude;
            case MotionType.LeftTurn:
            case MotionType.RightTurn:
                Vector2 startDir = (p0 - c).normalized;
                Vector2 endDir = (p1 - c).normalized;

                startAngle = Maths.AngleFromVec2(startDir);
                endAngle = Maths.AngleFromVec2(endDir);

                break;
        }

        float normalisedangleBetween = Maths.NormaliseAnglePositiveRad(endAngle - startAngle);
        float angleBetween = 0.0f;
        switch (motionType)
        {
            case MotionType.LeftTurn:
                angleBetween = normalisedangleBetween;
                break;
            case MotionType.RightTurn:
                angleBetween = 2 * Mathf.PI - normalisedangleBetween;
                break;
        }

        return r * angleBetween;
    }
}

public class Pair<T, U>
{
    public Pair()
    {
    }

    public Pair(T first, U second)
    {
        this.First = first;
        this.Second = second;
    }

    public T First { get; set; }
    public U Second { get; set; }
};

public class Interval
{
    public float lower;
    public float upper;

    public bool boundedBelow;
    public bool boundedAbove;

    public Interval(float lower_, float upper_, bool boundedBelow_ = true, bool boundedAbove_ = true)
    {
        Assert.IsTrue(lower_ <= upper_);

        lower = lower_;
        upper = upper_;

        boundedBelow = boundedBelow_;
        boundedAbove = boundedAbove_;
    }
    
    public bool Contains(float x)
    {
        return (!boundedBelow || x >= lower) && (!boundedAbove || x <= upper);
    }

    public float ProgressFromLowerToUpper(float x)
    {
        return Maths.InverseLerp(lower, upper, x);
    }

    public float Clamp(float x)
    {
        return Mathf.Clamp(x, boundedBelow ? lower : float.NegativeInfinity, boundedAbove ? upper : float.PositiveInfinity);
    }
    
    public float UpperMinusLower()
    {
        return upper - lower;
    }
}

public class IntervalDirectionless
{
    public float lower;
    public float upper;

    public bool boundedBelow;
    public bool boundedAbove;

    public IntervalDirectionless(float lower_, float upper_, bool boundedBelow_ = true, bool boundedAbove_ = true)
    {
        lower = lower_;
        upper = upper_;

        boundedBelow = boundedBelow_;
        boundedAbove = boundedAbove_;
    }

    public bool Contains(float x)
    {
        return (!boundedBelow || x >= lower) && (!boundedAbove || x <= upper);
    }

    public float ProgressFromLowerToUpper(float x)
    {
        return Maths.InverseLerp(lower, upper, x);
    }

    public float Clamp(float x)
    {
        return Mathf.Clamp(x, boundedBelow ? lower : float.NegativeInfinity, boundedAbove ? upper : float.PositiveInfinity);
    }

    public float UpperMinusLower()
    {
        return upper - lower;
    }
}

public class Phase
{
    public enum Type
    {
        Linear,
        Parabolic
    }

    Type type = Type.Linear;

    float c0 = 0.0f;
    float c1 = 0.0f;
    float c2 = 0.0f;

    public Phase(Type type_, float c0_, float c1_, float c2_)
    {
        type = type_;
        c0 = c0_;
        c1 = c1_;
        c2 = c2_;
    }

    public float Evaluate(float time)
    {
        switch (type)
        {
            case Type.Linear:
                return c1 * time + c0;
            case Type.Parabolic:
            default:
                return c2 * time * time + c1 * time + c0;
        }
    }

    public float Derivative(float time)
    {
        switch (type)
        {
            case Type.Linear:
                return c1;
            case Type.Parabolic:
            default:
                return c2*time + c1;
        }
    }
}

public class CurveOfPhases
{
    public List<Pair<Interval, Phase>> IntervalToPhaseList { get; set; }

    // optimisation based on usage of progressing through consecutive intervals in evaluation
    int firstCheckedPhase = 0;

    public CurveOfPhases(List<Pair<Interval, Phase>> intervalToPhaseList)
    {
        IntervalToPhaseList = intervalToPhaseList;
    }

    public float Evaluate(float time)
    {
        Interval interval = null;
        Phase phase = null;

        for (int i = 0; i < IntervalToPhaseList.Count; i++)
        {
            int i_ = (firstCheckedPhase + i) % IntervalToPhaseList.Count;
            Pair<Interval, Phase> intervalPhasePair = IntervalToPhaseList[i_];

            interval = intervalPhasePair.First;
            phase = intervalPhasePair.Second;

            if (interval.Contains(time))
            {
                firstCheckedPhase = i_;
                return phase.Evaluate(time);
            }
        }

        throw new System.Exception("CurveOfPhases.Evaluate() failed to find an interval containing progress point.");
    }
        
    public float Derivative(float time)
    {
        Interval interval = null;
        Phase phase = null;

        for (int i = 0; i < IntervalToPhaseList.Count; i++)
        {
            int i_ = (firstCheckedPhase + i) % IntervalToPhaseList.Count;
            Pair<Interval, Phase> intervalPhasePair = IntervalToPhaseList[i_];

            interval = intervalPhasePair.First;
            phase = intervalPhasePair.Second;

            if (interval.Contains(time))
            {
                firstCheckedPhase = i_;
                return phase.Derivative(time);
            }
        }

        throw new System.Exception("CurveOfPhases.Derivative() failed to find an interval containing progress point.");
    }
}

// optimisation to make: add equiv of firstCheckedPhase of above CurveOfPhases to below 
public class CurveOfArcs
{
    public List<Pair<Interval, Arc>> IntervalToArcList { get; set; }
    public float TotalDistance { get; set; }
        
    // optimisation based on usage of progressing through consecutive intervals in evaluation
    int firstCheckedPhase = 0;

    public void SetArcs(List<Arc> arcs)
    {
        TotalDistance = 0.0f;
        IntervalToArcList = new List<Pair<Interval, Arc>>();

        // create IntervalToArcList from arc list
        // and sum the arclengths for TotalDistance
        foreach (Arc arc in arcs)
        {
            float arcLength = arc.ArcLength();

            IntervalToArcList.Add(new Pair<Interval, Arc>(new Interval(TotalDistance, TotalDistance + arcLength, true, true), arc));
            TotalDistance += arc.ArcLength();
        }
    }

    public void SetIntervalArcsPairsAndTotal(List<Pair<Interval, Arc>> intervalArcPairs, float totalDistance)
    {
        IntervalToArcList = intervalArcPairs;
        TotalDistance = totalDistance;
    }

    public Vector2 positionFromDistance(float distance)
    {
        distance = Maths.mod(distance, TotalDistance);
        
        for (int i = 0; i < IntervalToArcList.Count; i++)
        {
            int i_ = (firstCheckedPhase + i) % IntervalToArcList.Count;
            Pair<Interval, Arc> intervalArcPair = IntervalToArcList[i_];

            Interval interval = intervalArcPair.First;
            Arc arc = intervalArcPair.Second;

            if (interval.Contains(distance))
            {
                firstCheckedPhase = i_;
                return positionOnArc(arc, interval.ProgressFromLowerToUpper(distance));
            }
        }

        throw new System.Exception("CurveOfArcs.position() failed to find an interval containing progress point.");
    }

    public Vector2 positionOnArc(Arc arc, float arcProgress)
    {
        float startAngle = 0.0f;
        float endAngle = 0.0f;

        switch (arc.motionType)
        {
            case MotionType.Linear:
                return Maths.LerpVec2(arc.p0, arc.p1, arcProgress);
            case MotionType.LeftTurn:
            case MotionType.RightTurn:
                Vector2 startDir = (arc.p0 - arc.c).normalized;
                Vector2 endDir = (arc.p1 - arc.c).normalized;

                startAngle = Maths.AngleFromVec2(startDir);
                endAngle = Maths.AngleFromVec2(endDir);

                break;
        }

        float currentAngle;
        switch (arc.motionType)
        {
            case MotionType.LeftTurn:
                while (endAngle < startAngle)
                    endAngle += 2 * Mathf.PI;

                currentAngle = Maths.Lerp(startAngle, endAngle, arcProgress);

                return arc.c + arc.r * Maths.Vec2FromAngle(currentAngle);
            case MotionType.RightTurn:
            default:
                while (startAngle < endAngle)
                    startAngle += 2 * Mathf.PI;

                currentAngle = Maths.Lerp(startAngle, endAngle, arcProgress);

                return arc.c + arc.r * Maths.Vec2FromAngle(currentAngle);
        }
    }
}

public class Maths
{
    public readonly static float radiansPerTurn   = 2*Mathf.PI;
    public readonly static float degreesPerTurn   = 360f;
    public readonly static float degreesPerRadian = degreesPerTurn/radiansPerTurn;

    public static int IntPow(int x, int pow)
    {
        int ret = 1;
        while (pow != 0)
        {
            if ((pow & 1) == 1)
                ret *= x;
            x *= x;
            pow >>= 1;
        }
        return ret;
    }

    public static float NormaliseAnglePositiveRad(float angle)
    {
        return mod(angle, 2*Mathf.PI);
    }

    public static float NormaliseAngleBelow180Deg(float angle)
    {
        return mod(angle + 180f, 360f) - 180f;
    }

    public static Vector2 Vec2FromAngle(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    public static float AngleFromVec2(Vector2 v)
    {
        float angle = Mathf.Atan2(v.y, v.x);

        return NormaliseAnglePositiveRad(angle);
    }

    public static Vector2 rotateV2LeftBy90(Vector2 v)
    {
        return new Vector2(-v.y, v.x);
    }

    public static Vector2 rotateV2RightBy90(Vector2 v)
    {
        return new Vector2(v.y, -v.x);
    }

    public static Vector2 HomotheticCenter(float r0, float r1, Vector2 c0, Vector2 c1)
    {
        return (r1 / (r0 + r1)) * c0 + (r0 / (r0 + r1)) * c1;
    }

    // Gives displacements from circle centre of entry point for tangent to circle through a point
    // split into:
    //  u = displacement along line joining centre
    //  v = displacement perpendicular to u
    public static Pair<Vector2, Vector2> GenericDispsForPointToCircle(float r1, Vector2 p, Vector2 c)
    {
        Vector2 centreDisplacement = (p - c);

        float centreDistance = centreDisplacement.magnitude;
        float r1Sqrd = r1 * r1;

        float uLength = r1Sqrd / centreDistance;
        float vLength = Mathf.Sqrt(r1Sqrd - uLength * uLength);

        Vector2 unitU = centreDisplacement / centreDistance;
        Vector2 unitV = rotateV2LeftBy90(unitU);

        Vector2 u = uLength * unitU;
        Vector2 v = vLength * unitV;

        return new Pair<Vector2, Vector2>(u, v);
    }

    public static Pair<Vector2, Vector2> EdgeFromPointToPoint(Vector2 c0, Vector2 c1)
    {
        Vector2 p0 = c0;
        Vector2 p1 = c1;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromPointToLeftTurn(float r1, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for point within circle

        Pair<Vector2, Vector2> e = GenericDispsForPointToCircle(r1, c0, c1);

        Vector2 u = e.First;
        Vector2 v = e.Second;

        Vector2 p0 = c0;
        Vector2 p1 = c1 + u + v;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromPointToRightTurn(float r1, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for point within circle

        Pair<Vector2, Vector2> e = GenericDispsForPointToCircle(r1, c0, c1);

        Vector2 u = e.First;
        Vector2 v = e.Second;

        Vector2 p0 = c0;
        Vector2 p1 = c1 + u - v;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromLeftTurnToPoint(float r0, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for point within circle

        Pair<Vector2, Vector2> e = GenericDispsForPointToCircle(r0, c1, c0);

        Vector2 u = e.First;
        Vector2 v = e.Second;

        Vector2 p0 = c0 + u - v;
        Vector2 p1 = c1;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromLeftTurnToLeftTurn(float r0, float r1, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for nested circles
        Vector2 centreDisplacement = c1 - c0;

        Vector2 DummyVector = Vector2.zero;
        if (centreDisplacement.magnitude + r0 < r1 || centreDisplacement.magnitude + r1 < r0)
        {
            return new Pair<Vector2, Vector2>(DummyVector, DummyVector);
        }

        Vector2 p0;
        Vector2 p1;
        if (r0 == r1)
        {
            // if radii are equal then we can't use HomotheticCenter
            // instead, use the face that the points are an offset version
            // of the circle centres (offset by their common radius)
            Vector2 perpOnRight = r0 * rotateV2RightBy90(centreDisplacement.normalized);

            p0 = c0 + perpOnRight;
            p1 = p0 + centreDisplacement;

            return new Pair<Vector2, Vector2>(p0, p1);
        }

        // if radii aren't equal then we can use HomotheticCenter
        // to split the problem into traveling to and from this point:
        //
        //    Circle1 -> HomotheticCenter -> Circle2
        //
        Vector2 homCentre = HomotheticCenter(-r0, r1, c0, c1);

        Pair<Vector2, Vector2> e0 = GenericDispsForPointToCircle(r0, homCentre, c0);
        Pair<Vector2, Vector2> e1 = GenericDispsForPointToCircle(r1, homCentre, c1);

        Vector2 u0 = e0.First;
        Vector2 v0 = e0.Second;

        Vector2 u1 = e1.First;
        Vector2 v1 = e1.Second;

        if (r0 > r1)
        {
            p0 = c0 + u0 - v0;
            p1 = c1 + u1 - v1;
        }
        else
        {
            p0 = c0 + u0 + v0;
            p1 = c1 + u1 + v1;
        }

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromLeftTurnToRightTurn(float r0, float r1, Vector2 c0, Vector2 c1)
    {
        Vector2 centreDisplacement = c1 - c0;

        // doesn't make sense for intersecting circles
        Vector2 DummyVector = Vector2.zero;
        if (centreDisplacement.magnitude < r0 + r1)
        {
            return new Pair<Vector2, Vector2>(DummyVector, DummyVector);
        }

        Vector2 homCentre = HomotheticCenter(r0, r1, c0, c1);

        Pair<Vector2, Vector2> e0 = GenericDispsForPointToCircle(r0, homCentre, c0);
        Pair<Vector2, Vector2> e1 = GenericDispsForPointToCircle(r1, homCentre, c1);

        Vector2 u0 = e0.First;
        Vector2 v0 = e0.Second;

        Vector2 u1 = e1.First;
        Vector2 v1 = e1.Second;

        Vector2 p0 = c0 + u0 - v0;
        Vector2 p1 = c1 + u1 - v1;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromRightTurnToPoint(float r0, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for point within circle
        Pair<Vector2, Vector2> e = GenericDispsForPointToCircle(r0, c1, c0);

        Vector2 u = e.First;
        Vector2 v = e.Second;

        Vector2 p0 = c0 + u + v;
        Vector2 p1 = c1;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromRightTurnToLeftTurn(float r0, float r1, Vector2 c0, Vector2 c1)
    {
        Vector2 centreDisplacement = c1 - c0;

        // doesn't make sense for intersecting circles
        Vector2 DummyVector = Vector2.zero;
        if (centreDisplacement.magnitude < r0 + r1)
        {
            return new Pair<Vector2, Vector2>(DummyVector, DummyVector);
        }

        Vector2 homCentre = HomotheticCenter(r0, r1, c0, c1);

        Pair<Vector2, Vector2> e0 = GenericDispsForPointToCircle(r0, homCentre, c0);
        Pair<Vector2, Vector2> e1 = GenericDispsForPointToCircle(r1, homCentre, c1);

        Vector2 u0 = e0.First;
        Vector2 v0 = e0.Second;

        Vector2 u1 = e1.First;
        Vector2 v1 = e1.Second;

        Vector2 p0 = c0 + u0 + v0;
        Vector2 p1 = c1 + u1 + v1;

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static Pair<Vector2, Vector2> EdgeFromRightTurnToRightTurn(float r0, float r1, Vector2 c0, Vector2 c1)
    {
        // doesn't make sense for nested circles
        Vector2 centreDisplacement = c1 - c0;

        Vector2 DummyVector = Vector2.zero;
        if (centreDisplacement.magnitude + r0 < r1 || centreDisplacement.magnitude + r1 < r0)
        {
            return new Pair<Vector2, Vector2>(DummyVector, DummyVector);
        }

        Vector2 p0;
        Vector2 p1;
        if (r0 == r1)
        {
            // if radii are equal then we can't use HomotheticCenter
            // instead, use the face that the points are an offset version
            // of the circle centres (offset by their common radius)
            Vector2 perpOnLeft = r0 * rotateV2LeftBy90(centreDisplacement.normalized);

            p0 = c0 + perpOnLeft;
            p1 = p0 + centreDisplacement;

            return new Pair<Vector2, Vector2>(p0, p1);
        }

        // if radii aren't equal then we can use HomotheticCenter
        // to split the problem into traveling to and from this point:
        //
        //    Circle1 -> HomotheticCenter -> Circle2
        //
        Vector2 homCentre = HomotheticCenter(-r0, r1, c0, c1);

        Pair<Vector2, Vector2> e0 = GenericDispsForPointToCircle(r0, homCentre, c0);
        Pair<Vector2, Vector2> e1 = GenericDispsForPointToCircle(r1, homCentre, c1);

        Vector2 u0 = e0.First;
        Vector2 v0 = e0.Second;

        Vector2 u1 = e1.First;
        Vector2 v1 = e1.Second;

        if (r0 < r1)
        {
            p0 = c0 + u0 - v0;
            p1 = c1 + u1 - v1;
        }
        else
        {
            p0 = c0 + u0 + v0;
            p1 = c1 + u1 + v1;
        }

        return new Pair<Vector2, Vector2>(p0, p1);
    }

    public static float mod(float x, float m)
    {
        float r = x % m;
        return r < 0 ? r + m : r;
    }

    public static int mod(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    public static float Lerp( float v0
                            , float v1
                            , float t )
    {
        return v0 * (1.0f - t) + v1 * t;
    }

    // my own version of Mathf.InverseLerp without the annoying clamping I don't want.
    public static float InverseLerp( float v0
                                   , float v1
                                   , float v)
    {
        return (v - v0) / (v1 - v0);
    }

    //public static float QuadraticInterpAFromValsAndEndRate( float x0
    //                                                      , float x1
    //                                                      , float v0)
    //{
    //    return x1 - x0 - v0;
    //}

    //public static float QuadraticInterpAFromRates( float v0
    //                                             , float v1)
    //{
    //    return (v1 - v0) / 2;
    //}

    //public static float QuadraticInterpEndVal( float x0
    //                                         , float x1
    //                                         , float v0)
    //{
    //    float a = QuadraticInterpAFromValsAndEndRate(x0, x1, v0);
    //    return a + x0 + v0;
    //}

    //public static float QuadraticInterpEndRate( float x0
    //                                          , float v0
    //                                          , float v1)
    //{
    //    float a = QuadraticInterpAFromRates(v0, v1);
    //    return 2*a + v0;
    //}

    //public static float QuadraticInterp( float a
    //                                   , float b
    //                                   , float c
    //                                   , float t)
    //{
    //    return a*t*t + b*t + c;
    //}

    public static Vector3 LerpVec3(Vector3 v0
                                    , Vector3 v1
                                    , float t)
    {
        return new Vector3(Lerp(v0.x, v1.x, t), Lerp(v0.y, v1.y, t), Lerp(v0.z, v1.z, t));
    }

    public static Vector2 LerpVec2( Vector2 v0
                                    , Vector2 v1
                                    , float t)
    {
        return new Vector2(Lerp(v0.x, v1.x, t), Lerp(v0.y, v1.y, t));
    }

    public static Vector2 ProjectVec3DownY(Vector3 v)
    {
        return new Vector2(v.x, v.z);
    }

    public static Vector3 UnprojectVec3DownY(Vector2 v)
    {
        return new Vector3(v.x, 0.0f, v.y);
    }

    public static Vector3 UnprojectVec3DownZ(Vector2 v)
    {
        return new Vector3(v.x, v.y, 0.0f);
    }

    public static float Rad2Deg(float x)
    {
        return 180.0f * (x / Mathf.PI);
    }
    public static float Deg2Rad(float x)
    {
        return Mathf.PI * (x / 180.0f);
    }

    public static bool ListsEqual(List<int> list1, List<int> list2)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }

        for (int i = 0; i < list1.Count; i++)
        {
            if (list1[i] != list2[i])
            {
                return false;
            }
        }

        return true;
    }

    public static bool ListsEqual<T>(List<T> list1, List<T> list2)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }

        for (int i = 0; i < list1.Count; i++)
        {
            if (Convert.ToInt32(list1[i]) != Convert.ToInt32(list2[i]))
            {
                return false;
            }
        }

        return true;
    }

    public enum CardinalDirection
    {
        None = 0,
        Up,
        Down,
        Left,
        Right
    }

    static public CardinalDirection GetCardinalDirection(float x, float y, float skew = 1f, float deadZoneRadius = 0f)
    {
        float distanceSquared = x * x + y * y;
        if (distanceSquared <= deadZoneRadius * deadZoneRadius)
        {
            return CardinalDirection.None;
        }
        else
        {
            bool inUpperLeft  = y >  x*skew;
            bool inUpperRight = y > -x*skew;

            if (inUpperLeft)
            {
                if (inUpperRight) { return CardinalDirection.Up; }
                else { return CardinalDirection.Left; }
            }
            else
            {
                if (inUpperRight) { return CardinalDirection.Right; }
                else { return CardinalDirection.Down; }
            }
        }
    }

    static public int RandomIndex(float[] weights)
    {
        int noOfOptions = weights.Length;
        float[] cumulativeWeights = new float [noOfOptions];
        Array.Copy(weights, cumulativeWeights, noOfOptions);
        float totalWeight = 0f;

        for (int i = 0; i < noOfOptions; i++)
        {
            totalWeight += weights[i];
            cumulativeWeights[i] = totalWeight;
        }
        
        float r = UnityEngine.Random.Range(0f, totalWeight);

        for (int i = 0; i < noOfOptions; i++)
        {
            if (r < cumulativeWeights[i])
            {
                return i;
            }
        }

        return noOfOptions - 1;
    }

    public static int HashString(string stringToHash)
    {
        MD5 md5Hasher = MD5.Create();
        var hashed = md5Hasher.ComputeHash(Encoding.UTF8.GetBytes(stringToHash));
        return Math.Abs(BitConverter.ToInt32(hashed, 0));
    }

    public static int HashStrings(string[] stringsToHash)
    {
        string stringToHash = "";
        foreach (string s in stringsToHash)
        {
            stringToHash += s + "\u001f";
        }

        return HashString(stringToHash);
    }

    /// <summary>
    /// Useful for playable characters or turrets.
    /// With this function, "Forwards" reverses when you bend backwards.
    /// If instead you want "Forwards" to remain unchanged, use FeetRotFromRightEarDir().
    /// </summary>
    public static Quaternion FeetRotFromNoseDir(Vector3 noseDirection)
    {
        Quaternion rightRot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, noseDirection));
        Vector3 forward = rightRot * Vector3.left;
        return Quaternion.LookRotation(forward);
    }

    /// <summary>
    /// Useful for playable characters or turrets.
    /// With this function, "Forwards" remains unchanged when you bend backwards.
    /// If instead you want "Forwards" to reverse, use FeetRotFromNoseDir().
    /// </summary>
    public static Quaternion FeetRotFromRightEarDir(Vector3 rightEarDirection)
    {
        // NB: It makes sense that forward maintains it's direction despite you leaning back/forward and
        // the fact that leaning far left or right reverse direction is a logical consequence of this
        // as it's indistinguishable from leaning back/forward from the opposite direction.
        return Quaternion.LookRotation(Vector3.Cross(rightEarDirection, Vector3.up));
    }

    public static (bool IntersectionsExist, float IntersectionDistanceFromOriginAlongLineConnectingOrigins, float HalfSeparationOfIntersections) CircleCircleIntersectionXAndY(float distanceFromOriginOfCentreOfSecondCircle, float radiusOfSecondCircle, float radiusOfCircleAtOrigin)
    {
        float x = (distanceFromOriginOfCentreOfSecondCircle * distanceFromOriginOfCentreOfSecondCircle - radiusOfSecondCircle * radiusOfSecondCircle + radiusOfCircleAtOrigin * radiusOfCircleAtOrigin) / (2 * distanceFromOriginOfCentreOfSecondCircle);

        float discrimanant = radiusOfCircleAtOrigin * radiusOfCircleAtOrigin - x * x;

        if (discrimanant < 0)
        {
            return (IntersectionsExist: false, IntersectionDistanceFromOriginAlongLineConnectingOrigins: x, HalfSeparationOfIntersections: 0);
        }
        else
        {
            float y = Mathf.Sqrt(discrimanant);
            return (IntersectionsExist: true, IntersectionDistanceFromOriginAlongLineConnectingOrigins: x, HalfSeparationOfIntersections: y);
        }
    }

    /// <summary>
    /// Maps the full range of floats to fractions of unity (using sinh).
    /// </summary>
    /// <param name="x"></param>
    /// <returns></returns>
    public float ToFrac(float x)
    {
        return (Sinh(x) + 1) / 2f;
    }

    public float Sinh(float x)
    {
        return (Mathf.Exp(x) - Mathf.Exp(-x)) / 2f;
    }
}

class Bits
{
    public static int SetBit(int bitString, int pos, bool newValue)
    {
        bool oldValue = TestBit(bitString, pos);

        if (newValue != oldValue)
        {
            return bitString ^ Bit(pos);
        }
        else
        {
            return bitString;
        }
    }

    public static bool TestBit(int bitString, int pos)
    {
        return (bitString & Bit(pos)) != 0;
    }

    public static int Bit(int pos)
    {
        return 1 << pos;
    }
}

public class Maybe<T>
{
    public bool exists;
    public T value;

    public Maybe(bool exists_, T value_)
    {
        exists = exists_;
        value = value_;
    }

    public Maybe(bool exists_)
    {
        exists = exists_;
    }

    public static Maybe<T> Just(T value_)
    {
        return new Maybe<T>(true, value_);
    }

    public static Maybe<T> Nothing()
    {
        return new Maybe<T>(false);
    }
}

public class Polyhedron
{
    public string name;
    public Face[] faces;
    public Edge3D[] edges;
    public Vector3[] triCorners;

    public Polyhedron(string name_, Face[] faces_, Edge3D[] edges_, Vector3[] corners_)
    {
        name = name_;
        faces = faces_;
        edges = edges_;
        triCorners = corners_;
    }

    public Polyhedron ExcludeAbove(string newName, float inputHeight)
    {
        // Sort faces into excluded / chopped / preserved according to whether they are entirely above or below inputHeight or overlap it
        List<Face> preservedFaces = new List<Face>();
        List<Face> choppedFaces = new List<Face>();
        foreach (Face face in faces)
        {
            float minHeight = float.PositiveInfinity;
            foreach (Three3DPoints tri in face.triangles)
            {
                minHeight = Mathf.Min(minHeight, tri.p1.y, tri.p2.y, tri.p3.y);
            }

            float maxHeight = float.NegativeInfinity;
            foreach (Three3DPoints tri in face.triangles)
            {
                maxHeight = Mathf.Max(minHeight, tri.p1.y, tri.p2.y, tri.p3.y);
            }

            Interval edgeHeightInterval = new Interval(minHeight, maxHeight);

            if (inputHeight > maxHeight)
            {
                preservedFaces.Add(face);
            }
            else if (edgeHeightInterval.Contains(inputHeight))
            {
                choppedFaces.Add(face);
            }
        }

        // Sort edges into excluded / chopped / preserved according to whether they are entirely above or below inputHeight or overlap it
        List<Edge3D> preservedEdges = new List<Edge3D>();
        List<Edge3D> choppedEdges = new List<Edge3D>();
        foreach (Edge3D edge in edges)
        {
            float minHeight = Mathf.Min(edge.p1.y, edge.p2.y);
            float maxHeight = Mathf.Max(edge.p1.y, edge.p2.y);

            Interval edgeHeightInterval = new Interval(minHeight, maxHeight);

            if (inputHeight > maxHeight)
            {
                preservedEdges.Add(edge);
            }
            else if (edgeHeightInterval.Contains(inputHeight))
            {
                choppedEdges.Add(edge);
            }
        }

        // Sort corners into excluded / preserved according to whether they are entirely above or below inputHeight or overlap it
        List<Vector3> preservedCorners = new List<Vector3>();
        foreach (Vector3 corner in triCorners)
        {
            if (corner.y <= inputHeight)
            {
                preservedCorners.Add(corner);
            }
        }


        // Populate afterChopFaces with parts of faces below inputHeight and afterChopEdges with any new edges wherever a tri got chopped
        List<Edge3D> afterChopEdges = new List<Edge3D>();
        List<Face> afterChopFaces = new List<Face>();
        foreach (Face face in choppedFaces)
        {
            // exclude / chop / preserve triangles in face in the same way as in code above this and below
            List<Three3DPoints> preservedTris = new List<Three3DPoints>();
            List<Three3DPoints> choppedTris = new List<Three3DPoints>();

            foreach (Three3DPoints tri in face.triangles)
            {
                float minHeight = float.PositiveInfinity;
                minHeight = Mathf.Min(minHeight, tri.p1.y, tri.p2.y, tri.p3.y);

                float maxHeight = float.NegativeInfinity;
                maxHeight = Mathf.Max(minHeight, tri.p1.y, tri.p2.y, tri.p3.y);

                Interval edgeHeightInterval = new Interval(minHeight, maxHeight);

                if (inputHeight > maxHeight)
                {
                    preservedTris.Add(tri);
                }
                else if (edgeHeightInterval.Contains(inputHeight))
                {
                    choppedTris.Add(tri);
                }
            }

            // Populate afterChopFaces with parts of faces below inputHeight and afterChopEdges with any new edges wherever a tri got chopped
            List<Three3DPoints> afterChopTris = new List<Three3DPoints>();
            foreach (Three3DPoints tri in choppedTris)
            {
                Edge3D newEdge;
                afterChopTris.AddRange(tri.ExcludeAbove(inputHeight, out newEdge));
                afterChopEdges.Add(newEdge);
            }

            List<Three3DPoints> newTris = new List<Three3DPoints>();

            newTris.AddRange(preservedTris);
            newTris.AddRange(afterChopTris);

            if (choppedTris.Count > 0)
            {
                afterChopFaces.Add(new Face(face.name, face.plane, newTris.ToArray()));
            }
        }

        // Populate afterChopEdges with parts of edges below inputHeight
        foreach (Edge3D edge in choppedEdges)
        {
            Edge3D edgeAfterChop = edge.ExcludeAbove(edge.name, inputHeight);
            afterChopEdges.Add(edgeAfterChop);
        }


        List<Face> newFaces = new List<Face>();
        List<Edge3D> newEdges = new List<Edge3D>();
        List<Vector3> newCorners = new List<Vector3>();

        newFaces.AddRange(preservedFaces);
        newFaces.AddRange(afterChopFaces);

        newEdges.AddRange(preservedEdges);
        newEdges.AddRange(afterChopEdges);

        newCorners.AddRange(preservedCorners);

        return new Polyhedron(newName, newFaces.ToArray(), newEdges.ToArray(), newCorners.ToArray());
    }

    public Vector3 GetNearestPoint(Vector3 inputPointPos)
    {
        Vector3 nearestPoint = Vector3.zero;

        // Check _FACES_ for nearest point
        Vector3 nearestPointOnFace = Vector3.zero;
        float nearestPointDistanceOnFace = float.PositiveInfinity;
        bool nearestPointFoundOnFace = false;
        foreach (Face face in faces)
        {
            Geometry.BasisDir projectionDir = face.preferredProjectionDir;

            Vector3 inputPointPosToPlane = Geometry.NearestPointOfPlane(inputPointPos, face.plane.p1, face.plane.p2, face.plane.p3);

            bool faceContainsInputPointPosToPlane = false;
            foreach (Three3DPoints triangle in face.triangles)
            {
                Vector2 inputPointPosDownDir = Geometry.ProjectDownDir(inputPointPosToPlane, projectionDir);
                Triangle2D projectedTriangle = triangle.ProjectDownDir(projectionDir);
                if (projectedTriangle.GetArea() == 0f)
                {
                    Debug.LogError("Degenerate triangle found.");
                }

                faceContainsInputPointPosToPlane |= projectedTriangle.Contains(inputPointPosDownDir);
            }

            if (faceContainsInputPointPosToPlane)
            {
                float thisPointDistance = (inputPointPosToPlane - inputPointPos).magnitude;

                if (nearestPointFoundOnFace)
                {
                    if (thisPointDistance < nearestPointDistanceOnFace)
                    {
                        nearestPointOnFace = inputPointPosToPlane;
                        nearestPointDistanceOnFace = thisPointDistance;
                    }
                }
                else
                {
                    nearestPointFoundOnFace = true;
                    nearestPointOnFace = inputPointPosToPlane;
                    nearestPointDistanceOnFace = thisPointDistance;
                }
            }
        }

        // Check _EDGES_ for nearest point
        Vector3 nearestPointOnEdge = Vector3.zero;
        float nearestPointDistanceOnEdge = float.PositiveInfinity;
        bool nearestPointFoundOnEdge = false;
        foreach (Edge3D edge in edges)
        {
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

            Vector3 inputPointPosToEdge = Geometry.NearestPointOfLineFromPoints(inputPointPos, edge.p1, edge.p2);
            Interval projectedInterval = edge.ProjectToDir(projectionDir);

            float inputPointPosToDir = Geometry.ProjectToDir(inputPointPosToEdge, projectionDir);

            bool edgeContainsInputPointPosToEdge = projectedInterval.Contains(inputPointPosToDir);

            if (edgeContainsInputPointPosToEdge)
            {
                float thisPointDistance = (inputPointPosToEdge - inputPointPos).magnitude;

                if (nearestPointFoundOnEdge)
                {
                    if (thisPointDistance < nearestPointDistanceOnEdge)
                    {
                        nearestPointOnEdge = inputPointPosToEdge;
                        nearestPointDistanceOnEdge = thisPointDistance;
                    }
                }
                else
                {
                    nearestPointFoundOnEdge = true;
                    nearestPointOnEdge = inputPointPosToEdge;
                    nearestPointDistanceOnEdge = thisPointDistance;
                }
            }
        }

        // Check _CORNERS_ for nearest point
        Vector3 nearestPointAtCorner = Vector3.zero;
        float nearestPointDistanceAtCorner = float.PositiveInfinity;
        bool nearestPointFoundAtCorner = false;
        foreach (Vector3 corner in triCorners)
        {
            float thisPointDistance = (corner - inputPointPos).magnitude;

            if (nearestPointFoundAtCorner)
            {
                if (thisPointDistance < nearestPointDistanceAtCorner)
                {
                    nearestPointAtCorner = corner;
                    nearestPointDistanceAtCorner = thisPointDistance;
                }
            }
            else
            {
                nearestPointFoundAtCorner = true;
                nearestPointAtCorner = corner;
                nearestPointDistanceAtCorner = thisPointDistance;
            }
        }

        if (!nearestPointFoundOnFace && !nearestPointFoundOnEdge && !nearestPointFoundAtCorner)
        {
            Debug.LogError("Somehow a nearest point couldn't even be found in corners...");
            nearestPoint = new Vector3(inputPointPos.x, float.NegativeInfinity, inputPointPos.z);
        }

        float nearestPointDistance = float.PositiveInfinity;
        if (nearestPointFoundOnFace)
        {
            if (nearestPointDistanceOnFace < nearestPointDistance)
            {
                nearestPoint = nearestPointOnFace;
                nearestPointDistance = nearestPointDistanceOnFace;
            }
        }

        if (nearestPointFoundOnEdge)
        {
            if (nearestPointDistanceOnEdge < nearestPointDistance)
            {
                nearestPoint = nearestPointOnEdge;
                nearestPointDistance = nearestPointDistanceOnEdge;
            }
        }

        if (nearestPointFoundAtCorner)
        {
            if (nearestPointDistanceAtCorner < nearestPointDistance)
            {
                nearestPoint = nearestPointAtCorner;
                nearestPointDistance = nearestPointDistanceAtCorner;
            }
        }

        return nearestPoint;
    }
}

public class Face
{
    public string name;
    public Three3DPoints plane;
    public Three3DPoints[] triangles;

    public Geometry.BasisDir preferredProjectionDir;

    public Face(string name_, Three3DPoints plane_, Three3DPoints[] triangles_)
    {
        name = name_;
        plane = plane_;
        triangles = triangles_;

        Maybe<Geometry.BasisDir> maybePreferredProjectionDir = plane.GetBestProjectionDir();
        if (maybePreferredProjectionDir.exists)
        {
            preferredProjectionDir = maybePreferredProjectionDir.value;
        }
        else
        {
            Debug.LogError("Degenerate face found.");
        }
    }
}

public class Three3DPoints
{
    public Vector3 p1;
    public Vector3 p2;
    public Vector3 p3;

    public Three3DPoints(Vector3 p1_, Vector3 p2_, Vector3 p3_)
    {
        p1 = p1_;
        p2 = p2_;
        p3 = p3_;
    }

    public Maybe<Geometry.BasisDir> GetBestProjectionDir()
    {
        bool nonZeroAreaFound = false;
        Geometry.BasisDir bestProjectionDir = Geometry.BasisDir.X;
        float maxAreaFound = 0f;

        foreach (Geometry.BasisDir dir in Enum.GetValues(typeof(Geometry.BasisDir)))
        {
            float thisArea = ProjectDownDir(dir).GetArea();

            if (thisArea > maxAreaFound)
            {
                nonZeroAreaFound = true;
                bestProjectionDir = dir;

                maxAreaFound = thisArea;
            }
        }

        return new Maybe<Geometry.BasisDir>(nonZeroAreaFound, bestProjectionDir);
    }

    public Triangle2D ProjectDownDir(Geometry.BasisDir dir)
    {
        return new Triangle2D(Geometry.ProjectDownDir(p1, dir), Geometry.ProjectDownDir(p2, dir), Geometry.ProjectDownDir(p3, dir));
    }

    public List<Three3DPoints> ExcludeAbove(float inputHeight, out Edge3D newEdge)
    {
        List<Three3DPoints> afterChopTris = new List<Three3DPoints>();

        List<Edge3D> triEdges = new List<Edge3D>
                {
                    new Edge3D("TriEdge1", p1, p2),
                    new Edge3D("TriEdge2", p2, p3),
                    new Edge3D("TriEdge3", p3, p1)
                };
        List<Vector3> triCorners = new List<Vector3>
                {
                    p1,
                    p2,
                    p3,
                };

        // Sort edges in triangle into excluded / chopped / preserved according to whether they are entirely above or below inputHeight or overlap it
        List<Edge3D> preservedTriEdges = new List<Edge3D>();
        List<Edge3D> choppedTriEdges = new List<Edge3D>();
        foreach (Edge3D triEdge in triEdges)
        {
            float minTriHeight = Mathf.Min(triEdge.p1.y, triEdge.p2.y);
            float maxTriHeight = Mathf.Max(triEdge.p1.y, triEdge.p2.y);

            Interval triEdgeHeightInterval = new Interval(minTriHeight, maxTriHeight);

            if (inputHeight > maxTriHeight)
            {
                preservedTriEdges.Add(triEdge);
            }
            else if (triEdgeHeightInterval.Contains(inputHeight))
            {
                choppedTriEdges.Add(triEdge);
            }
        }

        // Sort corners in triangle into excluded / preserved according to whether they are entirely above or below inputHeight or overlap it
        List<Vector3> preservedTriCorners = new List<Vector3>();
        foreach (Vector3 triCorner in triCorners)
        {
            if (triCorner.y <= inputHeight)
            {
                preservedTriCorners.Add(triCorner);
            }
        }

        // Count preserved Corners to see whether we're left with a quad or tri
        newEdge = null;
        if (preservedTriCorners.Count == 2)
        {
            // The portion of the triangle below the horizontal plane of height 'inputHeight' is a quadrilateral
            List<Vector3> chopPoints = new List<Vector3>();
            List<Edge3D> afterChopChoppedEdgesOnly = new List<Edge3D>();
            foreach (Edge3D triEdge in choppedTriEdges)
            {
                List<Vector3> points = new List<Vector3>
                {
                    triEdge.p1,
                    triEdge.p2
                };

                points.Sort(delegate (Vector3 a, Vector3 b)
                {
                    return a.y.CompareTo(b.y);
                });

                Vector3 lowPoint = points[0];
                Vector3 highPoint = points[1];

                float progressInHeightAlongEdge = Maths.InverseLerp(lowPoint.y, highPoint.y, inputHeight);

                Vector3 chopPoint = Maths.LerpVec3(lowPoint, highPoint, progressInHeightAlongEdge);
                chopPoints.Add(chopPoint);

                // Must set the first edge point to be the Lower point for the next bit to work
                afterChopChoppedEdgesOnly.Add(new Edge3D(triEdge.name, lowPoint, chopPoint));
            }

            newEdge = new Edge3D("ChopEdge", chopPoints[0], chopPoints[1]);
            afterChopTris.Add(new Three3DPoints(afterChopChoppedEdgesOnly[0].p1, afterChopChoppedEdgesOnly[0].p2, afterChopChoppedEdgesOnly[1].p1));
            afterChopTris.Add(new Three3DPoints(afterChopChoppedEdgesOnly[1].p2, afterChopChoppedEdgesOnly[1].p1, afterChopChoppedEdgesOnly[0].p2));
        }
        else if (preservedTriCorners.Count == 1)
        {
            // The portion of the triangle below the horizontal plane of height inputHeight is a triangle
            List<Vector3> chopPoints = new List<Vector3>();
            foreach (Edge3D triEdge in choppedTriEdges)
            {
                Vector3 chopPoint = Geometry.PointOnLineAtHeight(triEdge.p1, triEdge.p2, inputHeight);
                chopPoints.Add(chopPoint);
            }

            newEdge = new Edge3D("ChopEdge", chopPoints[0], chopPoints[1]);
            afterChopTris.Add(new Three3DPoints(preservedTriCorners[0], chopPoints[0], chopPoints[1]));
        }
        else
        {
            Debug.LogError("Impossible case entered while calculating Tri Excluding Above");
        }

        return afterChopTris;
    }
}

public class Triangle2D
{
    public Vector2 p1;
    public Vector2 p2;
    public Vector2 p3;

    public Triangle2D(Vector2 p1_, Vector2 p2_, Vector2 p3_)
    {
        p1 = p1_;
        p2 = p2_;
        p3 = p3_;
    }

    public float GetArea()
    {
        Vector3 u = p1 - p3;
        Vector3 v = p2 - p3;
        Vector3 cross = Vector3.Cross(u, v);

        return cross.magnitude / 2f;
    }

    public bool Contains(Vector2 p)
    {
        Vector3 affineCoords = Geometry.AffineCoords3Vec2s(p1, p2, p3, p);
        return affineCoords.x >= 0f && affineCoords.y >= 0f && affineCoords.z >= 0f;
    }
}

public class Edge3D
{
    public string name;
    public Vector3 p1;
    public Vector3 p2;

    public Edge3D(string name_, Vector3 p1_, Vector3 p2_)
    {
        name = name_;
        p1 = p1_;
        p2 = p2_;
    }

    public Maybe<Geometry.BasisDir> GetBestProjectionDir()
    {
        bool nonZeroLengthFound = false;
        Geometry.BasisDir bestProjectionDir = Geometry.BasisDir.X;
        float maxLengthFound = 0f;

        foreach (Geometry.BasisDir dir in Enum.GetValues(typeof(Geometry.BasisDir)))
        {
            float thisLength = ProjectToDir(dir).UpperMinusLower();

            if (thisLength > maxLengthFound)
            {
                nonZeroLengthFound = true;
                bestProjectionDir = dir;

                maxLengthFound = thisLength;
            }
        }

        return new Maybe<Geometry.BasisDir>(nonZeroLengthFound, bestProjectionDir);
    }

    public Interval ProjectToDir(Geometry.BasisDir dir)
    {
        float projP1 = Geometry.ProjectToDir(p1, dir);
        float projP2 = Geometry.ProjectToDir(p2, dir);

        float lower = Mathf.Min(projP1, projP2);
        float upper = Mathf.Max(projP1, projP2);

        return new Interval(lower, upper);
    }

    public IntervalDirectionless ProjectToDirDirectionless(Geometry.BasisDir dir)
    {
        float projP1 = Geometry.ProjectToDir(p1, dir);
        float projP2 = Geometry.ProjectToDir(p2, dir);

        return new IntervalDirectionless(projP1, projP2);
    }

    public Edge3D ExcludeAbove(string newName, float inputHeight)
    {
        List<Vector3> points = new List<Vector3>
        {
            p1,
            p2
        };

        points.Sort(delegate (Vector3 a, Vector3 b)
        {
            return a.y.CompareTo(b.y);
        });

        Vector3 lowPoint = points[0];
        Vector3 highPoint = points[1];

        float progressInHeightAlongEdge = Maths.InverseLerp(lowPoint.y, highPoint.y, inputHeight);

        Vector3 chopPoint = Maths.LerpVec3(lowPoint, highPoint, progressInHeightAlongEdge);

        return new Edge3D(newName, lowPoint, chopPoint);
    }
}

public class Geometry
{
    public enum BasisDir
    {
        X = 0,
        Y,
        Z
    }

    public static Vector3 NearestPointOfPlane(Vector3 inputPoint, Vector3 planePoint1, Vector3 planePoint2, Vector3 planePoint3)
    {
        // treat planePoint3 as new origin and rememeber to add it back when returning result
        Vector3 p = inputPoint - planePoint3;
        Vector3 u = planePoint1 - planePoint3;
        Vector3 v = planePoint2 - planePoint3;

        Vector3 normal = Vector3.Normalize(Vector3.Cross(u, v));

        return Vector3.ProjectOnPlane(p, normal) + planePoint3;
    }

    public static Vector3 NearestPointOfLineFromPoints(Vector3 inputPoint, Vector3 linePoint1, Vector3 linePoint2)
    {
        Vector3 linePoint = linePoint2;
        Vector3 lineVec = linePoint1 - linePoint2;
        return NearestPointOfLine(inputPoint, linePoint, lineVec);
    }

    public static Vector3 NearestPointOfLine(Vector3 inputPoint, Vector3 linePoint, Vector3 lineVec)
    {
        Vector3 p = inputPoint - linePoint;
        return Vector3.Dot(p, lineVec.normalized) * lineVec.normalized + linePoint;
    }

    public static Vector3 AffineCoords3Vec2s(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p)
    // Effectively the Mathf.InvLerp but for when lerping over a surface defined by a triangle
    {
        float x0 = p0.x;
        float y0 = p0.y;

        float x1 = p1.x;
        float y1 = p1.y;

        float x2 = p2.x;
        float y2 = p2.y;

        float x = p.x;
        float y = p.y;

        float denom = (y2 - y0) * (x1 - x0) + (x0 - x2) * (y1 - y0);

        if (denom != 0f)
        {
            float s1 = ((y2 - y0) * (x - x0) + (x0 - x2) * (y - y0)) / denom;
            float s2 = ((y0 - y1) * (x - x0) + (x1 - x0) * (y - y0)) / denom;

            float s0 = 1 - s1 - s2;

            return new Vector3(s0, s1, s2);
        }
        else
        {
            return new Vector3(1f, 0f, 0f);
        }
    }

    public static Vector2 ProjectDownDir(Vector3 p, BasisDir dir)
    {
        switch (dir)
        {
            case BasisDir.X:
                return new Vector2(p.y, p.z);
            case BasisDir.Y:
                return new Vector2(p.x, p.z);
            case BasisDir.Z:
                return new Vector2(p.x, p.y);
            default:
                return new Vector2(p.x, p.z);
        }
    }
    public static float ProjectToDir(Vector3 p, BasisDir dir)
    {
        switch (dir)
        {
            case BasisDir.X:
                return p.x;
            case BasisDir.Y:
                return p.y;
            case BasisDir.Z:
                return p.z;
            default:
                return p.y;
        }
    }
    // closestPointToPoint is just the point itself and it's always 'inside'

    //Find the line of intersection between two planes.
    //The inputs are two game objects which represent the planes.
    //The outputs are a point on the line and a vector which indicates it's direction.
    public static void PlanePlaneIntersection(out Vector3 linePoint, out Vector3 lineVec, Vector3 plane1Normal, Vector3 plane2Normal, Vector3 plane1Point, Vector3 plane2Point)
    {
        linePoint = Vector3.zero;
        lineVec = Vector3.zero;

        //We can get the direction of the line of intersection of the two planes by calculating the
        //cross product of the normals of the two planes. Note that this is just a direction and the line
        //is not fixed in space yet.
        lineVec = Vector3.Cross(plane1Normal, plane2Normal);

        //Next is to calculate a point on the line to fix it's position. This is done by finding a vector from
        //the plane2 location, moving parallel to it's plane, and intersecting plane1. To prevent rounding
        //errors, this vector also has to be perpendicular to lineDirection. To get this vector, calculate
        //the cross product of the normal of plane2 and the lineDirection.      
        Vector3 ldir = Vector3.Cross(plane2Normal, lineVec);

        float denom = Vector3.Dot(plane1Normal, ldir);
        
        //Prevent divide by zero.
        if (Mathf.Abs(denom) > 0.000001f)
        {
            Vector3 plane1ToPlane2 = plane1Point - plane2Point;
            float t = Vector3.Dot(plane1Normal, plane1ToPlane2) / denom;
            linePoint = plane2Point + t * ldir;
        }
        else
        {
            if (denom == 0)
            {
                Debug.LogError("Division by zero in PlanePlaneIntersection.");
            }
            else
            {
                Debug.LogError("Near division by zero in PlanePlaneIntersection.");
            }

            linePoint = new Vector3(0f, float.NegativeInfinity, 0f);
        }
    }

    //Find the line of intersection between two planes.
    //The inputs are two game objects which represent the planes.
    //The outputs are a point on the line and a vector which indicates it's direction.
    public static void PlanePlaneIntersectionFromPlanePoints(out Vector3 linePoint, out Vector3 lineVec, Vector3 plane1Point1, Vector3 plane1Point2, Vector3 plane1Point3, Vector3 plane2Point1, Vector3 plane2Point2, Vector3 plane2Point3)
    {
        //Get the normals of the planes.
        Vector3 plane1Normal = Vector3.Cross(plane1Point2 - plane1Point1, plane1Point3 - plane1Point1);
        Vector3 plane2Normal = Vector3.Cross(plane2Point2 - plane2Point1, plane2Point3 - plane2Point1);

        Vector3 plane1Point = plane1Point1;
        Vector3 plane2Point = plane2Point1;

        PlanePlaneIntersection(out linePoint, out lineVec, plane1Normal, plane2Normal, plane1Point, plane2Point);
    }

    //Find the line of intersection between two planes.
    //The inputs are two game objects which represent the planes.
    //The outputs are a point on the line and a vector which indicates it's direction.
    public static void PlaneIntersectionWithHorizontalPlane(out Vector3 linePoint, out Vector3 lineVec, float plane1Y, Vector3 plane2Point1, Vector3 plane2Point2, Vector3 plane2Point3)
    {
        //Get the normals of the planes.
        Vector3 plane1Normal = Vector3.up;
        Vector3 plane2Normal = Vector3.Cross(plane2Point2 - plane2Point1, plane2Point3 - plane2Point1).normalized;

        Vector3 plane1Point = plane1Y * Vector3.up;
        Vector3 plane2Point = plane2Point1;

        PlanePlaneIntersection(out linePoint, out lineVec, plane1Normal, plane2Normal, plane1Point, plane2Point);
    }

    public static Vector3 NearestPointToTriangleOnVertical(Vector3 inputPoint, Vector3 corner1, Vector3 corner2, Vector3 corner3)
    {
        Vector2 projCorner1 = ProjectDownDir(corner1, BasisDir.Y);
        Vector2 projCorner2 = ProjectDownDir(corner2, BasisDir.Y);
        Vector2 projCorner3 = ProjectDownDir(corner3, BasisDir.Y);

        Triangle2D projectedTriangle = new Triangle2D(projCorner1, projCorner2, projCorner3);
        if (projectedTriangle.GetArea() == 0f)
        {
            Debug.LogError("Degenerate triangle found.");
        }

        Vector2 projCharacterPos = ProjectDownDir(inputPoint, BasisDir.Y);

        Vector3 affineCoords = AffineCoords3Vec2s(projCorner1, projCorner2, projCorner3, projCharacterPos);

        Interval affineInsideRange = new Interval(0, 1);

        bool characterAndTriAreOverEachother = affineInsideRange.Contains(affineCoords.x)
                                            && affineInsideRange.Contains(affineCoords.y)
                                            && affineInsideRange.Contains(affineCoords.z);

        if (characterAndTriAreOverEachother)
        {
            float newHeight = affineCoords.x * projCorner1.y
                            + affineCoords.y * projCorner2.y
                            + affineCoords.z * projCorner3.y;

            return new Vector3(inputPoint.x, newHeight, inputPoint.z);
        }
        else
        {
            return new Vector3(inputPoint.x, float.NegativeInfinity, inputPoint.z);
        }
    }

    internal static Vector3 PointOnLineAtHeight(Vector3 p1, Vector3 p2, float inputHeight)
    {
        List<Vector3> points = new List<Vector3>
        {
            p1,
            p2
        };

        points.Sort(delegate (Vector3 a, Vector3 b)
        {
            return a.y.CompareTo(b.y);
        });

        Vector3 lowPoint = points[0];
        Vector3 highPoint = points[1];

        float progressInHeightAlongEdge = Maths.InverseLerp(lowPoint.y, highPoint.y, inputHeight);

        return Maths.LerpVec3(lowPoint, highPoint, progressInHeightAlongEdge);
    }
    
    public static Vector3 ProjectToPlaneDownY(Vector3 inputPoint, Vector3 planePoint1, Vector3 planePoint2, Vector3 planePoint3)
    {
        if (planePoint1.y == planePoint2.y && planePoint2.y == planePoint3.y)
        {
            // plane is horizontal
            Vector3 nearestPosOnPlane = new Vector3(inputPoint.x, planePoint1.y, inputPoint.z);
            return nearestPosOnPlane;
        }
        else
        {
            // plane is not horizontal
            Vector2 projCorner1 = ProjectDownDir(planePoint1, BasisDir.Y);
            Vector2 projCorner2 = ProjectDownDir(planePoint2, BasisDir.Y);
            Vector2 projCorner3 = ProjectDownDir(planePoint3, BasisDir.Y);

            Triangle2D projectedTriangle = new Triangle2D(projCorner1, projCorner2, projCorner3);
            if (projectedTriangle.GetArea() == 0f)
            {
                Debug.LogError("Degenerate triangle found.");
            }

            Vector2 projCharacterPos = ProjectDownDir(inputPoint, BasisDir.Y);

            Vector3 affineCoords = AffineCoords3Vec2s(projCorner1, projCorner2, projCorner3, projCharacterPos);

            float newHeight = affineCoords.x * planePoint1.y
                            + affineCoords.y * planePoint2.y
                            + affineCoords.z * planePoint3.y;

            return new Vector3(inputPoint.x, newHeight, inputPoint.z);
        }
    }
}

 public static class Vector2Extension
{

    public static Vector2 RotateByDegrees(this Vector2 v, float degrees)
    {
        float sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
        float cos = Mathf.Cos(degrees * Mathf.Deg2Rad);

        float tx = v.x;
        float ty = v.y;
        v.x = (cos * tx) - (sin * ty);
        v.y = (sin * tx) + (cos * ty);
        return v;
    }
}

public static class Vector3Extension
{
    public static float InverseLerp(Vector3 v1, Vector3 v2, Vector3 inputPointPos)
    {
        Edge3D edge = new("", v1, v2);

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

        Vector3 inputPointPosToEdge = Geometry.NearestPointOfLineFromPoints(inputPointPos, edge.p1, edge.p2);
        IntervalDirectionless projectedInterval = edge.ProjectToDirDirectionless(projectionDir);

        float inputPointPosToDir = Geometry.ProjectToDir(inputPointPosToEdge, projectionDir);

        return projectedInterval.ProgressFromLowerToUpper(inputPointPosToDir);
    }
}