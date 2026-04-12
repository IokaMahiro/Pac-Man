using UnityEngine;

/// <summary>
/// フライテンド（青・逃亡）ステート。
/// U ターンを除く通行可能方向からランダムに次の方向を選択する。
/// </summary>
public sealed class GhostStateFrightened : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.Frightened;

    public void Enter(BaseGhost host) { }
    public void Exit (BaseGhost host) { }

    public float GetSpeedRate(BaseGhost host) => host.InternalFrightenedRate;

    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
        => host.InternalPathfindFrightened(fromTile, incomingDir);

    public void OnTileReached(BaseGhost host) { }
}
