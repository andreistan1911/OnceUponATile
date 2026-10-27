using System.Collections.Generic;
using UnityEngine;

public enum PieceType { House, Road, Park }

/// <summary>
/// Pure queue logic, no visuals. The queue holds `capacity` pieces, but only the first
/// `activeCount` can be taken. Taking one removes it, the rest shift left, and a new random
/// piece joins the end:
///   [a b c] d e f g h i j   --take b-->   [a c d] e f g h i j k
/// </summary>
public class PieceQueue
{
    public readonly int Capacity;
    public readonly int ActiveCount;

    readonly List<PieceType> pieces = new List<PieceType>();

    public PieceQueue(int capacity, int activeCount)
    {
        Capacity = capacity;
        ActiveCount = activeCount;
        while (pieces.Count < capacity) pieces.Add(RandomPiece());
    }

    /// <summary>Piece at a queue position (0 = front). Works for any position, active or not.</summary>
    public PieceType Peek(int index) => pieces[index];

    /// <summary>Removes and returns an active piece (index &lt; ActiveCount) and refills the queue.</summary>
    public PieceType Take(int index)
    {
        if (index < 0 || index >= ActiveCount)
            throw new System.ArgumentOutOfRangeException(nameof(index), "Only the first ActiveCount pieces can be taken.");

        PieceType piece = pieces[index];
        pieces.RemoveAt(index);
        pieces.Add(RandomPiece());
        return piece;
    }

    static PieceType RandomPiece()
    {
        int count = System.Enum.GetValues(typeof(PieceType)).Length;
        return (PieceType)Random.Range(0, count);
    }
}
