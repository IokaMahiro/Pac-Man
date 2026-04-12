using UnityEngine;

/// <summary>
/// ゴーストの各行動モードが実装するステートインターフェース。
/// </summary>
/// <remarks>
/// 実装クラスはフィールドを持たず、引数 host（BaseGhost）を通じて
/// 状態の読み書きを行う。そのため全ゴーストで単一インスタンスを共有できる。
/// </remarks>
public interface IGhostState
{
    /// <summary>このステートに対応するモード識別子。</summary>
    BaseGhost.GhostMode Mode { get; }

    /// <summary>ステート開始時の初期化。</summary>
    void Enter(BaseGhost host);

    /// <summary>ステート終了時のクリーンアップ。</summary>
    void Exit(BaseGhost host);

    /// <summary>現フレームに使用する速度倍率を返します。</summary>
    float GetSpeedRate(BaseGhost host);

    /// <summary>
    /// タイル中心到達時に呼ばれます。
    /// ExitHouse → Scatter、Dead → House などの自動モード遷移をここで実装します。
    /// </summary>
    void OnTileReached(BaseGhost host);

    /// <summary>
    /// 次の移動方向を決定して返します。
    /// ターゲット追従・ランダム移動など、ステート固有のアルゴリズムで実装します。
    /// </summary>
    /// <param name="host">このステートを所有するゴースト。</param>
    /// <param name="fromTile">現在タイル座標。</param>
    /// <param name="incomingDir">到達時の進入方向（U ターン禁止の基準）。</param>
    Vector2Int DecideNextDirection(BaseGhost host, Vector2Int fromTile, Vector2Int incomingDir);
}
