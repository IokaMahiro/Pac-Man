using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ゲーム全体の状態機械を管理するビヘイビア。
/// スキャッター / チェイス サイクル・フライテンドモード・衝突判定・残機 / レベル管理を担います。
/// </summary>
/// <remarks>
/// ゲームフロー:
///   Start → InitializeLevel → ReadyCoroutine → Playing
///   Playing → 衝突      → DeathCoroutine    → (残機あり) InitializeLevel → Playing
///                                            → (残機なし) GameOver
///   Playing → 全ドット  → LevelClearCoroutine → _currentLevel++ → InitializeLevel → Playing
///
/// スキャッター / チェイス サイクル:
///   _scatterChasePhases 配列を順番に消化し、フライテンド中はタイマーを一時停止します。
///   Duration ≤ 0 の最終フェーズは無限 Chase として扱います。
///
/// グローバルドットカウンター（死亡後）に関する注意:
///   B_GhostHouseManager.Initialize() 内で閾値 0 のゴースト（Pinky）が
///   即座にハウスを退出するため、その後 OnPacManDied() を呼ぶと Pinky が
///   グローバルカウンターの pending リストに含まれません。
///   結果として Inky / Clyde の退出ドット数がオリジナル (17/32) より少なく (7/17) なります。
///   プレイアビリティへの影響は軽微であり、将来の改善余地として残します。
/// </remarks>
public class B_GameManager : MonoBehaviour
{
    #region 定義

    /// <summary>ゲームの進行状態</summary>
    public enum GameState
    {
        Ready,       // READY! 表示中（開始 / 死亡後リスポーン待ち）
        Playing,     // プレイ中
        PacManDead,  // 死亡演出中
        LevelClear,  // レベルクリア演出中
        GameOver,    // ゲームオーバー
    }

    /// <summary>スキャッター / チェイス フェーズ 1 段分の定義</summary>
    [System.Serializable]
    private struct ScatterChasePhase
    {
        public BaseGhost.GhostMode Mode;
        [Tooltip("フェーズ継続秒数。0 以下で無限（最終 Chase に使用）。")]
        public float Duration;
    }

    [Header("参照")]
    [SerializeField] private B_MazeGenerator     _mazeGenerator;
    [SerializeField] private B_PacManMover        _pacManMover;
    [SerializeField] private B_DotManager         _dotManager;
    [SerializeField] private B_GhostHouseManager  _ghostHouseManager;
    [SerializeField] private B_BlinkyAI           _blinky;
    [SerializeField] private B_PinkyAI            _pinky;
    [SerializeField] private B_InkyAI             _inky;
    [SerializeField] private B_ClydeAI            _clyde;

    [Header("ゲームパラメータ")]
    [SerializeField] private int   _initialLives       = 3;

    [Tooltip("フライテンドモードの継続秒数（Level 1）")]
    [SerializeField] private float _frightenedDuration  = 6f;

    [Tooltip("死亡演出の待機秒数")]
    [SerializeField] private float _deathPauseDuration  = 1.0f;

    [Tooltip("レベルクリア演出の待機秒数")]
    [SerializeField] private float _levelClearPause     = 2.0f;

    [Tooltip("READY! 表示中の待機秒数")]
    [SerializeField] private float _readyDuration       = 2.0f;

    [Tooltip("通常時のパックマン速度倍率（Level 1）")]
    [SerializeField] private float _pacManNormalRate    = 0.80f;

    [Tooltip("フライテンド中のパックマン速度倍率（Level 1）")]
    [SerializeField] private float _pacManFrightenedRate = 0.90f;

