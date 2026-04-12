using UnityEngine;

/// <summary>
/// スキャッター（縄張り巡回）ステート。
/// 各ゴーストの担当コーナー（GetScatterTarget）を目指す。
/// Blinky の Elroy2 は GetScatterTarget() のオーバーライドでチェイスターゲットに差し替えられる。
/// </summary>
public sealed class GhostStateScatter : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.Scatter;

    public void Enter(BaseGhost host) { }
    public void Exit (BaseGhost host) { }

    public float GetSpeedRate(BaseGhost host)
        => host.InternalIsInTunnel ? host.InternalTunnelSpeedRate : host.InternalNormalSpeedRate;

    /// <summary>
    /// スキャッターターゲットへの最短方向を返します。
    /// U ターン禁止・赤ゾーン上方向禁止を適用します。
    /// </summary>
    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
        => host.InternalPathfindBest(fromTile, incomingDir, host.InternalScatterTarget, allowUTurn: false);

    public void OnTileReached(BaseGhost host) { }
}
