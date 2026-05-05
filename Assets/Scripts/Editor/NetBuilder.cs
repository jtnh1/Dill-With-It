// NetBuilder.cs
// EDITOR ONLY — must live in Assets/Scripts/Editor/
// Usage: Pickleball → Build Net
// Deletes the old solid Net block and replaces it with a proper
// grid net made from thin horizontal and vertical bars.
// The Court root must exist in the scene (run Build Court first).

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class NetBuilder
{
    // ── Visual settings ───────────────────────────────────────────────────────
    private static readonly Color ColourPost    = new Color(0.85f, 0.85f, 0.80f); // off-white post
    private static readonly Color ColourTopTape = new Color(0.95f, 0.95f, 0.90f); // bright white top band
    private static readonly Color ColourString  = new Color(0.55f, 0.55f, 0.55f); // mid-grey strings

    public static float courtWidth = 20f;
    public static float netHeight = 1.75f;

    // Net grid density — how many cells wide and tall
    // Real pickleball net has ~2in squares; we use a coarser game-feel grid
    private const int GridColumns = 16;   // horizontal divisions
    private const int GridRows    =  6;   // vertical divisions

    // Bar thickness — thin enough to see through, thick enough to read
    private const float StringThickness = 0.012f;
    private const float PostRadius      = 0.04f;

    [MenuItem("Pickleball/Build Net")]
    public static void BuildNet()
    {
        // ── Find or validate Court root ───────────────────────────────────────
        var court = GameObject.Find("Court");
        if (court == null)
        {
            Debug.LogError("[NetBuilder] No 'Court' object found. Run Pickleball → Build Court first.");
            return;
        }

        // Remove old solid net block if present
        var oldNet = court.transform.Find("Net");
        if (oldNet != null)
        {
            Undo.DestroyObjectImmediate(oldNet.gameObject);
            Debug.Log("[NetBuilder] Removed old solid net.");
        }

        // ── Create Net root ───────────────────────────────────────────────────
        var netRoot = new GameObject("Net");
        Undo.RegisterCreatedObjectUndo(netRoot, "Build Net");
        netRoot.transform.SetParent(court.transform, false);
        netRoot.transform.localPosition = Vector3.zero;

        float netW = courtWidth;
        float netH = netHeight;
        float halfW = netW / 2f;

        // ── 1. Side posts ─────────────────────────────────────────────────────
        BuildPost(netRoot, "Post_Left",  new Vector3(-halfW, netH / 2f, 0f), netH);
        BuildPost(netRoot, "Post_Right", new Vector3( halfW, netH / 2f, 0f), netH);

        // ── 2. Top tape (wider white band) ────────────────────────────────────
        BuildBar(netRoot, "TopTape",
            new Vector3(0f, netH, 0f),
            new Vector3(netW, 0.04f, 0.025f),
            ColourTopTape);

        // ── 3. Bottom anchor ──────────────────────────────────────────────────
        BuildBar(netRoot, "BottomAnchor",
            new Vector3(0f, 0.01f, 0f),
            new Vector3(netW, 0.015f, 0.015f),
            ColourString);

        // ── 4. Horizontal strings ─────────────────────────────────────────────
        // Evenly spaced rows between bottom and top tape
        for (int row = 0; row <= GridRows; row++)
        {
            float t   = (float)row / GridRows;
            float y   = Mathf.Lerp(0.02f, netH - 0.02f, t);
            string id = $"HString_{row:00}";

            BuildBar(netRoot, id,
                new Vector3(0f, y, 0f),
                new Vector3(netW, StringThickness, StringThickness),
                ColourString);
        }

        // ── 5. Vertical strings ───────────────────────────────────────────────
        for (int col = 0; col <= GridColumns; col++)
        {
            float t   = (float)col / GridColumns;
            float x   = Mathf.Lerp(-halfW + PostRadius, halfW - PostRadius, t);
            string id = $"VString_{col:00}";

            BuildBar(netRoot, id,
                new Vector3(x, netH / 2f, 0f),
                new Vector3(StringThickness, netH, StringThickness),
                ColourString);
        }

        // ── 6. Centre strap (vertical white band at net midpoint) ─────────────
        BuildBar(netRoot, "CentreStrap",
            new Vector3(0f, netH * 0.4f, 0f),
            new Vector3(0.04f, netH * 0.8f, 0.015f),
            ColourTopTape);

        // ── 7. Invisible solid collider so ball still bounces off net ─────────
        // The visual strings are too thin for reliable physics — one hidden
        // collider handles all bouncing cleanly
        var colliderObj = new GameObject("NetCollider");
        colliderObj.transform.SetParent(netRoot.transform, false);
        colliderObj.transform.localPosition = new Vector3(0f, netH / 2f, 0f);
        var box  = colliderObj.AddComponent<BoxCollider>();
        box.size = new Vector3(netW, netH, 0.05f);
        // No renderer — purely physics

        Selection.activeGameObject = netRoot;
        Debug.Log($"[NetBuilder] Net built — {GridColumns * GridRows} cells, invisible collider retained.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void BuildPost(GameObject parent, string postName, Vector3 localPos, float height)
    {
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = postName;
        post.transform.SetParent(parent.transform, false);
        post.transform.localPosition = localPos;
        post.transform.localScale    = new Vector3(PostRadius * 2f, height / 2f, PostRadius * 2f);
        Object.DestroyImmediate(post.GetComponent<Collider>());
        ApplyFlatMaterial(post, ColourPost);
    }

    private static void BuildBar(GameObject parent, string barName,
                                  Vector3 localPos, Vector3 localScale, Color colour)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = barName;
        bar.transform.SetParent(parent.transform, false);
        bar.transform.localPosition = localPos;
        bar.transform.localScale    = localScale;
        Object.DestroyImmediate(bar.GetComponent<Collider>());
        ApplyFlatMaterial(bar, colour);
    }

    private static void ApplyFlatMaterial(GameObject go, Color colour)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = colour;
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_Metallic",   0f);
        rend.sharedMaterial = mat;
    }
}
#endif