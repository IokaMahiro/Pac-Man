using UnityEngine;

/// <summary>
/// チェイス（追跡）ステート。
/// 各ゴースト固有の AI ターゲット（GetChaseTarget の abstract 実装）を追う。
/// </summary>
public sealed class GhostStateChase : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.Chase;

    public void Enter(BaseGhost host) { }
    public void Exit (BaseGhost host) { }

    public float GetSpeedRate(BaseGhost host)
        => host.InternalIsInTunnel ? host.InternalTunnelSpeedRate : host.InternalNormalSpeedRate;

    /// <summary>
    /// チェイスターゲットへの最短方向を返します。
    /// GetChaseTarget() の abstract 呼び出しにより各サブクラスの固有 AI が機能します。
    /// U ターン禁止・赤ゾーン上方向禁止を適用します。
    /// </summary>
    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
        => host.InternalPathfindBest(fromTile, incomingDir, host.InternalChaseTarget, allowUTurn: false);

    public void OnTileReached(BaseGhost host) { }
}
