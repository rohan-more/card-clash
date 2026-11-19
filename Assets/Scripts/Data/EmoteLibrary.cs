using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every emote. The numbers are what travels between players (SEND_EMOTE and
/// EMOTE carry them), so they are fixed: give a new emote the next number,
/// and never renumber or reuse one.
/// </summary>
public enum Emote
{
    Laugh = 0,      // face with tears of joy
    Angry = 1,      // face with symbols on mouth
    Scared = 2,     // face screaming in fear
    Cool = 3,       // smiling face with sunglasses
    Crying = 4,     // loudly crying face
    StarStruck = 5,
    Thinking = 6,
    Zany = 7,
}

/// <summary>
/// Which sprite each emote shows. Entries are matched by their Emote, not their
/// place in the list, so reordering them is safe: the order is only the order
/// the picker shows them in.
/// Create one via Assets > Create > UNO > Emote Library.
/// </summary>
[CreateAssetMenu(fileName = "EmoteLibrary", menuName = "UNO/Emote Library")]
public class EmoteLibrary : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public Emote emote;
        public Sprite sprite;
    }

    [Tooltip("One entry per emote, in the order the picker shows them.")]
    [SerializeField] private Entry[] entries;

    private Dictionary<Emote, Sprite> lookup;

    /// <summary>The emotes in picker order, each once.</summary>
    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    /// <summary>The emote's sprite, or null if this library has none for it.</summary>
    public Sprite Get(Emote emote)
    {
        if (lookup == null)
            BuildLookup();
        return lookup.TryGetValue(emote, out var sprite) ? sprite : null;
    }

    /// <summary>
    /// For a number from the server. An emote this build doesn't know (added on
    /// a newer client) shows nothing rather than the wrong face.
    /// </summary>
    public Sprite Get(int number) => Enum.IsDefined(typeof(Emote), number) ? Get((Emote)number) : null;

    private void BuildLookup()
    {
        lookup = new Dictionary<Emote, Sprite>();
        foreach (var entry in Entries)
        {
            if (entry.sprite != null && !lookup.ContainsKey(entry.emote))
                lookup.Add(entry.emote, entry.sprite);
        }
    }

    // Rebuilt after every Inspector edit, with a warning for mistakes
    private void OnValidate()
    {
        lookup = null;

        var seen = new HashSet<Emote>();
        foreach (var entry in Entries)
        {
            if (!seen.Add(entry.emote))
                Debug.LogWarning($"EmoteLibrary: {entry.emote} is listed twice; the first entry is used", this);
            else if (entry.sprite == null)
                Debug.LogWarning($"EmoteLibrary: {entry.emote} has no sprite", this);
        }

        foreach (Emote emote in Enum.GetValues(typeof(Emote)))
        {
            if (!seen.Contains(emote))
                Debug.LogWarning($"EmoteLibrary: {emote} has no entry, so it can't be picked or shown", this);
        }
    }
}
