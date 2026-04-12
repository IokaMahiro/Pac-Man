using UnityEngine;

/// <summary>
/// デッド（目玉帰還）ステート。
/// 高速でハウス内部中央タイルへ戻り、到達後 ExitHouse モードへ自動遷移する。
/// ExitHouse モードが通常の退出シーケンスを担うため、Blinky を含む全ゴーストが
/// B_GhostHouseManager と無関係に自律復帰できる。
/// </summary>
/// <remarks>
/// 経路探索は GhostBfsHelper（BFS）を使用する。
/// グリーディ法はゴーストハウス周辺で局所解に陥るため採用しない。
///
/// ナビゲーション先: InternalHouseCenter（ハウス内部中央タイル、デフォルト (13,13)）
///   InternalHouseEntrance（ドア直上 (13,11)）ではなく内部まで進入することで
///   Blinky のような B_GhostHouseManager 管理外ゴーストも正しく復帰できる。
/// </remarks>
public sealed class GhostStateDead : IGhostState
{
    public BaseGhost.GhostMode Mode => BaseGhost.GhostMode.Dead;

    public void Enter(BaseGhost host) { }
    public void Exit (BaseGhost host) { }

    public float GetSpeedRate(BaseGhost host) => host.InternalDeadRate;

    /// <summary>
    /// BFS でハウス内部中央タイルへの最短経路の初手方向を返します。
    /// BFS が経路を発見できない場合はグリーディ法にフォールバックします。
    /// </summary>
    public Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir)
    {
        Vector2Int target    = host.InternalHouseCenter;
        Vector2Int firstStep = GhostBfsHelper.FirstStep(host, fromTile, target);

        // BFS で経路が見つからなければグリーディ法にフォールバック
        return firstStep != Vector2Int.zero
            ? firstStep
            : host.InternalPathfindBest(fromTile, incomingDir, target, allowUTurn: true);
    }

    /// <summary>
    /// ハウス内部中央タイル到達で ExitHouse へ遷移します。
    /// ExitHouse ステートが通常の退出シーケンスを実行するため、
    /// Blinky を含む全ゴーストが追加の管理なしに自律復帰できます。
    /// </summary>
    public void OnTileReached(BaseGhost host)
    {
        if (host.CurrentTile == host.InternalHouseCenter)
            host.InternalTransitionToState(BaseGhost.GhostMode.ExitHouse);
    }
}
