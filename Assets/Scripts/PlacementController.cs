using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Fixed isometric map + a piece strip at the bottom of the screen.
/// Milestone 1: hover highlight, click to place a 1x1 piece.
/// Milestone 2: strip of 10 pieces (first 3 selectable), 3 piece types, queue shifts when a piece is used.
/// Milestone 3: game over when none of the 3 available pieces has a legal cell; R restarts.
/// Milestone 4: placement rules (road: anywhere, house: next to a road, park: next to a house)
///              and a starter road in the middle of the map.
/// Everything is built at runtime, so no art assets are needed.
/// The strip is drawn with IMGUI (OnGUI) as a temporary UI layer; the queue logic lives in PieceQueue.
/// Works with the old Input Manager and the new Input System (picked at compile time).
/// </summary>
public class PlacementController : MonoBehaviour
{
    [Header("Map")]
    [SerializeField] int width = 8;
    [SerializeField] int height = 8;

    [Header("Map colors")]
    [SerializeField] Color groundColorA = new Color(0.66f, 0.82f, 0.52f);
    [SerializeField] Color groundColorB = new Color(0.60f, 0.77f, 0.47f);
    [SerializeField] Color invalidHoverColor = new Color(1f, 0.2f, 0.2f, 0.65f);
    [SerializeField] Color backgroundColor = new Color(0.98f, 0.93f, 0.82f);

    [Header("Piece colors")]
    [SerializeField] Color houseColor = new Color(0.93f, 0.55f, 0.35f);
    [SerializeField] Color roadColor = new Color(0.72f, 0.67f, 0.60f);
    [SerializeField] Color parkColor = new Color(0.27f, 0.55f, 0.40f);

    [Header("Piece strip")]
    [SerializeField] [Range(0.10f, 0.25f)] float stripHeightFraction = 0.15f;
    [SerializeField] Color panelColor = new Color(0.36f, 0.25f, 0.18f, 0.96f);
    [SerializeField] Color panelEdgeColor = new Color(0.58f, 0.43f, 0.30f);
    [SerializeField] Color cardColor = new Color(0.98f, 0.93f, 0.82f);
    [SerializeField] Color cardWaitingColor = new Color(0.98f, 0.93f, 0.82f, 0.45f);
    [SerializeField] Color selectedBorderColor = new Color(0.98f, 0.78f, 0.25f);

    [Header("Game over")]
    [SerializeField] Color gameOverOverlayColor = new Color(0.20f, 0.14f, 0.10f, 0.60f);

    const float TileWidth = 1f;
    const float TileHeight = 0.5f;
    const int QueueCapacity = 10;
    const int ActiveCount = 3;

    IsoGrid grid;
    PieceQueue queue;
    int selectedSlot;
    bool gameOver;

    Sprite diamond;
    Texture2D roundedTex;
    Transform piecesRoot;
    SpriteRenderer hover;
    Camera cam;
    Vector2Int hoveredCell;
    bool hasHover;
    int placedCount;
    Vector2Int lastScreenSize;

    // Strip layout in IMGUI coordinates (origin top-left, y grows downward).
    Rect panelRect;
    readonly Rect[] slotRects = new Rect[QueueCapacity];
    GUIStyle keyStyle;
    GUIStyle debugStyle;
    GUIStyle titleStyle;
    GUIStyle subtitleStyle;

    void Awake()
    {
        grid = new IsoGrid(width, height, TileWidth, TileHeight);
        diamond = CreateDiamondSprite();
        roundedTex = CreateRoundedSquareTexture();
        BuildGround();
        piecesRoot = new GameObject("Pieces").transform;
        piecesRoot.SetParent(transform, false);
        hover = CreateSprite("Hover", Vector3.zero, Color.white, 500, 1f);
        hover.gameObject.SetActive(false);
        StartNewGame();
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
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        ComputeStripLayout();
        SetupCamera();
    }

    void Update()
    {
        // Re-fit camera and strip if the window / Game view size changed.
        if (Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
        {
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            ComputeStripLayout();
            SetupCamera();
        }

        if (PressedRestartKey()) RestartGame();

        if (gameOver)
        {
            hasHover = false;
            hover.gameObject.SetActive(false);
            return;
        }

        Vector2 pointer = PointerPosition();
        Vector2 guiPointer = new Vector2(pointer.x, Screen.height - pointer.y); // IMGUI y is top-down
        bool click = ClickedThisFrame();

        int key = PressedSlotKey();
        if (key >= 0) selectedSlot = key;

        // Pointer over the strip: select a piece, never touch the map.
        if (panelRect.Contains(guiPointer))
        {
            hasHover = false;
            hover.gameObject.SetActive(false);
            if (click)
            {
                for (int i = 0; i < ActiveCount; i++)
                    if (slotRects[i].Contains(guiPointer)) selectedSlot = i;
            }
            return;
        }

        // Pointer over the map.
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, -cam.transform.position.z));
        hoveredCell = grid.WorldToCell(world);
        hasHover = grid.InBounds(hoveredCell);

