using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// ゲーム HUD 全体を管理するビヘイビア。
/// スコア表示・残機・ミッションパネル・コンボ表示・ポップアップ生成・バナーなどを担当します。
/// </summary>
public class B_GameHUD : MonoBehaviour
{
    #region 定義

    [Header("参照")]
    [SerializeField] private B_GameManager    _gameManager;
    [SerializeField] private B_ScoreManager   _scoreManager;
    [SerializeField] private B_MissionManager _missionManager;

    [Header("スコアエリア")]
    [SerializeField] private TMP_Text _scoreText;
    [SerializeField] private TMP_Text _hiScoreText;
    [SerializeField] private TMP_Text _livesText;

    [Header("ミッションパネル")]
    [Tooltip("Easy / Medium / Hard の順に 3 つ登録してください")]
    [SerializeField] private MissionUIRow[] _missionRows;

    [Header("状態オーバーレイ")]
    [Tooltip("READY! / GAME OVER / GAME CLEAR を表示する GameObject")]
    [SerializeField] private GameObject _stateOverlay;
    [SerializeField] private TMP_Text   _stateText;
    [Tooltip("ゲーム終了時に最終スコアを表示する TMP_Text（StateOverly 内の \"Image\" オブジェクト）")]
    [SerializeField] private TMP_Text   _overlayScoreText;

    [Header("スコアポップアップ")]
    [Tooltip("B_GhostScorePopup を持つワールド空間プレハブ（ゴースト撃破・ドット共通）")]
    [SerializeField] private GameObject _scorePopupPrefab;

    [Header("ミッション達成バナー")]
    [Tooltip("バナー全体の RectTransform（Inspector で表示位置に配置しておく）")]
    [SerializeField] private RectTransform _missionBannerRoot;
    [SerializeField] private TMP_Text      _missionBannerBadge;
    [SerializeField] private TMP_Text      _missionBannerDesc;
    [SerializeField] private float         _bannerHoldDuration = 2.0f;

    [Header("コンボ表示")]
    [Tooltip("×2 / ×4 などを表示する TMP_Text")]
    [SerializeField] private TMP_Text _comboText;

    // ── ミッション行の UI まとまり ──────────────────────────
    [Serializable]
    public class MissionUIRow
    {
        [Tooltip("[E] / [M] / [H] バッジ")]
        public TMP_Text Badge;

        [Tooltip("ミッション説明テキスト")]
        public TMP_Text Description;

        [Tooltip("進捗テキスト（0/1体 など）")]
        public TMP_Text Progress;
    }

    // ── 難易度カラー ─────────────────────────────────────
    private static readonly Color ColorEasy   = new(0.40f, 1.00f, 0.40f);
    private static readonly Color ColorMedium = new(1.00f, 0.85f, 0.20f);
    private static readonly Color ColorHard   = new(1.00f, 0.40f, 0.40f);

    private static readonly Color[] DifficultyColors = { ColorEasy, ColorMedium, ColorHard };
    private static readonly string[] DifficultyLabels = { "[E]", "[M]", "[H]" };

    // ── スコアカウントアップ ───────────────────────────────
    private int   _displayedScore;
    private int   _targetScore;
    private const float ScoreCountRate = 2000f; // pt/秒

    // ── ミッション達成バナー ───────────────────────────────
    private Vector2   _bannerOnPos;
    private const float BannerSlideOffset = 600f;
    private Coroutine _bannerCoroutine;

