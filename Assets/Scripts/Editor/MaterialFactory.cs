#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public static class MaterialFactory
{
    private const string MatPath = "Assets/Art/Materials";

    [MenuItem("Pickleball/Create Basic Materials")]
    public static void CreateAllMaterials()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder(MatPath);

        // Court
        CreateMat("Court_Surface", new Color(0.18f, 0.45f, 0.20f)); // Dark Green
        CreateMat("Court_Kitchen", new Color(0.72f, 0.85f, 0.95f)); // Light Blue
        CreateMat("Court_Lines", Color.white);
        CreateMat("Court_Net", new Color(0.15f, 0.15f, 0.15f)); // Dark Grey
        CreateMat("Court_Post", new Color(0.72f, 0.72f, 0.72f)); // Silver

        // Out of Bounds Floor
        CreateMat("Floor_OutOfBounds", new Color(0.55f, 0.75f, 0.35f)); // Lighter Green

        // Background / Environment
        CreateMat("Environment_Ground", new Color(0.40f, 0.65f, 0.28f)); // Grass
        CreateMat("Environment_Fence", new Color(0.30f, 0.30f, 0.30f)); // Dark Grey
        CreateMat("Environment_Sky", new Color(0.53f, 0.81f, 0.98f)); // Sky Blue

        // Zone Overlays (semi-transparent debug colors)
        CreateMatTransparent("Zone_A_Kitchen_Debug", new Color(1f, 0.3f, 0.3f, 0.25f));
        CreateMatTransparent("Zone_B_Kitchen_Debug", new Color(1f, 0.3f, 0.3f, 0.25f));
        CreateMatTransparent("Zone_Serve_Debug", new Color(0.3f, 0.3f, 1f, 0.15f));

        // Ball
        CreateMat("Ball", new Color(0.95f, 0.95f, 0.40f)); // Yellow

        // Paddle
        CreateMat("Paddle_Face", new Color(0.15f, 0.35f, 0.80f)); // Blue Face
        CreateMat("Paddle_Handle", new Color(0.25f, 0.12f, 0.05f)); // Brown Grip
        CreateMat("Paddle_Edge", new Color(0.85f, 0.85f, 0.85f)); // Light Grey Edge

        // UI
        CreateMat("UI_ScoreboardBG", new Color(0.05f, 0.05f, 0.05f)); // Near black

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[MaterialFactory] - All materials created in Assets/Art/Materials/");
    }

    static void CreateMat(string name, Color color)
    {
        string fullPath = $"{MatPath}/{name}.mat";
        if (File.Exists(Application.dataPath + fullPath.Replace("Assets", "")))
        {
            Debug.Log("[MaterialFactory] Skipped (already exists): {name}");
            return;
        }

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        AssetDatabase.CreateAsset(mat, fullPath);
    }

    static void CreateMatTransparent(string name, Color color)
    {
        string fullPath = $"{MatPath}/{name}.mat";
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // Set to transparent surface type
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_AlphaClip", 0);
        mat.renderQueue = 3000;
        mat.color = color;

        AssetDatabase.CreateAsset(mat, fullPath);
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path);
            string folder = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif