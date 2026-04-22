using System;
using System.Collections;
using TMPro;
using UnityEngine;
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

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_stateOverlay != null) _stateOverlay.SetActive(false);
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
            _gameManager.OnLivesChanged          += UpdateLives;
            _gameManager.OnGameStateChanged      += HandleGameStateChanged;
            _gameManager.OnRankDecided           += HandleRankDecided;
            UpdateLives(_gameManager.CurrentLives);
        }

        RefreshMissions();
    }

    private void OnDestroy()
    {
        if (_scoreManager != null) _scoreManager.OnScoreChanged     -= UpdateScore;
        if (_gameManager  != null)
        {
            _gameManager.OnLivesChanged         -= UpdateLives;
            _gameManager.OnGameStateChanged     -= HandleGameStateChanged;
            _gameManager.OnRankDecided          -= HandleRankDecided;
        }
    }

    private void Update()
    {
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
        if (_scoreText   != null) _scoreText.text   = score.ToString("N0");
        if (_hiScoreText != null) _hiScoreText.text = _scoreManager.HighScore.ToString("N0");
    }

    private void UpdateLives(int lives)
    {
        if (_livesText != null)
            _livesText.text = new string('●', Mathf.Max(0, lives));
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

            // バッジ
            if (row.Badge != null)
            {
                row.Badge.text  = diffIdx < DifficultyLabels.Length
                                  ? DifficultyLabels[diffIdx] : "[?]";
                row.Badge.color = diffColor;
            }

            // 説明
            if (row.Description != null)
            {
                row.Description.text  = m.Definition.Description;
                row.Description.color = (m.IsFailed || m.IsComplete)
                                        ? new Color(0.55f, 0.55f, 0.55f)
                                        : Color.white;
            }

            // 進捗
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
                break;
            case B_GameManager.GameState.GameOver:
                text  = "GAME OVER";
                color = new Color(1.0f, 0.2f, 0.2f);
                break;
            case B_GameManager.GameState.GameClear:
                text  = "GAME CLEAR!";
                color = new Color(0.4f, 1.0f, 0.4f);
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

    // ランクが確定したときに呼ばれる。HandleGameStateChanged と同一フレームで実行される
    private void HandleRankDecided(string rank)
    {
        if (_stateText == null) return;
        _stateText.text += $"\nRank : {rank}";
    }
    #endregion
}
