using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Watches for the server starting a match and opens the match scene.
///
/// Put this on the same persistent object as NakamaConnection: the first
/// GAME_STATE arrives while the home screen is still showing, and the presenter
/// in the match scene asks for a fresh snapshot once it loads.
/// </summary>
public class MatchSceneLoader : MonoBehaviour
{
    [SerializeField] private string matchSceneName = "MatchScene";
    [SerializeField] private string homeSceneName = "Lobby";

    private bool loading;

    private static IConnectionService Connection => Services.Connection;

    // Start, not OnEnable: by then every Awake has run, so NakamaConnection exists
    private void Start()
    {
        if (Connection != null)
            Connection.OnMatchState += HandleMatchState;
        else
            Debug.LogError("MatchSceneLoader found no NakamaConnection: it won't hear the deal, " +
                           "so the match scene will never open.");
    }

    private void OnDestroy()
    {
        if (Connection != null)
            Connection.OnMatchState -= HandleMatchState;
    }

    private void HandleMatchState(long opCode, string json)
    {
        if (opCode != UnoOpCodes.GameState)
            return;

        if (SceneManager.GetActiveScene().name == matchSceneName || loading)
            return; // already there or on the way; this is just a resync

        // Open the table straight away: the server pauses before the first
        // turn, and the match intro (the pot and the deal) plays in that pause.
        // Rejoining mid-match has no pause and no intro, and goes straight in too.
        Debug.Log($"[SCENE] Deal received; opening {matchSceneName}");

        // The menu music fades out as the table opens and its own track fades in (see SceneMusic)
        Services.Audio?.PlayMusic(Music.None);
        StartCoroutine(LoadMatchScene());
    }

    private IEnumerator LoadMatchScene()
    {
        loading = true;
        yield return SceneManager.LoadSceneAsync(matchSceneName);
        loading = false;
    }

    /// <summary>Back to the home screen, e.g. after the results screen.</summary>
    public void ReturnHome()
    {
        SceneManager.LoadScene(homeSceneName);
    }
}
