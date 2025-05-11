using UnityEngine;
using UnityEngine.EventSystems;

public class HalfBar : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector]
    public SevenPointPartitioner parentSevenPointPartitioner;
    public SpriteRenderer spriteRenderer;

    public void OnBeginDrag(PointerEventData eventData)
    {
        parentSevenPointPartitioner.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        parentSevenPointPartitioner.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        parentSevenPointPartitioner.OnEndDrag(eventData);
    }

    public void Initialize(SevenPointPartitioner parentSevenPointPartitioner, Point pivotJoint, Point adjacentJoint, Point oppositeJoint, Point alternativeAdjacentJoint)
    {
        this.parentSevenPointPartitioner = parentSevenPointPartitioner;
        this.pivotJoint = pivotJoint;
        this.adjacentJoint = adjacentJoint;
        this.oppositeJoint = oppositeJoint;
        this.alternativeAdjacentJoint = alternativeAdjacentJoint;

        Highlight(false);
    }

    public Point pivotJoint; // The pivot joint
    public Point adjacentJoint; // The joint adjacent to the pivot on the other end of the bar
    public Point oppositeJoint; // The joint adjacent to the pivot on the other end of the bar
    public Point alternativeAdjacentJoint; // The joint adjacent to the pivot on the other end of the bar
    public void Highlight(bool isHighlight)
    {
        spriteRenderer.color = isHighlight ? Color.yellow : Color.blue;
    }
}
