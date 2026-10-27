using UnityEngine;

/// <summary>
/// Pure grid logic: occupancy + conversion between grid cells and world positions.
/// No MonoBehaviour, no visuals. Standard 2:1 isometric layout.
/// Cell (0,0) is the bottom corner of the diamond; +x goes up-right, +y goes up-left.
/// </summary>
public class IsoGrid
{
    public readonly int Width;
    public readonly int Height;
    public readonly float TileWidth;   // world width of one diamond
    public readonly float TileHeight;  // world height of one diamond (usually half of width)

    readonly bool[,] occupied;

    public IsoGrid(int width, int height, float tileWidth = 1f, float tileHeight = 0.5f)
    {
        Width = width;
        Height = height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        occupied = new bool[width, height];
    }

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

    public bool IsOccupied(Vector2Int c) => InBounds(c) && occupied[c.x, c.y];

    public bool CanPlace(Vector2Int c) => InBounds(c) && !occupied[c.x, c.y];

    /// <summary>Marks the cell as occupied. Returns false (and changes nothing) if illegal.</summary>
    public bool TryPlace(Vector2Int c)
    {
        if (!CanPlace(c)) return false;
        occupied[c.x, c.y] = true;
        return true;
    }

    /// <summary>World position of the center of a cell.</summary>
    public Vector3 CellToWorld(Vector2Int c)
    {
        return new Vector3(
            (c.x - c.y) * TileWidth * 0.5f,
            (c.x + c.y) * TileHeight * 0.5f,
            0f);
    }

    /// <summary>Cell under a world position. May be out of bounds; check with InBounds.</summary>
    public Vector2Int WorldToCell(Vector3 world)
    {
        float a = world.x / (TileWidth * 0.5f);   // = x - y
        float b = world.y / (TileHeight * 0.5f);  // = x + y
        float fx = (a + b) * 0.5f;
        float fy = (b - a) * 0.5f;
        // In grid space each cell is a unit square centered on integer coords.
        return new Vector2Int(Mathf.FloorToInt(fx + 0.5f), Mathf.FloorToInt(fy + 0.5f));
    }

    /// <summary>World-space bounds of the whole map (min, max).</summary>
    public void GetWorldBounds(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(CellToWorld(new Vector2Int(0, Height - 1)).x - TileWidth * 0.5f,
                          CellToWorld(new Vector2Int(0, 0)).y - TileHeight * 0.5f);
        max = new Vector2(CellToWorld(new Vector2Int(Width - 1, 0)).x + TileWidth * 0.5f,
                          CellToWorld(new Vector2Int(Width - 1, Height - 1)).y + TileHeight * 0.5f);
    }
}
