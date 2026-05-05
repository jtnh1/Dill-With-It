using Mirror;
using UnityEditor;
using UnityEngine;

// Runs once after compile to add network components to the Player prefab.
[InitializeOnLoad]
public static class SetupNetworkPrefabs
{
    const string DoneKey = "NetworkPrefabSetup_v2";

    static SetupNetworkPrefabs()
    {
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        const string playerPath = "Assets/Prefabs/Player.prefab";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
        if (asset == null)
        {
            Debug.LogWarning($"[SetupNetworkPrefabs] Player prefab not found at {playerPath}");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(playerPath))
        {
            var go = scope.prefabContentsRoot;

            if (!go.GetComponent<NetworkIdentity>())
                go.AddComponent<NetworkIdentity>();

            var ntr = go.GetComponent<NetworkTransformReliable>();
            if (ntr == null) ntr = go.AddComponent<NetworkTransformReliable>();
            // ClientToServer: each player owns their movement
            var soNtr = new SerializedObject(ntr);
            var sdProp = soNtr.FindProperty("syncDirection");
            if (sdProp != null) { sdProp.enumValueIndex = 1; soNtr.ApplyModifiedProperties(); }

            var na = go.GetComponent<NetworkAnimator>();
            if (na == null) na = go.AddComponent<NetworkAnimator>();
            var anim = go.GetComponent<Animator>();
            if (anim != null) na.animator = anim;
            var soNa = new SerializedObject(na);
            var caProp = soNa.FindProperty("clientAuthority");
            if (caProp != null) { caProp.boolValue = true; soNa.ApplyModifiedProperties(); }

            if (!go.GetComponent<NetworkPlayerController>())
                go.AddComponent<NetworkPlayerController>();
        }

        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("[SetupNetworkPrefabs] Player.prefab network components configured.");
    }
}
