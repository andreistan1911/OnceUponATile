using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Milestone 1: fixed isometric map, hover highlight, click to place a 1x1 piece.
/// Builds everything at runtime from a generated diamond sprite, so no art assets are needed.
/// Works with the old Input Manager and the new Input System (picked at compile time).
/// </summary>
public class PlacementController : MonoBehaviour
{
    [Header("Map")]
    [SerializeField] int width = 8;
    [SerializeField] int height = 8;

    [Header("Colors")]
    [SerializeField] Color groundColorA = new Color(0.66f, 0.82f, 0.52f);
    [SerializeField] Color groundColorB = new Color(0.60f, 0.77f, 0.47f);
    [SerializeField] Color pieceColor = new Color(0.93f, 0.55f, 0.35f);
    [SerializeField] Color validHoverColor = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] Color invalidHoverColor = new Color(1f, 0.2f, 0.2f, 0.65f);
    [SerializeField] Color backgroundColor = new Color(0.98f, 0.93f, 0.82f);

    const float TileWidth = 1f;
    const float TileHeight = 0.5f;

    IsoGrid grid;
    Sprite diamond;
    SpriteRenderer hover;
    Camera cam;
    Vector2Int hoveredCell;
    bool hasHover;
    int placedCount;

    void Awake()
    {
        grid = new IsoGrid(width, height, TileWidth, TileHeight);
        diamond = CreateDiamondSprite();
        BuildGround();
        hover = CreateSprite("Hover", Vector3.zero, Color.white, 500, 1f);
        hover.gameObject.SetActive(false);
    }

    void Start()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("PlacementController: no camera tagged MainCamera in the scene.");
            enabled = false;
            return;
        }
        SetupCamera();
    }

    void Update()
    {
        Vector3 world = cam.ScreenToWorldPoint(
            new Vector3(PointerPosition().x, PointerPosition().y, -cam.transform.position.z));
        hoveredCell = grid.WorldToCell(world);
        hasHover = grid.InBounds(hoveredCell);

        hover.gameObject.SetActive(hasHover);
        if (!hasHover) return;

        bool canPlace = grid.CanPlace(hoveredCell);
        hover.transform.position = grid.CellToWorld(hoveredCell);
        hover.color = canPlace ? validHoverColor : invalidHoverColor;

        if (canPlace && ClickedThisFrame())
        {
            grid.TryPlace(hoveredCell);
            SpawnPiece(hoveredCell);
            placedCount++;
        }
    }

    // ---------- building the scene ----------

    void BuildGround()
    {
        var root = new GameObject("Ground").transform;
        root.SetParent(transform, false);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = new Vector2Int(x, y);
                var color = ((x + y) % 2 == 0) ? groundColorA : groundColorB;
                var sr = CreateSprite($"Cell_{x}_{y}", grid.CellToWorld(cell), color, 0, 0.97f);
                sr.transform.SetParent(root, true);
            }
        }
    }

    void SpawnPiece(Vector2Int cell)
    {
        // Higher x+y = closer to the viewer = drawn on top.
        var sr = CreateSprite($"Piece_{cell.x}_{cell.y}", grid.CellToWorld(cell), pieceColor, 10 + cell.x + cell.y, 0.8f);
        sr.transform.SetParent(transform, true);
    }

    SpriteRenderer CreateSprite(string objName, Vector3 pos, Color color, int sortingOrder, float scale)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = diamond;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    void SetupCamera()
    {
        grid.GetWorldBounds(out Vector2 min, out Vector2 max);
        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;
        const float margin = 1f;

        cam.orthographic = true;
        cam.transform.position = new Vector3(center.x, center.y, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
        cam.orthographicSize = Mathf.Max(size.y * 0.5f + margin, (size.x * 0.5f + margin) / cam.aspect);
    }

    /// <summary>Generates a 128x64 diamond (2:1) with a slightly soft edge. 128 px per unit => 1 x 0.5 world units.</summary>
    static Sprite CreateDiamondSprite()
    {
        const int w = 128, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                float ny = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                float alpha = Mathf.Clamp01((1f - (nx + ny)) * 24f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 128f);
    }

    // ---------- input (old Input Manager or new Input System) ----------

    static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    static bool ClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    // Temporary debug readout, remove when real UI exists.
    void OnGUI()
    {
        string cellText = hasHover ? $"({hoveredCell.x}, {hoveredCell.y})" : "-";
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = new Color(0.25f, 0.2f, 0.15f);
        GUI.Label(new Rect(10, 10, 500, 30), $"Cell: {cellText}   Placed: {placedCount}", style);
    }
}
