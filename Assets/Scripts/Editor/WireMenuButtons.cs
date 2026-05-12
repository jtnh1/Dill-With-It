using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

// Run via Tools > Wire Menu Buttons to connect all UI button onClick events in MainMenu.
public static class WireMenuButtons
{
    [MenuItem("Tools/Wire Menu Buttons")]
    public static void Wire()
    {
        const string sceneName = "MainMenu";
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("MainMenu"))
        {
            Debug.LogWarning("[WireMenuButtons] Open the MainMenu scene first.");
            return;
        }

        var uiManager = Object.FindAnyObjectByType<UIManager>();
        if (uiManager == null) { Debug.LogError("[WireMenuButtons] UIManager not found."); return; }

        var lobbyUI   = Object.FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
        var joinPanel = Object.FindAnyObjectByType<JoinPanel>(FindObjectsInactive.Include);

        // ── MainMenuPanel buttons ──────────────────────────────────────────────
        WireButton("MainMenuCanvas/MainMenuPanel/PlayButton",    uiManager, "OnPlayButton");
        WireButton("MainMenuCanvas/MainMenuPanel/HostButton",    uiManager, "OnHostButton");
        WireButton("MainMenuCanvas/MainMenuPanel/JoinButton",    uiManager, "OnJoinButton");
        WireButton("MainMenuCanvas/MainMenuPanel/OptionsButton", uiManager, "OnOptionsButton");
        WireButton("MainMenuCanvas/MainMenuPanel/QuitButton",    uiManager, "OnQuitButton");

        // ── LobbyPanel buttons ─────────────────────────────────────────────────
        if (lobbyUI != null)
        {
            WireButton("MainMenuCanvas/LobbyPanel/ReadyButton", lobbyUI, "OnReadyClicked");
            WireButton("MainMenuCanvas/LobbyPanel/StartButton", lobbyUI, "OnStartClicked");
            WireButton("MainMenuCanvas/LobbyPanel/BackButton",  lobbyUI, "OnBackButton");
        }

        // ── JoinPanel buttons ──────────────────────────────────────────────────
        if (joinPanel != null)
        {
            WireButton("MainMenuCanvas/JoinPanel/SearchButton", joinPanel, "RefreshList");
            WireButton("MainMenuCanvas/JoinPanel/BackButton",   joinPanel, "OnBackButton");
        }

        Debug.Log("[WireMenuButtons] All buttons wired.");
        EditorUtility.SetDirty(uiManager.gameObject.scene.GetRootGameObjects()[0]);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void WireButton(string path, Object target, string methodName)
    {
        var go = FindIncludingInactive(path);
        if (go == null) { Debug.LogWarning($"[WireMenuButtons] Not found: {path}"); return; }
        var btn = go.GetComponent<Button>();
        if (btn == null) { Debug.LogWarning($"[WireMenuButtons] No Button on {path}"); return; }

        // Remove existing persistent listeners to avoid duplicates.
        btn.onClick.RemoveAllListeners();

        var method = target.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (method == null) { Debug.LogWarning($"[WireMenuButtons] Method {methodName} not found on {target.GetType()}"); return; }

        UnityEventTools.AddVoidPersistentListener(btn.onClick,
            System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), target, method)
            as UnityEngine.Events.UnityAction);

        EditorUtility.SetDirty(btn);
        Debug.Log($"[WireMenuButtons] Wired {path} -> {methodName}");
    }

    // GameObject.Find skips inactive nodes; walk the hierarchy by hand so we can
    // wire buttons inside panels that start hidden (LobbyPanel, JoinPanel).
    static GameObject FindIncludingInactive(string path)
    {
        string[] parts = path.Split('/');
        if (parts.Length == 0) return null;

        GameObject root = null;
        foreach (GameObject candidate in UnityEngine.SceneManagement.SceneManager
                     .GetActiveScene().GetRootGameObjects())
        {
            if (candidate.name == parts[0]) { root = candidate; break; }
        }
        if (root == null) return null;

        Transform current = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            Transform next = null;
            for (int c = 0; c < current.childCount; c++)
            {
                if (current.GetChild(c).name == parts[i]) { next = current.GetChild(c); break; }
            }
            if (next == null) return null;
            current = next;
        }
        return current.gameObject;
    }
}
