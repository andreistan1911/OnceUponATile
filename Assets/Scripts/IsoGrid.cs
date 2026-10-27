using UnityEngine;

/// <summary>
/// Pure grid logic: which piece (if any) is on each cell + conversion between grid cells and world positions.
/// No MonoBehaviour, no visuals. Standard 2:1 isometric layout.
/// Cell (0,0) is the bottom corner of the diamond; +x goes up-right, +y goes up-left.
/// </summary>
public class IsoGrid
{
    public readonly int Width;
    public readonly int Height;
    public readonly float TileWidth;   // world width of one diamond
    public readonly float TileHeight;  // world height of one diamond (usually half of width)

    readonly PieceType?[,] pieces;     // null = free cell

    public IsoGrid(int width, int height, float tileWidth = 1f, float tileHeight = 0.5f)
    {
        Width = width;
        Height = height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        pieces = new PieceType?[width, height];
    }

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

    public bool IsOccupied(Vector2Int c) => InBounds(c) && pieces[c.x, c.y].HasValue;

    /// <summary>True if the cell exists and is free. Says nothing about placement rules.</summary>
    public bool CanPlace(Vector2Int c) => InBounds(c) && !pieces[c.x, c.y].HasValue;

    /// <summary>The piece on a cell, or null if the cell is free or out of bounds.</summary>
    public PieceType? GetPiece(Vector2Int c) => InBounds(c) ? pieces[c.x, c.y] : null;

    /// <summary>Puts a piece on the cell. Returns false (and changes nothing) if the cell is not free.</summary>
    public bool TryPlace(Vector2Int c, PieceType type)
    {
        if (!CanPlace(c)) return false;
        pieces[c.x, c.y] = type;
        return true;
    }

    /// <summary>True if one of the 4 side neighbours of the cell holds a piece of the given type.</summary>
    public bool HasNeighbor(Vector2Int c, PieceType type)
    {
        return IsType(c.x + 1, c.y, type) || IsType(c.x - 1, c.y, type)
            || IsType(c.x, c.y + 1, type) || IsType(c.x, c.y - 1, type);
    }

    /// <summary>Empties the whole grid.</summary>
    public void Clear()
    {
        System.Array.Clear(pieces, 0, pieces.Length);
    }

    bool IsType(int x, int y, PieceType type)
    {
        return x >= 0 && y >= 0 && x < Width && y < Height && pieces[x, y] == type;
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
