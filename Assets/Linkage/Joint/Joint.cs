using UnityEngine;
using UnityEngine.EventSystems;

public class Joint : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector]
    public Linkage parentLinkage;
    public SpriteRenderer spriteRenderer;
    public AngleRange angleRange1;
    public AngleRange angleRange2;

    public void SetAngleRanges(float degreesCCWFromDownOfCentre1, float degreesBetweenExtremes1, bool showAngle1, Color color1, float degreesCCWFromDownOfCentre2, float degreesBetweenExtremes2, bool showAngle2, Color color2)
    {
        angleRange1.isVisible = showAngle1;
        angleRange2.isVisible = showAngle2;
        angleRange1.degreesCCWFromDownOfCentre = degreesCCWFromDownOfCentre1;
        angleRange1.degreesBetweenExtremes = degreesBetweenExtremes1;
        angleRange2.degreesCCWFromDownOfCentre = degreesCCWFromDownOfCentre2;
        angleRange2.degreesBetweenExtremes = degreesBetweenExtremes2;
        angleRange1.color = color1;
        angleRange2.color = color2;
    }

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

    public void Highlight(bool isHighlight)
    {
        spriteRenderer.color = isHighlight ? Color.yellow : Color.white;
    }
}
