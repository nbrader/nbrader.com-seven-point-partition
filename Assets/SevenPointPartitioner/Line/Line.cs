using UnityEngine;
using UnityEngine.EventSystems;

public class Line : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
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

    public void Initialize(SevenPointPartitioner parentSevenPointPartitioner, Point pivotPoint, Point adjacentPoint, Point oppositePoint, Point alternativeAdjacentPoint)
    {
        this.parentSevenPointPartitioner = parentSevenPointPartitioner;
        this.pivotPoint = pivotPoint;
        this.adjacentPoint = adjacentPoint;
        this.oppositePoint = oppositePoint;
        this.alternativeAdjacentPoint = alternativeAdjacentPoint;

        Highlight(false);
    }

    public Point pivotPoint; // The pivot point
    public Point adjacentPoint; // The point adjacent to the pivot on the other end of the line
    public Point oppositePoint; // The point adjacent to the pivot on the other end of the line
    public Point alternativeAdjacentPoint; // The point adjacent to the pivot on the other end of the line
    public void Highlight(bool isHighlight)
    {
        spriteRenderer.color = isHighlight ? Color.yellow : Color.blue;
    }
}
