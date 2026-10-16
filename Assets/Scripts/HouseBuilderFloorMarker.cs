using UnityEngine;

/// <summary>Floor metadata assigned by the Fantasy Kingdom building editor.</summary>
[DisallowMultipleComponent]
public sealed class HouseBuilderFloorMarker : MonoBehaviour
{
    public int FloorIndex;
    public float BaseHeight;
    public float PlacementOffset;

    public float PlacementHeight => BaseHeight + PlacementOffset;
}