        hover.gameObject.SetActive(hasHover);
        if (!hasHover) return;

        PieceType selectedType = queue.Peek(selectedSlot);
        bool canPlace = IsLegal(hoveredCell, selectedType);
        Color pieceColor = ColorOf(selectedType);
        hover.transform.position = grid.CellToWorld(hoveredCell);
        hover.color = canPlace ? new Color(pieceColor.r, pieceColor.g, pieceColor.b, 0.7f) : invalidHoverColor;

        if (canPlace && click)
        {
            PieceType placed = queue.Take(selectedSlot);
            grid.TryPlace(hoveredCell, placed);
            SpawnPiece(hoveredCell, placed);
            placedCount++;

            if (!HasLegalMove()) gameOver = true;
        }
    }

    // ---------- rules ----------

    /// <summary>
    /// The single place that decides if a piece may go on a cell.
    ///   Road : any free cell.
    ///   House: a free cell with a road on one of its 4 sides.
    ///   Park : a free cell with a house on one of its 4 sides.
    /// </summary>
    bool IsLegal(Vector2Int cell, PieceType type)
    {
        if (!grid.CanPlace(cell)) return false;
        switch (type)
        {
            case PieceType.Road: return true;
            case PieceType.House: return grid.HasNeighbor(cell, PieceType.Road);
            default: return grid.HasNeighbor(cell, PieceType.House); // Park
        }
    }

    /// <summary>True if at least one of the currently available pieces can be placed somewhere.</summary>
    bool HasLegalMove()
    {
        for (int slot = 0; slot < ActiveCount; slot++)
        {
            PieceType type = queue.Peek(slot);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (IsLegal(new Vector2Int(x, y), type)) return true;
        }
        return false;
    }

    /// <summary>Empties the map, puts the starter road in the middle and deals a first queue that allows a move.</summary>
    void StartNewGame()
    {
        grid.Clear();
        var start = new Vector2Int(width / 2, height / 2);
        grid.TryPlace(start, PieceType.Road);
        SpawnPiece(start, PieceType.Road);

        // The first 3 pieces must allow at least one move (e.g. not three parks).
        queue = new PieceQueue(QueueCapacity, ActiveCount);
        for (int i = 0; i < 100 && !HasLegalMove(); i++)
            queue = new PieceQueue(QueueCapacity, ActiveCount);

        selectedSlot = 0;
        placedCount = 0;
        gameOver = false;
    }

    void RestartGame()
    {
        for (int i = piecesRoot.childCount - 1; i >= 0; i--)
            Destroy(piecesRoot.GetChild(i).gameObject);
        StartNewGame();
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

    void SpawnPiece(Vector2Int cell, PieceType type)
    {
        // Higher x+y = closer to the viewer = drawn on top.
        var sr = CreateSprite($"{type}_{cell.x}_{cell.y}", grid.CellToWorld(cell), ColorOf(type), 10 + cell.x + cell.y, 0.8f);
        sr.transform.SetParent(piecesRoot, true);
    }

    Color ColorOf(PieceType type)
    {
        switch (type)
        {
            case PieceType.House: return houseColor;
            case PieceType.Road: return roadColor;
            default: return parkColor;
        }
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

    /// <summary>
    /// Fits the whole map into the part of the screen above the strip, centered there.
    /// The strip covers the bottom `stripHeightFraction` of the screen.
    /// </summary>
    void SetupCamera()
    {
        grid.GetWorldBounds(out Vector2 min, out Vector2 max);
        Vector2 mapCenter = (min + max) * 0.5f;
        Vector2 mapSize = max - min;
        const float margin = 0.75f;
        float f = stripHeightFraction;
        float aspect = (float)Screen.width / Screen.height;

        // Full visible world height H. The map must fit in the free part (height H * (1 - f)) and in the width.
        float viewH = Mathf.Max((mapSize.y + 2f * margin) / (1f - f), (mapSize.x + 2f * margin) / aspect);

        cam.orthographic = true;
        cam.orthographicSize = viewH * 0.5f;
        // Free region center = camY + f*H/2, so camY = mapCenterY - f*H/2.
        cam.transform.position = new Vector3(mapCenter.x, mapCenter.y - f * viewH * 0.5f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    // ---------- piece strip (layout + drawing) ----------

    /// <summary>
    /// Horizontal strip at the bottom: [3 big selectable cards] [gap] [7 small waiting cards], centered.
    /// All sizes derive from the strip height; the whole row shrinks if the window is too narrow.
    /// </summary>
    void ComputeStripLayout()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float panelH = sh * stripHeightFraction;

        float activeSize = panelH * 0.74f;
        float waitSize = panelH * 0.52f;
        float gap = panelH * 0.10f;
        float groupGap = gap * 2.5f;
        int waitCount = QueueCapacity - ActiveCount;

        float total = ActiveCount * activeSize + waitCount * waitSize + (QueueCapacity - 2) * gap + groupGap;
        float scale = Mathf.Min(1f, (sw * 0.96f) / total);
        activeSize *= scale;
        waitSize *= scale;
        gap *= scale;
        groupGap *= scale;
        total *= scale;

        panelRect = new Rect(0f, sh - panelH, sw, panelH);
        float x = (sw - total) * 0.5f;
        for (int i = 0; i < QueueCapacity; i++)
        {
            float size = i < ActiveCount ? activeSize : waitSize;
            float y = panelRect.y + (panelH - size) * 0.5f;
            slotRects[i] = new Rect(x, y, size, size);
            x += size + (i == ActiveCount - 1 ? groupGap : gap);
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (queue == null || diamond == null) return;

        if (keyStyle == null)
        {
            keyStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = new Color(0.36f, 0.25f, 0.18f);
            debugStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            debugStyle.normal.textColor = new Color(0.25f, 0.2f, 0.15f);
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.98f, 0.93f, 0.82f);
            subtitleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            subtitleStyle.normal.textColor = new Color(0.98f, 0.93f, 0.82f);
        }

        // Panel + light top edge.
        DrawRect(panelRect, panelColor);
        DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), panelEdgeColor);

        keyStyle.fontSize = Mathf.RoundToInt(panelRect.height * 0.16f);

        for (int i = 0; i < QueueCapacity; i++)
        {
            Rect r = slotRects[i];
            bool active = i < ActiveCount;
            bool selected = active && i == selectedSlot && !gameOver;
            if (selected) r.y -= r.height * 0.10f; // selected card is raised

            if (selected)
                DrawRounded(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), selectedBorderColor);
            DrawRounded(r, active ? cardColor : cardWaitingColor);

            // Piece icon: the same 2:1 diamond used on the map, tinted by piece type.
            float iconW = r.width * 0.72f;
            var icon = new Rect(r.center.x - iconW * 0.5f, r.center.y - iconW * 0.25f, iconW, iconW * 0.5f);
            Color pc = ColorOf(queue.Peek(i));
            if (!active) pc.a = 0.65f;
            GUI.color = pc;
            GUI.DrawTexture(icon, diamond.texture);
            GUI.color = Color.white;

            if (active)
                GUI.Label(new Rect(r.x + r.width * 0.08f, r.y + r.height * 0.03f, r.width * 0.4f, r.height * 0.3f), (i + 1).ToString(), keyStyle);
        }

        // Game over overlay: dims everything and shows the result.
        if (gameOver)
        {
            float sw = Screen.width;
            float sh = Screen.height;
            float cy = panelRect.y * 0.5f; // center of the map area
            DrawRect(new Rect(0f, 0f, sw, sh), gameOverOverlayColor);
            titleStyle.fontSize = Mathf.RoundToInt(sh * 0.09f);
            subtitleStyle.fontSize = Mathf.RoundToInt(sh * 0.035f);
            GUI.Label(new Rect(0f, cy - sh * 0.09f, sw, sh * 0.12f), "Game over", titleStyle);
            GUI.Label(new Rect(0f, cy + sh * 0.03f, sw, sh * 0.06f), $"Pieces placed: {placedCount}   -   press R to restart", subtitleStyle);
        }

        // Temporary debug readout, remove when real UI exists.
        string cellText = hasHover ? $"({hoveredCell.x}, {hoveredCell.y})" : "-";
        GUI.Label(new Rect(10, 10, 600, 30), $"Cell: {cellText}   Placed: {placedCount}   (R = restart)", debugStyle);
    }

    static void DrawRect(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void DrawRounded(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, roundedTex);
        GUI.color = Color.white;
    }

    // ---------- generated textures ----------

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

    /// <summary>White 64x64 rounded square, used (tinted) for the strip cards.</summary>
    static Texture2D CreateRoundedSquareTexture()
    {
        const int s = 64;
        const float radius = 16f;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color32[s * s];
        float half = s * 0.5f;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
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

    /// <summary>Returns 0, 1 or 2 if the number key 1, 2 or 3 was pressed this frame, otherwise -1.</summary>
    static int PressedSlotKey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return -1;
        if (kb.digit1Key.wasPressedThisFrame) return 0;
        if (kb.digit2Key.wasPressedThisFrame) return 1;
        if (kb.digit3Key.wasPressedThisFrame) return 2;
        return -1;
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) return 2;
        return -1;
#endif
    }

    static bool PressedRestartKey()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }
}
