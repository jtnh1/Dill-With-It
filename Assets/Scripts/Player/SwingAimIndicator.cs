using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class SwingAimIndicator : MonoBehaviour
{
    [Header("Colours")]
    public Color colorReady = new Color(0.1f, 1f, 0.1f, 1f);
    public Color colorIdle  = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Header("Shape")]
    public float circleRadius   = 1.5f;
    public int   circleSegments = 48;
    public float lineWidth      = 0.06f;
    public float headSize       = 0.55f;

    private Transform    _root;
    private Material     _mat;
    private BallController _ball;
    private float        _reach;

    private void Start()
    {
        _reach = GetComponent<PlayerController>().swingReach;
        _ball  = FindAnyObjectByType<BallController>();
        Build();
    }

    private void LateUpdate()
    {
        if (_root == null) return;
        if (_ball == null) _ball = FindAnyObjectByType<BallController>();

        _root.position = new Vector3(transform.position.x, 0.03f, transform.position.z);

        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (fwd.sqrMagnitude > 0.001f)
            _root.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        bool inRange = _ball != null && HorizontalDist(_ball.transform.position) <= _reach;
        SetColor(inRange ? colorReady : colorIdle);
    }

    float HorizontalDist(Vector3 other) =>
        new Vector2(transform.position.x - other.x, transform.position.z - other.z).magnitude;

    void Build()
    {
        _root = new GameObject("SwingAimIndicator").transform;
        _mat  = BuildMaterial(colorIdle);
        BuildCircle();
        BuildArrow();
    }

    void BuildCircle()
    {
        var go = new GameObject("Circle");
        go.transform.SetParent(_root, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace      = false;
        lr.loop               = true;
        lr.positionCount      = circleSegments;
        lr.startWidth         = lineWidth;
        lr.endWidth           = lineWidth;
        lr.sharedMaterial     = _mat;
        lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows     = false;

        for (int i = 0; i < circleSegments; i++)
        {
            float a = (i / (float)circleSegments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Sin(a) * circleRadius, 0f, Mathf.Cos(a) * circleRadius));
        }
    }

    void BuildArrow()
    {
        // Tip at the forward edge of the circle; chevron arms extend back from the tip.
        float arm   = headSize * 0.7071f;   // 45-degree projection
        Vector3 tip = new Vector3(0f, 0f, circleRadius);

        MakeSegment("ArrowR", tip, tip + new Vector3( arm, 0f, -arm));
        MakeSegment("ArrowL", tip, tip + new Vector3(-arm, 0f, -arm));
    }

    void MakeSegment(string id, Vector3 a, Vector3 b)
    {
        var go = new GameObject(id);
        go.transform.SetParent(_root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace      = false;
        lr.positionCount      = 2;
        lr.startWidth         = lineWidth;
        lr.endWidth           = lineWidth;
        lr.sharedMaterial     = _mat;
        lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows     = false;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    void SetColor(Color c)
    {
        if (_mat == null) return;
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        else                                _mat.color = c;
    }

    static Material BuildMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else                               mat.color = color;
        return mat;
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root.gameObject);
        if (_mat  != null) Destroy(_mat);
    }
}
