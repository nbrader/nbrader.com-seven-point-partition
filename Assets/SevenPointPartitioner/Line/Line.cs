using System.Net;
using UnityEngine;

public class Line : MonoBehaviour
{
    [HideInInspector]
    public SevenPointPartitioner parentSevenPointPartitioner;
    public SpriteRenderer spriteRenderer;

    public Transform lineTransform;

    public Transform inputPoint1;
    public Transform inputPoint2;

    public Transform endPoint1;
    public Transform endPoint2;

    public Color colour;

    public bool IsVisible
    {
        get { return lineTransform.gameObject.activeInHierarchy; }
        set { lineTransform.gameObject.SetActive(value); }
    }

    private void Update()
    {
        Vector2 disp = Maths.ProjectVec3DownZ(endPoint2.position - endPoint1.position);
        float degrees = Maths.Rad2Deg(Maths.AngleFromVec2(disp));
        float dist = disp.magnitude;

        lineTransform.position = endPoint1.position;
        lineTransform.rotation = Quaternion.Euler(0f, 0f, degrees);
        lineTransform.transform.localScale = new Vector3(dist, SevenPointPartitioner.lineVisibleThickness, 1);

        spriteRenderer.color = colour;
    }
}
