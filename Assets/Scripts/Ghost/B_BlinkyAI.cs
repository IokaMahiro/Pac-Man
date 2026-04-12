using UnityEngine;

/// <summary>
/// ブリンキー（赤）の AI。パックマンの現在タイルを直接追跡する。
/// 残ドット数が少なくなると「クルーズ・エルロイ」として加速し、
/// Elroy2 ではスキャッターモード中もチェイスターゲットを使用する。
/// </summary>
public class B_BlinkyAI : BaseGhost
{
    #region 定義

    [Header("エルロイ速度倍率（Level 1）")]
    [SerializeField] private float _elroy1SpeedRate = 0.80f; // 残 20 個以下
    [SerializeField] private float _elroy2SpeedRate = 0.85f; // 残 10 個以下

    [SerializeField] private B_DotManager _dotManager;

    // エルロイ状態フラグ
    private bool _elroy1Active;
    private bool _elroy2Active;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// エルロイ状態をリセットします。
    /// レベル開始時に B_GameManager から呼んでください。
    /// </summary>
    public void ResetElroy()
    {
        _elroy1Active = false;
        _elroy2Active = false;
    }

    #endregion

    #region 非公開メソッド

    protected override void OnAwake()
    {
        // スキャッターターゲット: 迷路右上の到達不能タイル
        _scatterTarget = new Vector2Int(25, -3);
    }

    private void Start()
    {
        if (_dotManager == null)
        {
            Debug.LogError("[B_BlinkyAI] _dotManager がアタッチされていません。");
            return;
        }
        _dotManager.OnElroyThreshold += HandleElroyThreshold;
    }

    private void OnDestroy()
    {
        if (_dotManager != null)
            _dotManager.OnElroyThreshold -= HandleElroyThreshold;
    }

    /// <summary>パックマンの現在タイルをそのまま返します。</summary>
    protected override Vector2Int GetChaseTarget()
    {
        if (_pacManMover == null) return _scatterTarget;
        return _pacManMover.CurrentTile;
    }

    /// <summary>
    /// Elroy2 ではスキャッターモード中もチェイスターゲットを返します。
    /// スキャッター無効化によりパックマンを縄張りに逃げても追跡し続けます。
    /// </summary>
    protected override Vector2Int GetScatterTarget()
    {
        return _elroy2Active ? GetChaseTarget() : base.GetScatterTarget();
    }

    /// <summary>エルロイ状態に応じた速度倍率を返します。</summary>
    protected override float GetNormalSpeedRate()
    {
        if (_elroy2Active) return _elroy2SpeedRate;
        if (_elroy1Active) return _elroy1SpeedRate;
        return base.GetNormalSpeedRate();
    }

    /// <summary>B_DotManager.OnElroyThreshold の購読ハンドラ。</summary>
    private void HandleElroyThreshold(int level)
    {
        if (level == 1) _elroy1Active = true;
        if (level == 2) _elroy2Active = true;
    }

    #endregion
}
