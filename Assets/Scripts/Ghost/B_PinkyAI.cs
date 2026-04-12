using UnityEngine;

/// <summary>
/// ピンキー（ピンク）の AI。パックマンの進行方向 4 タイル先を狙い先回りする。
/// 原作のオーバーフローバグを再現: 上向き移動中は「4 上 + 4 左」がターゲットになる。
/// </summary>
public class B_PinkyAI : BaseGhost
{
    #region 定義

    // 先読みタイル数
    private const int LookAheadTiles = 4;

    // 上向きバグのオフセット（原作 ROM のオーバーフロー再現）
    private static readonly Vector2Int UpBugOffset = new Vector2Int(-4, 0);

    // 上方向ベクトル（タイル空間）
    private static readonly Vector2Int DirUp = new Vector2Int(0, -1);

    #endregion

    #region 非公開メソッド

    protected override void OnAwake()
    {
        // スキャッターターゲット: 迷路左上の到達不能タイル
        _scatterTarget = new Vector2Int(2, -3);
    }

    /// <summary>
    /// パックマンの進行方向 4 タイル先をターゲットにします。
    /// 上向き時は原作バグを再現して 4 上 + 4 左 になります。
    /// </summary>
    protected override Vector2Int GetChaseTarget()
    {
        if (_pacManMover == null) return _scatterTarget;

        Vector2Int pacTile = _pacManMover.CurrentTile;
        Vector2Int pacDir  = _pacManMover.CurrentDir;

        // 4 タイル先
        Vector2Int target = pacTile + pacDir * LookAheadTiles;

        // 上向きバグ再現: 上を向いているとき、さらに左へ 4 タイルずれる
        if (pacDir == DirUp)
            target += UpBugOffset;

        return target;
    }

    #endregion
}
