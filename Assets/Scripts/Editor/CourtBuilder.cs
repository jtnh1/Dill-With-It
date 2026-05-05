using UnityEditor;
using UnityEngine;

// Provides the Pickleball/Build Court menu item.
// All actual build logic lives in the runtime CourtBuilder MonoBehaviour.
public static class CourtBuilderEditorTool
{
    [MenuItem("Pickleball/Build Court")]
    public static void BuildCourt()
    {
        var builder = Object.FindAnyObjectByType<CourtBuilder>();
        if (builder == null)
        {
            Debug.LogError("[CourtBuilder] No CourtBuilder component found in the scene. " +
                           "Add the CourtBuilder component to your Court GameObject first, then run this menu item.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Build Pickleball Court");
        builder.BuildCourt();
    }
}
