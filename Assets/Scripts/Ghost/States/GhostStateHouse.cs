using UnityEngine;

/// <summary>
/// ゴーストハウス内待機ステート。
/// ゴーストは停止し、退出タイミングは B_GhostHouseManager が制御する。
/// </summary>
public sealed class GhostStateHouse : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.House;

    public void Enter(BaseGhost host) { }
    public void Exit (BaseGhost host) { }

    /// <summary>ハウス内は停止するため 0 を返します（FixedUpdate でガード済み）。</summary>
    public float GetSpeedRate(BaseGhost host) => 0f;

    /// <summary>ハウス内では自動遷移なし。</summary>
    public void OnTileReached(BaseGhost host) { }

    /// <summary>ハウス内では呼ばれない想定。zero を返します。</summary>
    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
        => Vector2Int.zero;
}
