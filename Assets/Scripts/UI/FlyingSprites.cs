using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pool of plain Images for things that fly across the table, many at once:
/// dealt cards, coins into the pot. Plain C#: the intro step that owns it
/// creates one on a layer above the table and releases everything when done.
/// Positions are world positions, so sources and targets can live anywhere in
/// the canvas.
/// </summary>
public class FlyingSprites
{
    private readonly RectTransform layer;
    private readonly List<Image> free = new List<Image>();
    private readonly List<Image> inUse = new List<Image>();

    public FlyingSprites(RectTransform layer)
    {
        this.layer = layer;
    }

    /// <summary>Shows a sprite at a world position, sized in the layer's units.</summary>
    public Image Spawn(Sprite sprite, Vector2 size, Vector3 worldPosition)
    {
        Image image;
        if (free.Count > 0)
        {
            image = free[free.Count - 1];
            free.RemoveAt(free.Count - 1);
        }
        else
        {
            var go = new GameObject("Flying", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            image = go.GetComponent<Image>();
            image.raycastTarget = false; // never catches a tap meant for the cards
            image.preserveAspect = true;
        }

        var t = image.rectTransform;
        t.SetAsLastSibling(); // the newest flies on top
        t.sizeDelta = size;
        t.position = worldPosition;
        t.localScale = Vector3.one;
        t.localRotation = Quaternion.identity;
        image.sprite = sprite;
        image.color = Color.white;
        image.gameObject.SetActive(true);
        inUse.Add(image);
        return image;
    }

    /// <summary>Hides one sprite for reuse.</summary>
    public void Release(Image image)
    {
        if (image == null || !inUse.Remove(image))
            return;
        image.rectTransform.DOKill();
        image.DOKill();
        image.gameObject.SetActive(false);
        free.Add(image);
    }

    /// <summary>Stops and hides everything still flying.</summary>
    public void ReleaseAll()
    {
        for (int i = inUse.Count - 1; i >= 0; i--)
            Release(inUse[i]);
    }

    /// <summary>
    /// The size, in this layer's units, that matches an object on screen, e.g.
    /// the draw pile, so a flying card looks like the cards it comes from.
    /// </summary>
    public Vector2 SizeLike(RectTransform target)
    {
        if (target == null)
            return new Vector2(100f, 150f);

        var size = target.rect.size;
        float targetScale = target.lossyScale.x;
        float layerScale = layer.lossyScale.x;
        return layerScale > 0f ? size * (targetScale / layerScale) : size;
    }

    /// <summary>Where the middle of an object on screen is, as a world position on this layer.</summary>
    public Vector3 PointOf(RectTransform target) => PointOn(layer, target);

    /// <summary>
    /// Where the middle of `target` is, as a world position on `layer`. Goes
    /// through the screen, so the two can be on different canvases (the table
    /// canvas and an overlay canvas, say) with different modes or scales.
    /// </summary>
    public static Vector3 PointOn(RectTransform layer, RectTransform target)
    {
        var world = target.TransformPoint(target.rect.center);
        var screen = RectTransformUtility.WorldToScreenPoint(CameraFor(target), world);
        return RectTransformUtility.ScreenPointToWorldPointInRectangle(layer, screen, CameraFor(layer), out var onLayer)
            ? onLayer
            : world;
    }

    // Overlay canvases draw without a camera; the others through theirs
    private static Camera CameraFor(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null)
            return null;
        canvas = canvas.rootCanvas;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }
}
