using UnityEngine;
using UnityEngine.EventSystems;

public class Point_MB : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector]
    public SevenPointPartitioner_MB parentSevenPointPartitioner;
    public SpriteRenderer spriteRenderer; // This should be the SpriteRenderer on the child GameObject
    public Color normalColour = Color.red;
    Color highlightedColour = Color.grey;

    public Vector3 Position { get { return transform.position; } set { transform.position = value; } }

    /// <summary>
    /// Sets the local scale of the GameObject that has the SpriteRenderer attached.
    /// </summary>
    /// <param name="scale">The desired uniform scale.</param>
    public void SetSpriteScale(float scale)
    {
        if (spriteRenderer != null)
        {
            // The spriteRenderer is on a child GameObject, so we want to scale that child.
            // Assuming spriteRenderer is directly referencing the child's SpriteRenderer.
            spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            Debug.LogWarning("SpriteRenderer not assigned to Point_MB. Cannot set sprite scale.");
        }
    }

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