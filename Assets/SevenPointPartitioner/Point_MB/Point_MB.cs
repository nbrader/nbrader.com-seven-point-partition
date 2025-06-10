using UnityEngine;
using UnityEngine.EventSystems;

public class Point_MB : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector]
    public SevenPointPartitioner_MB parentSevenPointPartitioner;
    public SpriteRenderer spriteRenderer;
    public Color normalColour = Color.red;
    Color highlightedColour = Color.grey;

    public Vector3 Position { get { return transform.position; } set { transform.position = value; } }

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

    public void Highlight(bool isHighlight)
    {
        spriteRenderer.color = isHighlight ? (normalColour * 0.5f + highlightedColour * 0.5f) : normalColour;
    }
}
