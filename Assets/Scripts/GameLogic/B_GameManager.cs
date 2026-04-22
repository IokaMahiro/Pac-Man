using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ゲーム全体の状態機械を管理するビヘイビア。
/// フライテンドモード・ゴースト衝突処理・残機管理を担います。
/// </summary>
/// <remarks>
/// ゲームフロー:
///   Start → StartGame → ReadyCoroutine → Playing
///   Playing → 全ドット取得 → GameClearCoroutine → GameClear（終了）
///   Playing → ゴースト衝突 → DeathCoroutine
///                          → (残機あり) RespawnAfterDeath → ReadyCoroutine → Playing
///                          → (残機なし) GameOver（終了）
/// </remarks>
public class B_GameManager : MonoBehaviour
{
    #region 定義

    /// <summary>ゲームの進行状態</summary>
    public enum GameState
    {
        Ready,       // READY! 表示中
        Playing,     // プレイ中
        KillCam,     // キルカメラ演出中
        PacManDead,  // 死亡演出中
        GameClear,   // ゲームクリア
        GameOver,    // ゲームオーバー
    }

    [Header("参照")]
    [SerializeField] private B_MazeGenerator _mazeGenerator;
    [SerializeField] private B_PacManMover _pacManMover;
    [SerializeField] private B_DotManager _dotManager;
    [SerializeField] private B_ScoreManager _scoreManager;
    [SerializeField] private B_MissionManager _missionManager;
    [SerializeField] private B_KillCamDirector _killCamDirector;
    [SerializeField] private B_DeathDirector _deathDirector;

    [Tooltip("4 体のゴーストを登録してください")]
    [SerializeField] private GhostMover[] _allGhosts;

    [Header("ゲームパラメータ")]
    [SerializeField] private int _initialLives = 3;

    [SerializeField] private float _frightenedDuration = 6f;
    [SerializeField] private float _deathPauseDuration = 1.0f;
    [SerializeField] private float _gameClearPause = 2.0f;
    [SerializeField] private float _readyDuration = 2.0f;
    [SerializeField] private float _pacManNormalRate = 0.80f;
    [SerializeField] private float _pacManFrightenedRate = 0.90f;

    // ── ゲーム状態 ──────────────────────────────────
    private GameState _gameState;
    private int _currentLives;

    // ── フライテンドタイマー ─────────────────────────
    private bool _frightened;
    private float _frightenedTimer;
    private int _ghostEatCount;
    private bool _frightenedWarning; // 警告点滅フェーズに入ったか

    [Tooltip("フライテンド終了の何秒前から点滅警告を出すか")]
    [SerializeField] private float _frightenedWarningTime = 2f;

    // ── ミッション全達成ボーナス ──────────────────────────────
    // 全ミッション達成時に加算するボーナス点
    [SerializeField] private int _allMissionsBonus = 2000;

    // ── ランク判定スコア閾値 ──────────────────────────────────
    // この点以上でスコアポイント +2（S ランクへ寄与）
    [SerializeField] private int _rankScoreHigh = 3000;
    // この点以上でスコアポイント +1（B/C ランクへ寄与）
    [SerializeField] private int _rankScoreMid = 1500;

    // 最後に確定したランク
    private string _currentRank = "";

    /// <summary>残機が変わったときに発火。引数: 新しい残機数。</summary>
    public event Action<int> OnLivesChanged;

    /// <summary>ゲーム状態が変わったときに発火。</summary>
    public event Action<GameState> OnGameStateChanged;

    /// <summary>ゴーストを食べたときに発火。引数: 得点 (200 / 400 / 800 / 1600)。</summary>
    public event Action<int> OnGhostEaten;

    /// <summary>全ミッション達成時に発火。引数: ボーナス点。</summary>
    public event Action<int> OnAllMissionsCompleted;

    /// <summary>ランクが確定したときに発火。引数: ランク文字列（S/A/B/C/D）。</summary>
    public event Action<string> OnRankDecided;

    #endregion

