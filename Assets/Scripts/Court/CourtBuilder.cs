using UnityEngine;
using UnityEditor;

public class CourtBuilder : MonoBehaviour
{
    [Header("Court Dimensions (feet -> units)")]
    public static float courtWidth   = 20f;
    public float courtLength  = 44f;
    public float kitchenDepth = 7f;
    [Tooltip("How far behind the baseline the serving position zones extend.")]
    public float serveZoneDepth = 5f;
      public static float netHeight = 1.75f;

    [Header("Materials")]
    public Material courtMaterial;
    // public Material netMaterial;
    public Material lineMaterial;
    public Material outOfBoundsMaterial;

    // ── Visual settings for Net ───────────────────────────────────────────────────────
    private static readonly Color ColourPost    = new Color(0.85f, 0.85f, 0.80f); // off-white post
    private static readonly Color ColourTopTape = new Color(0.95f, 0.95f, 0.90f); // bright white top band
    private static readonly Color ColourString  = new Color(0.55f, 0.55f, 0.55f); // mid-grey strings

    // Net grid density — how many cells wide and tall
    private const int GridColumns = 16;   // horizontal divisions
    private const int GridRows    =  6;   // vertical divisions
    // Bar thickness — thin enough to see through, thick enough to read
    private const float StringThickness = 0.012f;
    private const float PostRadius      = 0.04f;


    [Header("Out-of-Bounds Floor")]
    [Tooltip("Total size (width & depth) of the OOB floor plane in world units.")]
    public float oobFloorSize = 200f;
    
    [Header("Lines")]
    public float lineThickness = 0.2f;

    [ContextMenu("Build Court")]
    public void BuildCourt()
    {
        ClearChildren();
        BuildOutOfBoundsFloor();
        BuildFloor();
        BuildNet();
        BuildLines();
        BuildZones();
        Debug.Log("Court built successfully.");
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    void BuildOutOfBoundsFloor()
    {
        GameObject oob = GameObject.CreatePrimitive(PrimitiveType.Plane);
        oob.name = "OOB Floor";
        oob.tag = "Out Of Bounds";
        oob.transform.SetParent(transform);
        oob.transform.localPosition = new Vector3(0f, -0.005f, 0f); // just below court surface
        oob.transform.localScale = new Vector3(oobFloorSize / 10f, 1f, oobFloorSize / 10f);

        if (outOfBoundsMaterial) oob.GetComponent<Renderer>().sharedMaterial = outOfBoundsMaterial;
    }

    void BuildFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Court Mesh";
        floor.tag = "Court";
        floor.transform.SetParent(transform);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localScale = new Vector3(courtWidth / 10f, 1f, courtLength / 10f);

        if (courtMaterial) floor.GetComponent<Renderer>().sharedMaterial = courtMaterial;
    }

    // void BuildNet()
    // {
    //     GameObject net = GameObject.CreatePrimitive(PrimitiveType.Cube);
    //     net.name = "Net";
    //     net.tag = "Net";
    //     net.transform.SetParent(transform);
    //     net.transform.localPosition = new Vector3(0f, netHeight / 2f, 0f);
    //     net.transform.localScale = new Vector3(courtWidth, netHeight, 0.1f);

    //     if (netMaterial) net.GetComponent<Renderer>().sharedMaterial = netMaterial;
    // }

    void BuildLines()
    {
        GameObject linesRoot = new GameObject("Lines");
        linesRoot.transform.SetParent(transform);
        linesRoot.transform.localPosition = Vector3.zero;

        float hw = courtWidth / 2f;   // half width  (X)
        float hl = courtLength / 2f;  // half length (Z)
        float t  = lineThickness;
        float y  = 0.002f;            // just above court surface to avoid z-fighting

        // Sidelines — run full length along Z
        CreateLine(linesRoot, "Sideline_Left",  new Vector3(-hw, y, 0),  new Vector3(t, 0.01f, courtLength + t));
        CreateLine(linesRoot, "Sideline_Right", new Vector3( hw, y, 0),  new Vector3(t, 0.01f, courtLength + t));

        // Baselines — run full width along X
        CreateLine(linesRoot, "Baseline_A", new Vector3(0, y, -hl), new Vector3(courtWidth, 0.01f, t));
        CreateLine(linesRoot, "Baseline_B", new Vector3(0, y,  hl), new Vector3(courtWidth, 0.01f, t));

        // Kitchen / Non-Volley Zone lines — parallel to net, 7ft each side
        CreateLine(linesRoot, "Kitchen_Line_A", new Vector3(0, y, -kitchenDepth), new Vector3(courtWidth, 0.01f, t));
        CreateLine(linesRoot, "Kitchen_Line_B", new Vector3(0, y,  kitchenDepth), new Vector3(courtWidth, 0.01f, t));

        // Center service lines — from kitchen line to baseline, splits each service box
        float serviceLength = hl - kitchenDepth;
        float serviceMidA   = -(kitchenDepth + serviceLength / 2f);
        float serviceMidB   =   kitchenDepth + serviceLength / 2f;

        CreateLine(linesRoot, "Center_Line_A", new Vector3(0, y, serviceMidA), new Vector3(t, 0.01f, serviceLength));
        CreateLine(linesRoot, "Center_Line_B", new Vector3(0, y, serviceMidB), new Vector3(t, 0.01f, serviceLength));
    }

