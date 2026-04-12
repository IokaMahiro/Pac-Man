using UnityEngine;

/// <summary>
/// インキー（水色）の AI。最も複雑な挟み撃ちアルゴリズム。
/// 「パックマン進行方向 2 タイル先（中間点）」と「ブリンキーの位置」を使い、
/// ブリンキーから中間点へのベクトルを 2 倍延長した先をターゲットにする。
/// ブリンキーとパックマンが接近しているほど、インキーもパックマンに近づく。
/// </summary>
public class B_InkyAI : BaseGhost
{
    #region 定義

    [SerializeField] private B_BlinkyAI _blinky;

    // 中間点の先読みタイル数
    private const int PivotTiles = 2;

    // 上向きバグのオフセット（原作 ROM のオーバーフロー再現）
    private static readonly Vector2Int UpBugOffset = new Vector2Int(-2, 0);

    // 上方向ベクトル（タイル空間）
    private static readonly Vector2Int DirUp = new Vector2Int(0, -1);

    #endregion

    #region 非公開メソッド

    protected override void OnAwake()
    {
        if (_blinky == null)
            Debug.LogError("[B_InkyAI] _blinky がアタッチされていません。");
    }

    /// <summary>
    /// ブリンキーとパックマンを使った挟み撃ちターゲットを計算します。
    ///
    /// 計算手順:
    ///   1. パックマンの進行方向 2 タイル先を中間点（pivot）とする
    ///      （上向き時は原作バグで 2 上 + 2 左 になる）
    ///   2. ブリンキーの現在タイルから中間点へのベクトルを求める
    ///   3. そのベクトルを 2 倍延長した先をターゲットとする
    /// </summary>
    protected override Vector2Int GetChaseTarget()
    {
        if (_pacManMover == null || _blinky == null) return _scatterTarget;

        Vector2Int pacDir  = _pacManMover.CurrentDir;

        // ① 中間点: パックマン進行方向 2 タイル先
        Vector2Int pivot = _pacManMover.CurrentTile + pacDir * PivotTiles;

        // 上向きバグ再現: 上を向いているとき、さらに左へ 2 タイルずれる
        if (pacDir == DirUp)
            pivot += UpBugOffset;

        // ② ブリンキーから中間点へのベクトルを 2 倍延長
        Vector2Int offset = pivot - _blinky.CurrentTile;
        return pivot + offset; // = blinkyTile + offset * 2
    }

    #endregion
}