    [Header("スキャッター / チェイス フェーズ（Level 1）")]
    [SerializeField] private ScatterChasePhase[] _scatterChasePhases = new ScatterChasePhase[]
    {
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Scatter, Duration =  7f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Chase,   Duration = 20f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Scatter, Duration =  7f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Chase,   Duration = 20f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Scatter, Duration =  5f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Chase,   Duration = 20f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Scatter, Duration =  5f },
        new ScatterChasePhase { Mode = BaseGhost.GhostMode.Chase,   Duration = -1f }, // 無限
    };

    // ── ゲーム状態 ──────────────────────────────────
    private GameState _gameState;
    private int       _currentLevel;
    private int       _currentLives;
    private bool      _isFirstLevel = true; // 初回は MazeGenerator.Awake() で生成済みのため再生成しない

    // ── フライテンドタイマー ─────────────────────────
    private bool  _frightened;
    private float _frightenedTimer;
    private int   _ghostEatCount; // このエナジャイザー 1 個での連続食べカウント（得点倍増用）

    // ── スキャッター / チェイス サイクル ─────────────
    private int   _phaseIndex;
    private float _phaseTimer;
    private bool  _scatterChasePaused; // フライテンド中はタイマーを停止

    // ── ゴースト配列（衝突判定・一括モード切替に使用）─
    private BaseGhost[] _allGhosts;

    // ── イベント ────────────────────────────────────

    /// <summary>残機が変わったときに発火。引数: 新しい残機数。</summary>
    public event Action<int>       OnLivesChanged;

    /// <summary>レベルが変わったときに発火。引数: 新しいレベル番号（1 始まり）。</summary>
    public event Action<int>       OnLevelChanged;

    /// <summary>ゲーム状態が変わったときに発火。</summary>
    public event Action<GameState> OnGameStateChanged;

    /// <summary>ゴーストを食べたときに発火。引数: 得点 (200 / 400 / 800 / 1600)。</summary>
    public event Action<int>       OnGhostEaten;

    #endregion

    #region 公開プロパティ

    /// <summary>現在のゲーム状態。</summary>
    public GameState CurrentGameState => _gameState;

    /// <summary>現在のレベル（1 始まり）。</summary>
    public int CurrentLevel => _currentLevel;

    /// <summary>現在の残機数。</summary>
    public int CurrentLives => _currentLives;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_mazeGenerator     == null) Debug.LogError("[B_GameManager] _mazeGenerator が未設定です。");
        if (_pacManMover       == null) Debug.LogError("[B_GameManager] _pacManMover が未設定です。");
        if (_dotManager        == null) Debug.LogError("[B_GameManager] _dotManager が未設定です。");
        if (_ghostHouseManager == null) Debug.LogError("[B_GameManager] _ghostHouseManager が未設定です。");
        if (_blinky == null) Debug.LogError("[B_GameManager] _blinky が未設定です。");
        if (_pinky  == null) Debug.LogError("[B_GameManager] _pinky が未設定です。");
        if (_inky   == null) Debug.LogError("[B_GameManager] _inky が未設定です。");
        if (_clyde  == null) Debug.LogError("[B_GameManager] _clyde が未設定です。");

        _allGhosts    = new BaseGhost[] { _blinky, _pinky, _inky, _clyde };
        _currentLevel = 1;
        _currentLives = _initialLives;
    }

    private void Start()
    {
        if (_dotManager != null)
        {
            _dotManager.OnEnergizerEaten += HandleEnergizerEaten;
            _dotManager.OnLevelClear     += HandleLevelClear;
        }

        InitializeLevel(isNewLevel: true);
    }

    private void OnDestroy()
    {
        if (_dotManager != null)
        {
            _dotManager.OnEnergizerEaten -= HandleEnergizerEaten;
            _dotManager.OnLevelClear     -= HandleLevelClear;
        }
    }

    private void Update()
    {
        if (_gameState != GameState.Playing) return;

        // フライテンドタイマーを実時間で減算
        if (_frightened)
        {
            _frightenedTimer -= Time.deltaTime;
            if (_frightenedTimer <= 0f)
                EndFrightened();
        }
    }

    private void FixedUpdate()
    {
        if (_gameState != GameState.Playing) return;

        UpdateScatterChaseTimer();
        CheckGhostCollisions();
    }

    // ─────────────────────────────────────────────────
    //  レベル初期化
    // ─────────────────────────────────────────────────

    /// <summary>
    /// レベルを初期化します。
    /// </summary>
    /// <param name="isNewLevel">
    /// true  … 新レベル / ゲーム開始: 迷路再生成・ドット再カウント・エルロイリセット。
    /// false … 死亡後リスポーン: 迷路を維持しキャラクター座標のみリセット。
    /// </param>
    private void InitializeLevel(bool isNewLevel)
    {
        // 初回は B_MazeGenerator.Awake() で既に生成済みのため再生成しない
        if (isNewLevel && !_isFirstLevel)
            _mazeGenerator.RegenerateTiles();

        _isFirstLevel = false;

        if (isNewLevel)
        {
            _dotManager.Initialize();
            _blinky.ResetElroy();
            OnLevelChanged?.Invoke(_currentLevel);
        }

        // ── キャラクター配置リセット ────────────────
        SO_MazeData mazeData = _mazeGenerator.MazeData;
        _pacManMover.Initialize(mazeData.PacManSpawn);

        // ブリンキー: ハウス外・左向き・スキャッターモードで開始
        _blinky.Initialize(mazeData.BlinkySpawn, new Vector2Int(-1, 0), BaseGhost.GhostMode.Scatter,
                           mazeData.BlinkyScatterTarget);

        // ピンキー / インキー / クライド: ハウス内待機で開始
        _pinky.Initialize(mazeData.PinkySpawn, Vector2Int.zero, BaseGhost.GhostMode.House,
                          mazeData.PinkyScatterTarget);
        _inky.Initialize (mazeData.InkySpawn,  Vector2Int.zero, BaseGhost.GhostMode.House,
                          mazeData.InkyScatterTarget);
        _clyde.Initialize(mazeData.ClydeSpawn, Vector2Int.zero, BaseGhost.GhostMode.House,
                          mazeData.ClydeScatterTarget);

        // ゴーストハウス退出タイミング管理をリセット
        _ghostHouseManager.Initialize();

        // ── スキャッター / チェイス サイクルリセット ─
        _phaseIndex         = 0;
        _phaseTimer         = _scatterChasePhases.Length > 0
                              ? _scatterChasePhases[0].Duration
                              : -1f;
        _scatterChasePaused = false;

        // ── フライテンドリセット ──────────────────
        _frightened      = false;
        _frightenedTimer  = 0f;
        _ghostEatCount   = 0;

        // パックマン速度を通常に戻す
        _pacManMover.SetSpeedRate(_pacManNormalRate);

        // READY! 演出後にプレイ開始
        StartCoroutine(ReadyCoroutine());
    }

    /// <summary>READY! 表示中に移動を停止し、一定時間後にプレイを開始します。</summary>
    private IEnumerator ReadyCoroutine()
    {
        SetGameState(GameState.Ready);
        SetAllMovementEnabled(false);
        yield return new WaitForSeconds(_readyDuration);
        SetGameState(GameState.Playing);
        SetAllMovementEnabled(true);
    }

    // ─────────────────────────────────────────────────
    //  フライテンドモード
    // ─────────────────────────────────────────────────

    private void HandleEnergizerEaten()
    {
        // タイマーをリセット（初回スタート / 延長どちらも同じ処理）
        _frightenedTimer = _frightenedDuration;
        _ghostEatCount   = 0; // 連続食べカウントをリセット

        if (!_frightened)
        {
            _frightened         = true;
            _scatterChasePaused = true;

            // 全ゴーストをフライテンドへ（House / ExitHouse / Dead は SetMode 内で除外）
            foreach (BaseGhost ghost in _allGhosts)
                ghost.SetMode(BaseGhost.GhostMode.Frightened);

            _pacManMover.SetSpeedRate(_pacManFrightenedRate);
        }
        // 既にフライテンド中の場合はタイマー延長のみ（ゴーストモードは維持）
    }

    private void EndFrightened()
    {
        _frightened         = false;
        _frightenedTimer    = 0f;
        _scatterChasePaused = false;

        // フライテンド中のゴーストを元のモード（Scatter / Chase）へ復帰
        foreach (BaseGhost ghost in _allGhosts)
            ghost.ExitFrightened();

        _pacManMover.SetSpeedRate(_pacManNormalRate);
    }

    // ─────────────────────────────────────────────────
    //  スキャッター / チェイス サイクル
    // ─────────────────────────────────────────────────

    /// <summary>
    /// スキャッター / チェイス タイマーを FixedDeltaTime で更新し、
    /// フェーズ終了時に次フェーズへ移行します。
    /// </summary>
    private void UpdateScatterChaseTimer()
    {
        if (_scatterChasePaused)                              return;
        if (_phaseIndex >= _scatterChasePhases.Length)        return;
        if (_scatterChasePhases[_phaseIndex].Duration <= 0f)  return; // 無限フェーズ

        _phaseTimer -= Time.fixedDeltaTime;
        if (_phaseTimer <= 0f)
            AdvanceScatterChasePhase();
    }

    /// <summary>次のスキャッター / チェイス フェーズへ移行します。</summary>
    private void AdvanceScatterChasePhase()
    {
        _phaseIndex++;
        if (_phaseIndex >= _scatterChasePhases.Length) return;

        ScatterChasePhase next = _scatterChasePhases[_phaseIndex];
        _phaseTimer = next.Duration;

        // Scatter / Chase でローミング中のゴーストのみモードを切り替える
        // Frightened / Dead / House / ExitHouse は各サブシステムに任せる
        foreach (BaseGhost ghost in _allGhosts)
        {
            BaseGhost.GhostMode m = ghost.CurrentMode;
            if (m == BaseGhost.GhostMode.Scatter || m == BaseGhost.GhostMode.Chase)
                ghost.SetMode(next.Mode);
        }
    }

    // ─────────────────────────────────────────────────
    //  衝突判定
    // ─────────────────────────────────────────────────

    /// <summary>
    /// パックマンと全ゴーストのタイル一致を FixedUpdate で検査します。
    /// </summary>
    private void CheckGhostCollisions()
    {
        Vector2Int pacTile = _pacManMover.CurrentTile;

        foreach (BaseGhost ghost in _allGhosts)
        {
            if (ghost.CurrentTile != pacTile) continue;

            switch (ghost.CurrentMode)
            {
                case BaseGhost.GhostMode.Frightened:
                    EatGhost(ghost);
                    break;

                case BaseGhost.GhostMode.Scatter:
                case BaseGhost.GhostMode.Chase:
                case BaseGhost.GhostMode.ExitHouse:
                    StartDeathSequence();
                    return; // 死亡確定後は他ゴーストとの判定を打ち切る
                    // House / Dead は当たり判定なし
            }
        }
    }

    /// <summary>フライテンドゴーストを食べます。</summary>
    private void EatGhost(BaseGhost ghost)
    {
        _ghostEatCount++;
        // 1 回目: 200 / 2 回目: 400 / 3 回目: 800 / 4 回目: 1600
        int score = 200 * (int)Mathf.Pow(2f, _ghostEatCount - 1);
        ghost.SetMode(BaseGhost.GhostMode.Dead);
        OnGhostEaten?.Invoke(score);
    }

    // ─────────────────────────────────────────────────
    //  死亡シーケンス
    // ─────────────────────────────────────────────────

    private void StartDeathSequence()
    {
        if (_gameState != GameState.Playing) return;
        StartCoroutine(DeathCoroutine());
    }

    private IEnumerator DeathCoroutine()
    {
        SetGameState(GameState.PacManDead);
        SetAllMovementEnabled(false);

        // 死亡演出
        yield return new WaitForSeconds(_deathPauseDuration);

        _currentLives--;
        OnLivesChanged?.Invoke(_currentLives);

        if (_currentLives <= 0)
        {
            SetGameState(GameState.GameOver);
            yield break;
        }

        // 残機あり → リスポーン
        // エルロイはリセット（Dossier: 死亡後の新ライフでは通常速度から再開）
        _blinky.ResetElroy();
        InitializeLevel(isNewLevel: false);

        // グローバルカウンターへ切り替え（Initialize 後に呼ぶことで Inky / Clyde を記録）
        _ghostHouseManager.OnPacManDied();
    }

    // ─────────────────────────────────────────────────
    //  レベルクリア シーケンス
    // ─────────────────────────────────────────────────

    private void HandleLevelClear()
    {
        if (_gameState != GameState.Playing) return;
        StartCoroutine(LevelClearCoroutine());
    }

    private IEnumerator LevelClearCoroutine()
    {
        SetGameState(GameState.LevelClear);
        SetAllMovementEnabled(false);

        // レベルクリア演出
        yield return new WaitForSeconds(_levelClearPause);

        _currentLevel++;
        InitializeLevel(isNewLevel: true);
    }

    // ─────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────

    private void SetGameState(GameState newState)
    {
        if (_gameState == newState) return;
        _gameState = newState;
        OnGameStateChanged?.Invoke(newState);
    }

    /// <summary>
    /// パックマンおよび全ゴーストの MonoBehaviour 更新を一括で有効 / 無効にします。
    /// Ready / PacManDead / LevelClear の演出中は false を渡して停止してください。
    /// </summary>
    private void SetAllMovementEnabled(bool enable)
    {
        _pacManMover.enabled       = enable;
        _ghostHouseManager.enabled = enable;
        foreach (BaseGhost ghost in _allGhosts)
            ghost.enabled = enable;
    }

    #endregion
}
