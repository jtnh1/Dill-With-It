using UnityEditor;

public static class BridgeScratch
{
    public static string Run()
    {
        WireMenuButtons.Wire();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        return "Wired menu buttons and saved scene.";
    }
}
