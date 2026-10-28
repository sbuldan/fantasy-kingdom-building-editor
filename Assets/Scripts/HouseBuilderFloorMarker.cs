using UnityEngine;

public enum HouseBuilderPieceKind { Unspecified, Floor, Wall, Pillar }

/// <summary>Floor metadata assigned by the Fantasy Kingdom building editor.</summary>
[DisallowMultipleComponent]
public sealed class HouseBuilderFloorMarker : MonoBehaviour
{
    public int FloorIndex;
    public float BaseHeight;
    public float PlacementOffset;
    public HouseBuilderPieceKind PieceKind;

    public float PlacementHeight => BaseHeight + PlacementOffset;
}
