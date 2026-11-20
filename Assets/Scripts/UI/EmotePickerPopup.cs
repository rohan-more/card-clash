using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What the emote picker shows, and who hears the pick.</summary>
public class EmoteChoice
{
    public EmoteLibrary Library;
    public Action<Emote> OnPick;
}

/// <summary>
/// The panel of emotes, one button per emote in the library, in the library's
/// order. Opened by EmotePicker through PopupManager as PopupType.EmotePicker
/// with an EmoteChoice context.
///
/// Set its prefab to Priority Low, so it never closes a choice the game is
/// waiting for, and Close On Outside Tap on. Its one emote button (inactive,
/// e.g. under a Grid Layout Group) is the template, copied for every emote.
/// </summary>
public class EmotePickerPopup : PopupView
{
    [Tooltip("An inactive Button whose Image shows the emote; copied once per emote, next to itself.")]
    [SerializeField] private Button emoteButtonTemplate;

    private readonly List<Button> emoteButtons = new List<Button>();
    private EmoteChoice choice;

    public override void Show(object context = null, Action onCompleted = null)
    {
        base.Show(context, onCompleted);

        choice = context as EmoteChoice;
        BuildButtons(choice?.Library);
    }

    // One copy of the template per emote, in the library's order
    private void BuildButtons(EmoteLibrary library)
    {
        if (emoteButtons.Count > 0)
            return; // PopupManager makes a fresh popup each time, but just in case
        if (emoteButtonTemplate == null || library == null)
        {
            Debug.LogWarning("EmotePickerPopup: needs an Emote Button Template, and an Emote Library " +
                             "in its EmoteChoice", this);
            return;
        }

        emoteButtonTemplate.gameObject.SetActive(false);
        var parent = emoteButtonTemplate.transform.parent;

        foreach (var entry in library.Entries)
        {
            if (entry.sprite == null)
                continue;

            var button = Instantiate(emoteButtonTemplate, parent);
            button.name = entry.emote.ToString();
            if (button.image != null)
            {
                button.image.sprite = entry.sprite;
                button.image.preserveAspect = true;
            }

            var emote = entry.emote; // a copy for the listener below
            button.onClick.AddListener(() => Answer(emote));
            button.gameObject.SetActive(true);
            emoteButtons.Add(button);
        }
    }

    // Close first, the same as the other popups
    private void Answer(Emote emote)
    {
        var picked = choice?.OnPick;
        choice = null;
        Services.Popups.CloseActive();
        picked?.Invoke(emote);
    }
}
