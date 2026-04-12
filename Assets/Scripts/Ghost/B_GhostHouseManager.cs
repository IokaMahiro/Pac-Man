using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゴーストのハウス内待機・退出タイミングを管理するビヘイビア。
/// </summary>
/// <remarks>
/// 退出制御の 2 モード:
///
///   【個別カウンター】通常時
///     ハウス内で最優先のゴーストを「アクティブゴースト」とし、
///     パックマンがドットを食べるたびにそのゴースト専用のカウンターを加算。
///     しきい値に達したら退出させ、次のゴーストがアクティブになる。
///     優先順: Pinky(0個) → Inky(30個) → Clyde(60個)  ※ Level 1
///
///   【グローバルカウンター】残機消失後
///     OnPacManDied() 呼び出し時点でハウスにいるゴーストを記録し、
///     以降のドット取得数が 7 / 17 / 32 個に達した順に退出させる。
///
///   【無活動タイマー】どちらのモードでも有効
///     パックマンがドットを食べない状態が一定時間続くと、
///     ハウス内の最優先ゴーストを強制退出させカウンターをリセットする。
///     Level 1-4: 4 秒 / Level 5+: 3 秒
/// </remarks>
public class B_GhostHouseManager : MonoBehaviour
{
    #region 定義

    [SerializeField] private B_PacManMover _pacManMover;
    [SerializeField] private BaseGhost     _pinky;
    [SerializeField] private BaseGhost     _inky;
    [SerializeField] private BaseGhost     _clyde;

    [Header("個別ドットカウンターしきい値（インデックス: 0=Pinky / 1=Inky / 2=Clyde）")]
    [Tooltip("Level 1: {0, 30, 60} / Level 2: {0, 0, 50} / Level 3+: {0, 0, 0}")]
    [SerializeField] private int[] _personalThresholds = { 0, 30, 60 };

    [Header("グローバルカウンターしきい値（残機消失後: 1番目/2番目/3番目に退出するゴーストの累計ドット数）")]
    [SerializeField] private int[] _globalThresholds = { 7, 17, 32 };

    [Header("無活動タイムアウト（秒）")]
    [Tooltip("Level 1-4: 4 秒 / Level 5+: 3 秒")]
    [SerializeField] private float _inactivityTimeout = 4f;

    // Pinky=0, Inky=1, Clyde=2 の順で管理
    private BaseGhost[] _houseGhosts;

    // ── 個別カウンター ──────────────────────────
    // アクティブゴースト: ハウス内で最も優先度が高い（インデックスが最小の）ゴースト
    private int _activeGhostIndex;   // 現在カウントしているゴーストのインデックス
    private int _personalDotCounter; // そのゴースト専用のドット取得カウント

    // ── グローバルカウンター ────────────────────
    private bool       _useGlobalCounter;
    private int        _globalDotCounter;
    // 残機消失時点でハウスにいたゴーストのインデックスを優先順に格納
    private List<int>  _globalPendingIndices;

    // ── 無活動タイマー ──────────────────────────
    private float _inactivityTimer;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// ゴーストハウス管理をリセットし退出シーケンスを開始します。
    /// レベル開始時に B_GameManager から呼んでください。
    /// </summary>
    public void Initialize()
    {
        _houseGhosts         = new BaseGhost[] { _pinky, _inky, _clyde };
        _personalDotCounter  = 0;
        _useGlobalCounter    = false;
        _globalDotCounter    = 0;
        _globalPendingIndices = new List<int>(3);
        _inactivityTimer     = _inactivityTimeout;

        // しきい値 0 のゴーストを先頭から順に即座に退出させ、
        // 最初に待機が必要なゴーストのインデックスを _activeGhostIndex に設定する
        ReleaseImmediateGhostsFrom(startIndex: 0);
    }

    /// <summary>
    /// 残機消失時に呼びます。個別カウンターをグローバルカウンターに切り替えます。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void OnPacManDied()
    {
        _useGlobalCounter = true;
        _globalDotCounter = 0;
        _inactivityTimer  = _inactivityTimeout;

        // 残機消失時点でハウスにいるゴーストを優先順でキューに積む
        _globalPendingIndices = new List<int>(3);
        for (int i = 0; i < _houseGhosts.Length; i++)
        {
            if (IsInHouse(i)) _globalPendingIndices.Add(i);
        }
    }

