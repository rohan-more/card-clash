using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Popups for the current scene, by way of Services.Popups.</summary>
public interface IPopupService
{
    /// <summary>
    /// Opens a popup, replacing the one showing, unless the one showing has a
    /// higher priority: then nothing opens and it returns false.
    /// </summary>
    bool Show(PopupType type, object context = null, Action onCompleted = null);

    /// <summary>Whether Show would open this popup right now.</summary>
    bool CanShow(PopupType type);

    /// <summary>The popup showing, or None.</summary>
    PopupType ActiveType { get; }

    void CloseActive();
}

/// <summary>
/// Shows one popup at a time from its prefab list. One per scene that needs
/// popups; it registers itself as Services.Popups and leaves with the scene.
///
/// Each popup has a priority (set on its prefab). A popup opens over one of
/// the same or a lower priority and replaces it, but is refused over a higher
/// one: so a light popup like the emote picker can never close a choice the
/// game is waiting for, while that choice always replaces the emote picker.
/// </summary>
public class PopupManager : MonoBehaviour, IPopupService
{
    [SerializeField] private List<PopupView> popupPrefabs;

    private readonly Dictionary<PopupType, PopupView> prefabLookup = new();
    private PopupView activePopup;

    public PopupType ActiveType => activePopup != null ? activePopup.Type : PopupType.None;

    private void Awake()
    {
        // Services.Popups is an interface, so "!= null" can't see that the
        // manager from a previous scene load has been destroyed: ask Unity instead.
        if (Services.Popups is PopupManager existing && existing != null && existing != this)
        {
            Destroy(gameObject);
            return;
        }

        Services.Register(this);

        foreach (var prefab in popupPrefabs)
        {
            if (prefabLookup.ContainsKey(prefab.Type))
            {
                Debug.LogError($"Duplicate popup prefab for type {prefab.Type}");
                continue;
            }

            prefabLookup.Add(prefab.Type, prefab);
        }
    }

    public bool CanShow(PopupType type)
    {
        if (!prefabLookup.TryGetValue(type, out var prefab))
            return false;
        return activePopup == null || prefab.Priority >= activePopup.Priority;
    }

    public bool Show(PopupType type, object context = null, Action onCompleted = null)
    {
        if (!prefabLookup.TryGetValue(type, out var prefab))
        {
            Debug.LogError($"Popup prefab not registered: {type}");
            return false;
        }

        // A more important popup is waiting for an answer: leave it alone
        if (activePopup != null && prefab.Priority < activePopup.Priority)
            return false;

        CloseActive();

        PopupView instance = Instantiate(prefab, transform);
        activePopup = instance;

        instance.Show(context, () =>
            {
                onCompleted?.Invoke();
                instance.DestroyWhenClosed(); // after its exit animation
                // onCompleted may have opened the next popup: keep that one
                if (activePopup == instance)
                    activePopup = null;
            }
        );
        return true;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    public void CloseActive()
    {
        if (activePopup == null) return;

        PopupView popup = activePopup;
        activePopup = null;
        popup.Hide(); // Hide → Complete → destroy callback
    }
}
