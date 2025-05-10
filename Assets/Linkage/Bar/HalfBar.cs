using UnityEngine;
using UnityEngine.EventSystems;

public class HalfBar : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector]
    public Linkage parentLinkage;
    public SpriteRenderer spriteRenderer;

    public void OnBeginDrag(PointerEventData eventData)
    {
        parentLinkage.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        parentLinkage.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        parentLinkage.OnEndDrag(eventData);
    }

    public void Initialize(Linkage parentLinkage, Joint pivotJoint, Joint adjacentJoint, Joint oppositeJoint, Joint alternativeAdjacentJoint)
    {
        this.parentLinkage = parentLinkage;
        this.pivotJoint = pivotJoint;
        this.adjacentJoint = adjacentJoint;
        this.oppositeJoint = oppositeJoint;
        this.alternativeAdjacentJoint = alternativeAdjacentJoint;

        Highlight(false);
    }

    public Joint pivotJoint; // The pivot joint
    public Joint adjacentJoint; // The joint adjacent to the pivot on the other end of the bar
    public Joint oppositeJoint; // The joint adjacent to the pivot on the other end of the bar
    public Joint alternativeAdjacentJoint; // The joint adjacent to the pivot on the other end of the bar
    public void Highlight(bool isHighlight)
    {
        spriteRenderer.color = isHighlight ? Color.yellow : Color.blue;
    }
}