    /// <summary>
    /// レベルに応じた個別カウンターしきい値を更新します。
    /// B_GameManager からレベルアップ時に呼んでください。
    /// </summary>
    /// <param name="thresholds">Pinky・Inky・Clyde の順のしきい値配列（要素数 3）</param>
    /// <param name="inactivityTimeout">無活動タイムアウト秒数</param>
    public void SetLevelParameters(int[] thresholds, float inactivityTimeout)
    {
        if (thresholds != null && thresholds.Length == 3)
            _personalThresholds = thresholds;
        if (inactivityTimeout > 0f)
            _inactivityTimeout = inactivityTimeout;
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_pacManMover == null) Debug.LogError("[B_GhostHouseManager] _pacManMover が未設定です。");
        if (_pinky  == null)     Debug.LogError("[B_GhostHouseManager] _pinky が未設定です。");
        if (_inky   == null)     Debug.LogError("[B_GhostHouseManager] _inky が未設定です。");
        if (_clyde  == null)     Debug.LogError("[B_GhostHouseManager] _clyde が未設定です。");
    }

    private void Start()
    {
        if (_pacManMover != null)
            _pacManMover.OnDotEaten += HandleDotEaten;
    }

    private void OnDestroy()
    {
        if (_pacManMover != null)
            _pacManMover.OnDotEaten -= HandleDotEaten;
    }

    private void Update()
    {
        // Initialize() 呼び出し前は _houseGhosts が null → スキップ
        if (_houseGhosts == null) return;

        // ハウスにゴーストがいなければタイマー不要
        if (GetFirstHouseIndex() < 0) return;

        _inactivityTimer -= Time.deltaTime;
        if (_inactivityTimer <= 0f)
        {
            _inactivityTimer = _inactivityTimeout;
            ForceReleaseFirst();
        }
    }

    // ─────────────────────────────────────────────────
    //  ドット取得ハンドラ
    // ─────────────────────────────────────────────────

    private void HandleDotEaten(bool isEnergizer)
    {
        // Initialize() 呼び出し前は _houseGhosts が null → スキップ
        if (_houseGhosts == null) return;

        _inactivityTimer = _inactivityTimeout; // タイマーをリセット

        if (_useGlobalCounter)
            HandleGlobalCounter();
        else
            HandlePersonalCounter();
    }

    /// <summary>
    /// 個別カウンターモードの処理。
    /// ハウス内の最優先ゴーストのカウンターを加算し、しきい値到達で退出させます。
    /// </summary>
    private void HandlePersonalCounter()
    {
        int idx = GetFirstHouseIndex();
        if (idx < 0) return;

        // アクティブゴーストが切り替わった場合（前のゴーストが退出済みなど）はカウンターをリセット
        if (idx != _activeGhostIndex)
        {
            _activeGhostIndex   = idx;
            _personalDotCounter = 0;
        }

        _personalDotCounter++;
        if (_personalDotCounter >= _personalThresholds[idx])
        {
            ReleaseGhost(idx);
        }
    }

    /// <summary>
    /// グローバルカウンターモードの処理。
    /// 累計ドット数が各しきい値（7/17/32）に達したゴーストを退出させます。
    /// </summary>
    private void HandleGlobalCounter()
    {
        _globalDotCounter++;

        // ペンディングキューの順番に対応するしきい値をチェック
        for (int qi = 0; qi < _globalPendingIndices.Count; qi++)
        {
            if (_globalDotCounter != _globalThresholds[qi]) continue;

            int ghostIdx = _globalPendingIndices[qi];
            if (IsInHouse(ghostIdx))
                _houseGhosts[ghostIdx].SetMode(BaseGhost.GhostMode.ExitHouse);
        }
    }

    // ─────────────────────────────────────────────────
    //  退出処理ヘルパー
    // ─────────────────────────────────────────────────

    /// <summary>
    /// 指定インデックスのゴーストを退出させ、次のゴーストのカウントを開始します。
    /// しきい値 0 のゴーストは連続的に即座退出させます。
    /// </summary>
    private void ReleaseGhost(int index)
    {
        if (!IsInHouse(index)) return;

        _houseGhosts[index].SetMode(BaseGhost.GhostMode.ExitHouse);
        _personalDotCounter = 0;

        // 次のゴーストへ（しきい値 0 は連続リリース）
        ReleaseImmediateGhostsFrom(index + 1);
    }

    /// <summary>
    /// startIndex 以降で最初にハウスにいるゴーストを強制退出させます（タイムアウト用）。
    /// </summary>
    private void ForceReleaseFirst()
    {
        _personalDotCounter = 0;
        _globalDotCounter   = 0;

        int idx = GetFirstHouseIndex();
        if (idx < 0) return;

        _houseGhosts[idx].SetMode(BaseGhost.GhostMode.ExitHouse);

        // グローバルカウンターのペンディングからも除去
        _globalPendingIndices.Remove(idx);

        // 次のゴーストへ（個別カウンターモードのみ即座リリースを確認）
        if (!_useGlobalCounter)
            ReleaseImmediateGhostsFrom(idx + 1);
    }

    /// <summary>
    /// startIndex 以降のゴーストについて、しきい値 0 のものを連続退出させます。
    /// 最初にしきい値 > 0 のゴーストが見つかった時点で _activeGhostIndex を更新して停止します。
    /// </summary>
    private void ReleaseImmediateGhostsFrom(int startIndex)
    {
        for (int i = startIndex; i < _houseGhosts.Length; i++)
        {
            if (!IsInHouse(i))
            {
                // すでにハウスの外にいる（ExitHouse / Scatter などに移行済み）
                continue;
            }

            if (_personalThresholds[i] == 0)
            {
                // しきい値 0 → 即座退出
                _houseGhosts[i].SetMode(BaseGhost.GhostMode.ExitHouse);
            }
            else
            {
                // ここで待機が必要なゴーストが確定
                _activeGhostIndex   = i;
                _personalDotCounter = 0;
                return;
            }
        }

        // 全員退出完了
        _activeGhostIndex = _houseGhosts.Length;
    }

    // ─────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────

    /// <summary>ハウス内の最優先ゴーストのインデックスを返します。いなければ -1 を返します。</summary>
    private int GetFirstHouseIndex()
    {
        for (int i = 0; i < _houseGhosts.Length; i++)
        {
            if (IsInHouse(i)) return i;
        }
        return -1;
    }

    /// <summary>指定インデックスのゴーストが現在ハウス内にいるかを返します。</summary>
    private bool IsInHouse(int index)
    {
        if (index < 0 || index >= _houseGhosts.Length) return false;
        if (_houseGhosts[index] == null) return false;
        return _houseGhosts[index].CurrentMode == BaseGhost.GhostMode.House;
    }

    #endregion
}