    #region 公開プロパティ

    /// <summary>現在のゲーム状態。</summary>
    public GameState CurrentGameState => _gameState;

    /// <summary>現在の残機数。</summary>
    public int CurrentLives => _currentLives;

    /// <summary>最後に確定したランク（S/A/B/C/D）。</summary>
    public string CurrentRank => _currentRank;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        _currentLives = _initialLives;
    }

    private void Start()
    {
        if (_dotManager != null)
        {
            _dotManager.OnEnergizerEaten += HandleEnergizerEaten;
            _dotManager.OnLevelClear += HandleGameClear;
        }

        if (_pacManMover != null)
            _pacManMover.OnGhostHit += HandleGhostHit;

        if (_missionManager != null)
            _missionManager.OnAllMissionsCompleted += HandleAllMissionsCompleted;

        StartGame();
    }

    private void OnDestroy()
    {
        if (_dotManager != null)
        {
            _dotManager.OnEnergizerEaten -= HandleEnergizerEaten;
            _dotManager.OnLevelClear -= HandleGameClear;
        }

        if (_pacManMover != null)
            _pacManMover.OnGhostHit -= HandleGhostHit;

        if (_missionManager != null)
            _missionManager.OnAllMissionsCompleted -= HandleAllMissionsCompleted;
    }

    private void Update()
    {
        if (_gameState != GameState.Playing) return;

        if (_frightened)
        {
            _frightenedTimer -= Time.deltaTime;

            // 残り _frightenedWarningTime 秒になったら点滅警告開始
            if (!_frightenedWarning && _frightenedTimer <= _frightenedWarningTime)
            {
                _frightenedWarning = true;
                foreach (GhostMover ghost in _allGhosts)
                    ghost.SetFrightenedWarning();
            }

            if (_frightenedTimer <= 0f)
                EndFrightened();
        }
    }

    // ─────────────────────────────────────────────────
    //  初期化
    // ─────────────────────────────────────────────────

    /// <summary>ゲームを最初から開始します。迷路・ドット・キャラクターをすべてリセットします。</summary>
    private void StartGame()
    {
        _scoreManager?.ResetScore();
        _missionManager?.StartMissions();
        _mazeGenerator.RegenerateTiles();
        _dotManager.Initialize();
        ResetCharacters();
        ResetFrightened();
        StartCoroutine(ReadyCoroutine());
    }

    /// <summary>死亡後のリスポーン。迷路・ドットはそのままでキャラクターのみ再配置します。</summary>
    private void RespawnAfterDeath()
    {
        ResetCharacters();
        ResetFrightened();
        StartCoroutine(ReadyCoroutine());
    }

    private void ResetCharacters()
    {
        _pacManMover.Initialize(_mazeGenerator.MazeData.PacManSpawn);
        foreach (GhostMover ghost in _allGhosts)
            ghost.Initialize();
    }

    private void ResetFrightened()
    {
        _frightened = false;
        _frightenedTimer = 0f;
        _ghostEatCount = 0;
        _frightenedWarning = false;
        _pacManMover.SetSpeedRate(_pacManNormalRate);
    }

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
        _frightenedTimer = _frightenedDuration;
        _ghostEatCount = 0;
        _frightenedWarning = false; // 警告フェーズをリセット

        if (!_frightened)
            _pacManMover.SetSpeedRate(_pacManFrightenedRate);

        // 初回・再取得いずれも SetFrightened を呼ぶ（色と点滅を青にリセット）
        _frightened = true;
        foreach (GhostMover ghost in _allGhosts)
            ghost.SetFrightened();
    }

    private void EndFrightened()
    {
        _frightened = false;
        _frightenedTimer = 0f;

        foreach (GhostMover ghost in _allGhosts)
            ghost.ExitFrightened();

        _pacManMover.SetSpeedRate(_pacManNormalRate);
    }

    // ─────────────────────────────────────────────────
    //  全ミッション達成・ランク
    // ─────────────────────────────────────────────────

    private void HandleAllMissionsCompleted()
    {
        _scoreManager?.AddBonus(_allMissionsBonus);
        OnAllMissionsCompleted?.Invoke(_allMissionsBonus);
    }

    /// <summary>
    /// ミッション達成数とスコアからランクを算出します。
    /// ミッション達成数（0〜3）+ スコアポイント（0〜2）= 最大 5 点。
    ///   5点 → S / 4点 → A / 3点 → B / 2点 → C / 0〜1点 → D
    /// </summary>
    private string CalcRank()
    {
        int done = 0;
        if (_missionManager != null)
        {
            foreach (var m in _missionManager.ActiveMissions)
            {
                if (m.IsComplete)
                {
                    done++;
                }
            }
        }

        int score = _scoreManager?.CurrentScore ?? 0;
        int points = done;
        if (score >= _rankScoreHigh)
        {
            points += 2;
        }
        else if (score >= _rankScoreMid)
        {
            points += 1;
        }

        return points switch
        {
            5 => "S",
            4 => "A",
            3 => "B",
            2 => "C",
            _ => "D",
        };
    }

    // 衝突
    private void HandleGhostHit(GhostMover ghost)
    {
        if (_gameState != GameState.Playing) return;
        if (!ghost.IsAlive) return;

        if (ghost.CurrentMode == GhostMover.GhostMode.Frightened)
            EatGhost(ghost);
        else
            StartDeathSequence(ghost);
    }

    private void EatGhost(GhostMover ghost)
    {
        _ghostEatCount++;
        int score = 200 * (int)Mathf.Pow(2f, _ghostEatCount - 1);
        StartCoroutine(EatGhostCoroutine(ghost, score));
    }

    private IEnumerator EatGhostCoroutine(GhostMover ghost, int score)
    {
        SetGameState(GameState.KillCam);
        SetAllMovementEnabled(false);

        if (_killCamDirector != null)
            yield return _killCamDirector.Play(ghost.transform.position, _pacManMover.transform.position);

        ghost.OnEatenByPacMan();
        OnGhostEaten?.Invoke(score);

        SetAllMovementEnabled(true);
        SetGameState(GameState.Playing);
    }

    private void StartDeathSequence(GhostMover killerGhost)
    {
        if (_gameState != GameState.Playing) return;
        StartCoroutine(DeathCoroutine(killerGhost));
    }

    private IEnumerator DeathCoroutine(GhostMover killerGhost)
    {
        SetGameState(GameState.PacManDead);
        SetAllMovementEnabled(false);

        if (_deathDirector != null)
            yield return _deathDirector.Play(_pacManMover.transform.position, killerGhost.transform.position);
        else
            yield return new WaitForSeconds(_deathPauseDuration);

        _currentLives--;
        OnLivesChanged?.Invoke(_currentLives);

        if (_currentLives <= 0)
        {
            SetGameState(GameState.GameOver);
            _currentRank = CalcRank();
            OnRankDecided?.Invoke(_currentRank);
            yield break;
        }

        RespawnAfterDeath();
    }


    // クリア処理
    private void HandleGameClear()
    {
        if (_gameState != GameState.Playing) return;
        StartCoroutine(GameClearCoroutine());
    }

    private IEnumerator GameClearCoroutine()
    {
        SetAllMovementEnabled(false);
        SetGameState(GameState.GameClear);   // MissionManager のハンドラが同期実行される
        _currentRank = CalcRank();           // ミッション状態が確定した後に判定
        OnRankDecided?.Invoke(_currentRank);
        yield return new WaitForSeconds(_gameClearPause);
    }


    private void SetGameState(GameState newState)
    {
        if (_gameState == newState) return;
        _gameState = newState;
        OnGameStateChanged?.Invoke(newState);
    }

    private void SetAllMovementEnabled(bool enable)
    {
        _pacManMover.enabled = enable;
        foreach (GhostMover ghost in _allGhosts)
            ghost.enabled = enable;
    }

    #endregion
}