    // ── コンボ表示 ─────────────────────────────────────────
    private bool      _comboVisible;
    private float     _comboPulseTime;
    private float     _comboPulseBoost;   // ゴースト撃破時に瞬間的に振幅を上乗せ
    private Coroutine _comboFadeCoroutine;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_stateOverlay != null) _stateOverlay.SetActive(false);

        // バナーのオンスクリーン位置を保存してから非表示化
        if (_missionBannerRoot != null)
        {
            _bannerOnPos = _missionBannerRoot.anchoredPosition;
            _missionBannerRoot.gameObject.SetActive(false);
        }

        // コンボテキストは初期非表示
        if (_comboText != null)
        {
            _comboText.gameObject.SetActive(false);
            _comboText.transform.localScale = Vector3.one;
        }
    }

    private void Start()
    {
        if (_scoreManager != null)
        {
            _scoreManager.OnScoreChanged += UpdateScore;
            UpdateScore(_scoreManager.CurrentScore);
        }

        if (_gameManager != null)
        {
            _gameManager.OnLivesChanged      += UpdateLives;
            _gameManager.OnGameStateChanged  += HandleGameStateChanged;
            _gameManager.OnRankDecided       += HandleRankDecided;
            _gameManager.OnGhostEaten        += HandleGhostEaten;
            _gameManager.OnGhostScorePopup   += HandleGhostScorePopup;
            _gameManager.OnFrightenedStarted += HandleFrightenedStarted;
            _gameManager.OnFrightenedEnded   += HandleFrightenedEnded;
            _gameManager.OnDotScored         += HandleDotScored;
            UpdateLives(_gameManager.CurrentLives);
        }

        if (_missionManager != null)
            _missionManager.OnMissionCompleted += HandleMissionCompleted;

        RefreshMissions();
    }

    private void OnDestroy()
    {
        if (_scoreManager != null)
            _scoreManager.OnScoreChanged -= UpdateScore;

        if (_gameManager != null)
        {
            _gameManager.OnLivesChanged      -= UpdateLives;
            _gameManager.OnGameStateChanged  -= HandleGameStateChanged;
            _gameManager.OnRankDecided       -= HandleRankDecided;
            _gameManager.OnGhostEaten        -= HandleGhostEaten;
            _gameManager.OnGhostScorePopup   -= HandleGhostScorePopup;
            _gameManager.OnFrightenedStarted -= HandleFrightenedStarted;
            _gameManager.OnFrightenedEnded   -= HandleFrightenedEnded;
            _gameManager.OnDotScored         -= HandleDotScored;
        }

        if (_missionManager != null)
            _missionManager.OnMissionCompleted -= HandleMissionCompleted;
    }

    private void Update()
    {
        // スコアカウントアップ
        if (_scoreText != null && _displayedScore < _targetScore)
        {
            int step = Mathf.Max(1, Mathf.RoundToInt(ScoreCountRate * Time.unscaledDeltaTime));
            _displayedScore = Mathf.Min(_displayedScore + step, _targetScore);
            _scoreText.text = _displayedScore.ToString("N0");
        }

        // コンボ脈動（フライテンド中ずっと）
        if (_comboVisible && _comboText != null && _comboText.gameObject.activeSelf)
        {
            _comboPulseTime  += Time.unscaledDeltaTime;
            _comboPulseBoost  = Mathf.Max(0f, _comboPulseBoost - Time.unscaledDeltaTime * 4f);

            float amp   = 0.08f + _comboPulseBoost;
            float scale = 1f + amp * Mathf.Abs(Mathf.Sin(_comboPulseTime * 4.5f));
            _comboText.transform.localScale = Vector3.one * scale;
        }

        // タイマーミッション（残り XX 秒）をリアルタイム更新
        if (_gameManager != null &&
            _gameManager.CurrentGameState == B_GameManager.GameState.Playing)
            RefreshMissions();
    }

    // ─────────────────────────────────────────────────────
    //  スコア・残機
    // ─────────────────────────────────────────────────────

    private void UpdateScore(int score)
    {
        _targetScore = score;
        if (_hiScoreText != null) _hiScoreText.text = _scoreManager.HighScore.ToString("N0");
    }

    private void UpdateLives(int lives)
    {
        if (_livesText != null)
            _livesText.text = new string('●', Mathf.Max(0, lives));
    }

    // ─────────────────────────────────────────────────────
    //  スコアポップアップ
    // ─────────────────────────────────────────────────────

    /// <summary>ヒットストップ直後にゴースト撃破ポップアップを生成します。</summary>
    private void HandleGhostScorePopup(int score, int comboCount, Vector3 worldPos)
    {
        SpawnPopup(worldPos + Vector3.up * 0.3f, score, comboCount, isDot: false);
    }

    /// <summary>ドット取得時に倍率適用済みスコアのポップアップを生成します。</summary>
    private void HandleDotScored(int totalScore, Vector3 worldPos)
    {
        SpawnPopup(worldPos + Vector3.up * 0.2f, totalScore, comboCount: 0, isDot: true);
    }

    /// <summary>ゴースト撃破・ドット取得共通のポップアップ生成。</summary>
    private void SpawnPopup(Vector3 worldPos, int score, int comboCount, bool isDot)
    {
        if (_scorePopupPrefab == null) return;
        var go    = Instantiate(_scorePopupPrefab, worldPos, Quaternion.identity);
        var popup = go.GetComponent<B_GhostScorePopup>();
        if (popup == null) return;

        if (isDot) popup.PlayDot(score);
        else       popup.Play(score, comboCount);
    }

    // ─────────────────────────────────────────────────────
    //  コンボ表示
    // ─────────────────────────────────────────────────────

    /// <summary>フライテンド開始：内部状態のみリセット（UI はゴースト撃破まで出さない）。</summary>
    private void HandleFrightenedStarted()
    {
        if (_comboFadeCoroutine != null) { StopCoroutine(_comboFadeCoroutine); _comboFadeCoroutine = null; }
        if (_comboText != null) _comboText.gameObject.SetActive(false);

        _comboVisible    = false;
        _comboPulseTime  = 0f;
        _comboPulseBoost = 0f;
    }

    /// <summary>ゴースト撃破：初回でコンボ UI を表示し、倍率を更新してポップさせます。</summary>
    private void HandleGhostEaten(int score, int comboCount, Vector3 worldPos)
    {
        if (_comboText == null) return;

        // 次の撃破倍率を表示（comboCount=1 → 次は ×2、comboCount=2 → 次は ×4…）
        int nextMultiplier = (int)Mathf.Pow(2f, comboCount);
        _comboText.text  = $"×{nextMultiplier}";
        _comboText.color = comboCount >= 3 ? new Color(1f, 0.20f, 0.20f) :
                           comboCount >= 2 ? new Color(1f, 0.55f, 0.10f) :
                                             new Color(1f, 0.92f, 0.16f);

        if (!_comboVisible)
        {
            _comboText.color = new Color(_comboText.color.r, _comboText.color.g, _comboText.color.b, 1f);
            _comboText.gameObject.SetActive(true);
            _comboVisible   = true;
            _comboPulseTime = 0f;
        }

        _comboPulseBoost = 0.45f;
    }

    /// <summary>フライテンド終了：コンボ UI をフェードアウトして非表示にします。</summary>
    private void HandleFrightenedEnded()
    {
        if (_comboText == null) return;
        _comboVisible = false;
        if (_comboFadeCoroutine != null) StopCoroutine(_comboFadeCoroutine);
        _comboFadeCoroutine = StartCoroutine(FadeOutCombo());
    }

    private IEnumerator FadeOutCombo()
    {
        if (_comboText == null) yield break;
        Color col     = _comboText.color;
        float elapsed = 0f;
        const float FadeDuration = 0.4f;

        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _comboText.color = new Color(col.r, col.g, col.b, 1f - elapsed / FadeDuration);
            yield return null;
        }

        _comboText.gameObject.SetActive(false);
        _comboText.transform.localScale = Vector3.one;
        _comboFadeCoroutine = null;
    }

    // ─────────────────────────────────────────────────────
    //  ミッション達成バナー
    // ─────────────────────────────────────────────────────

    private void HandleMissionCompleted(B_MissionManager.ActiveMission mission)
    {
        if (_bannerCoroutine != null) StopCoroutine(_bannerCoroutine);
        _bannerCoroutine = StartCoroutine(ShowMissionBanner(mission));
    }

    private IEnumerator ShowMissionBanner(B_MissionManager.ActiveMission mission)
    {
        if (_missionBannerRoot == null) yield break;

        int    diffIdx   = (int)mission.Definition.Difficulty;
        Color  diffColor = diffIdx < DifficultyColors.Length ? DifficultyColors[diffIdx] : Color.white;
        string label     = diffIdx < DifficultyLabels.Length ? DifficultyLabels[diffIdx] : "[?]";

        if (_missionBannerBadge != null) { _missionBannerBadge.text = label; _missionBannerBadge.color = diffColor; }
        if (_missionBannerDesc  != null)   _missionBannerDesc.text  = mission.Definition.Description;

        _missionBannerRoot.gameObject.SetActive(true);

        Vector2 offScreen = _bannerOnPos + new Vector2(BannerSlideOffset, 0f);
        yield return TweenAnchoredPos(_missionBannerRoot, offScreen, _bannerOnPos, 0.28f);
        yield return new WaitForSecondsRealtime(_bannerHoldDuration);
        yield return TweenAnchoredPos(_missionBannerRoot, _bannerOnPos, offScreen, 0.28f);

        _missionBannerRoot.gameObject.SetActive(false);
        _bannerCoroutine = null;
    }

    // ─────────────────────────────────────────────────────
    //  ミッションパネル
    // ─────────────────────────────────────────────────────

    private void RefreshMissions()
    {
        if (_missionManager == null || _missionRows == null) return;

        var missions = _missionManager.ActiveMissions;

        for (int i = 0; i < _missionRows.Length; i++)
        {
            MissionUIRow row = _missionRows[i];
            if (row == null || i >= missions.Count) continue;

            var   m       = missions[i];
            int   diffIdx = (int)m.Definition.Difficulty;
            Color diffColor = diffIdx < DifficultyColors.Length
                              ? DifficultyColors[diffIdx] : Color.white;

            if (row.Badge != null)
            {
                row.Badge.text  = diffIdx < DifficultyLabels.Length
                                  ? DifficultyLabels[diffIdx] : "[?]";
                row.Badge.color = diffColor;
            }

            if (row.Description != null)
            {
                row.Description.text  = m.Definition.Description;
                row.Description.color = (m.IsFailed || m.IsComplete)
                                        ? new Color(0.55f, 0.55f, 0.55f)
                                        : Color.white;
            }

            if (row.Progress != null)
            {
                row.Progress.text  = m.ProgressText();
                row.Progress.color = m.IsFailed   ? new Color(1.0f, 0.3f, 0.3f) :
                                     m.IsComplete ? new Color(0.4f, 1.0f, 0.4f) :
                                                    new Color(1.0f, 1.0f, 0.5f);
            }
        }
    }

    // ─────────────────────────────────────────────────────
    //  状態オーバーレイ
    // ─────────────────────────────────────────────────────

    private void HandleGameStateChanged(B_GameManager.GameState state)
    {
        string text  = null;
        Color  color = Color.white;

        switch (state)
        {
            case B_GameManager.GameState.Ready:
                text  = "READY!";
                color = new Color(1.0f, 1.0f, 0.0f);
                if (_overlayScoreText != null) _overlayScoreText.text = "";
                break;

            case B_GameManager.GameState.GameOver:
                text  = "GAME OVER";
                color = new Color(1.0f, 0.2f, 0.2f);
                ShowFinalScore();
                break;

            case B_GameManager.GameState.GameClear:
                text  = "GAME CLEAR!";
                color = new Color(0.4f, 1.0f, 0.4f);
                ShowFinalScore();
                break;
        }

        bool show = text != null;
        if (_stateOverlay != null) _stateOverlay.SetActive(show);
        if (show && _stateText != null)
        {
            _stateText.text  = text;
            _stateText.color = color;
        }
    }

    /// <summary>ゲーム終了時のオーバーレイにスコアとハイスコアを表示します。</summary>
    private void ShowFinalScore()
    {
        if (_overlayScoreText == null || _scoreManager == null) return;
        int score     = _scoreManager.CurrentScore;
        int highScore = _scoreManager.HighScore;
        _overlayScoreText.text = $"SCORE  {score:N0}\nBEST   {highScore:N0}";
    }

    private void HandleRankDecided(string rank)
    {
        if (_stateText == null) return;
        _stateText.text += $"\nRank : {rank}";
    }

    // ─────────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────────

    private IEnumerator TweenAnchoredPos(RectTransform rt, Vector2 from, Vector2 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            rt.anchoredPosition = Vector2.Lerp(from, to, EaseInOut(Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        rt.anchoredPosition = to;
    }

    private static float EaseInOut(float t) =>
        t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

    #endregion
}
