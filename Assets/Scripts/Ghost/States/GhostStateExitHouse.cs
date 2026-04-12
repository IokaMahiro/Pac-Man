using UnityEngine;

/// <summary>
/// ゴーストハウス退出ステート。
/// BFS でハウス出口タイルへ最短経路を移動し、到達後 Scatter モードへ自動遷移する。
/// </summary>
/// <remarks>
/// 経路探索は GhostBfsHelper（BFS）を使用する。
/// グリーディ法はハウス内部の狭い経路で局所解に陥りゴーストが
/// ハウス内でグルグル回るバグを引き起こすため採用しない。
/// </remarks>
public sealed class GhostStateExitHouse : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.ExitHouse;

    /// <summary>退出開始時、進行方向を zero にリセットして経路再計算を促します。</summary>
    public void Enter(BaseGhost host) => host.InternalCurrentDir = Vector2Int.zero;

    public void Exit(BaseGhost host) { }

    public float GetSpeedRate(BaseGhost host)
        => host.InternalIsInTunnel ? host.InternalTunnelSpeedRate : host.InternalNormalSpeedRate;

    /// <summary>
    /// BFS でハウス出口への最短経路の初手方向を返します。
    /// BFS が経路を発見できない場合はグリーディ法にフォールバックします。
    /// </summary>
    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
    {
        Vector2Int target    = host.InternalHouseEntrance;
        Vector2Int firstStep = GhostBfsHelper.FirstStep(host, fromTile, target);

        // BFS で経路が見つからなければグリーディ法にフォールバック
        return firstStep != Vector2Int.zero
            ? firstStep
            : host.InternalPathfindBest(fromTile, incomingDir, target, allowUTurn: true);
    }

    /// <summary>出口タイル到達で Scatter へ遷移します。</summary>
    public void OnTileReached(BaseGhost host)
    {
        if (host.CurrentTile == host.InternalHouseEntrance)
            host.InternalTransitionToState(BaseGhost.GhostMode.Scatter);
    }
}
