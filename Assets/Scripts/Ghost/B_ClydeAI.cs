using UnityEngine;

/// <summary>
/// クライド（オレンジ）の AI。距離によって行動が変わる「臆病者」。
/// パックマンまでの距離が 8 タイル以上なら直接追跡。
/// 8 タイル未満に近づいたら自分のスキャッターコーナー（左下）へ逃げる。
/// この繰り返しにより、パックマンの近くを行ったり来たりする独特の動きをする。
/// </summary>
public class B_ClydeAI : BaseGhost
{
    #region 定義

    // 逃げ始める距離しきい値（タイル数）
    private const float FleeDistance = 8f;

    #endregion

    #region 非公開メソッド

    protected override void OnAwake()
    {
        // スキャッターターゲット: 迷路左下の到達不能タイル
        _scatterTarget = new Vector2Int(0, 29);
    }

    /// <summary>
    /// パックマンとの距離が 8 タイル以上なら直接追跡、
    /// 未満ならスキャッターターゲット（左下）へ逃げます。
    /// </summary>
    protected override Vector2Int GetChaseTarget()
    {
        if (_pacManMover == null) return _scatterTarget;

        Vector2Int pacTile = _pacManMover.CurrentTile;

        float dx   = CurrentTile.x - pacTile.x;
        float dz   = CurrentTile.y - pacTile.y;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);

        return dist >= FleeDistance ? pacTile : _scatterTarget;
    }

    #endregion
}