    void CreateLine(GameObject parent, string lineName, Vector3 localPos, Vector3 size)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = lineName;
        line.transform.SetParent(parent.transform);
        line.transform.localPosition = localPos;
        line.transform.localScale = size;

        DestroyImmediate(line.GetComponent<BoxCollider>());

        if (lineMaterial) line.GetComponent<Renderer>().sharedMaterial = lineMaterial;
    }

    void CreateZone(GameObject parent, ZoneID id, Vector3 localPos, Vector3 size)
    {
        GameObject go = new GameObject(id.ToString());
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos;

        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = size;

        go.AddComponent<CourtZone>().zoneID = id;
    }

    void BuildZones()
    {
        GameObject zonesRoot = new GameObject("Zones");
        zonesRoot.transform.SetParent(transform);
        zonesRoot.transform.localPosition = Vector3.zero;

        float half  = courtLength / 2f;   // 22 — distance from center to baseline
        float hw    = courtWidth  / 2f;   // 10 — half court width
        float hw2   = hw / 2f;            //  5 — quarter court width (center of each service box)

        // Service box dimensions (between kitchen line and baseline)
        float serviceLen  = half - kitchenDepth;               // 15
        float serviceMidA = -(kitchenDepth + serviceLen / 2f); // -14.5
        float serviceMidB =   kitchenDepth + serviceLen / 2f;  // +14.5

        // Serving position dimensions (behind baseline, off court)
        float serveMidA = -(half + serveZoneDepth / 2f);       // -24.5
        float serveMidB =   half + serveZoneDepth / 2f;        // +24.5

        // ── Side A (negative Z) ─────────────────────────────────────────────

        // Kitchen / NVZ: net (Z=0) → kitchen line (Z=-7)
        CreateZone(zonesRoot, ZoneID.Zone_A_Kitchen,
            new Vector3(0, 0, -(kitchenDepth / 2f)),
            new Vector3(courtWidth, 1f, kitchenDepth));

        // Service boxes on court — where a serve from Side B must land.
        // Left/Right from Team A's own perspective (facing +Z): left = −X, right = +X.
        CreateZone(zonesRoot, ZoneID.Zone_A_Left,
            new Vector3(-hw2, 0, serviceMidA),
            new Vector3(hw, 1f, serviceLen));
        CreateZone(zonesRoot, ZoneID.Zone_A_Right,
            new Vector3( hw2, 0, serviceMidA),
            new Vector3(hw, 1f, serviceLen));

        // Serving positions — where the Side A server stands behind the baseline.
        CreateZone(zonesRoot, ZoneID.Zone_A_Serve_Left,
            new Vector3(-hw2, 0, serveMidA),
            new Vector3(hw, 1f, serveZoneDepth));
        CreateZone(zonesRoot, ZoneID.Zone_A_Serve_Right,
            new Vector3( hw2, 0, serveMidA),
            new Vector3(hw, 1f, serveZoneDepth));

        // ── Side B (positive Z) ─────────────────────────────────────────────

        // Kitchen / NVZ: net (Z=0) → kitchen line (Z=+7)
        CreateZone(zonesRoot, ZoneID.Zone_B_Kitchen,
            new Vector3(0, 0, kitchenDepth / 2f),
            new Vector3(courtWidth, 1f, kitchenDepth));

        // Service boxes on court — where a serve from Side A must land.
        // Left/Right from Team B's own perspective (facing −Z): left = +X, right = −X.
        CreateZone(zonesRoot, ZoneID.Zone_B_Left,
            new Vector3( hw2, 0, serviceMidB),
            new Vector3(hw, 1f, serviceLen));
        CreateZone(zonesRoot, ZoneID.Zone_B_Right,
            new Vector3(-hw2, 0, serviceMidB),
            new Vector3(hw, 1f, serviceLen));

        // Serving positions — where the Side B server stands behind the baseline.
        // Left/Right from Team B's own perspective.
        CreateZone(zonesRoot, ZoneID.Zone_B_Serve_Left,
            new Vector3( hw2, 0, serveMidB),
            new Vector3(hw, 1f, serveZoneDepth));
        CreateZone(zonesRoot, ZoneID.Zone_B_Serve_Right,
            new Vector3(-hw2, 0, serveMidB),
            new Vector3(hw, 1f, serveZoneDepth));
    }

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
            #if UNITY_EDITOR
            Undo.DestroyObjectImmediate(oldNet.gameObject);
            #endif
            Debug.Log("[NetBuilder] Removed old solid net.");
        }

        // ── Create Net root ───────────────────────────────────────────────────
        var netRoot = new GameObject("Net");
        #if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(netRoot, "Build Net");
        #endif
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

        #if UNITY_EDITOR
        Selection.activeGameObject = netRoot;
        #endif
        Debug.Log($"[NetBuilder] Net built — {GridColumns * GridRows} cells, invisible collider retained.");
    }

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
