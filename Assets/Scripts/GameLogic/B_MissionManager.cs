using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲームごとにランダムな 3 つのミッションを抽選・追跡するビヘイビア。
/// Easy / Medium / Hard から各 1 つを選出し、達成時にボーナスを加算します。
/// </summary>
public class B_MissionManager : MonoBehaviour
{
    #region 定義

    [Header("参照")]
    [SerializeField] private B_GameManager  _gameManager;
    [SerializeField] private B_DotManager   _dotManager;
    [SerializeField] private B_ScoreManager _scoreManager;

    // ── ミッション種別 ────────────────────────────────────────────
    public enum MissionType
    {
        EatGhosts,                // ゴーストを N 体食べる（累計）
        EatGhostsInOneFrightened, // 1 回のフライテンドで N 体食べる
        ScoreTarget,              // N 点以上獲得する
        ClearWithoutDying,        // 残機を失わずにクリア
        ClearWithinTime,          // N 秒以内にクリア
    }

    public enum MissionDifficulty { Easy, Medium, Hard }

    // ── ミッション定義（不変データ）────────────────────────────────
    public class MissionDefinition
    {
        public MissionType       Type;
        public string            Description;
        public int               TargetValue;
        public int               BonusScore;
        public MissionDifficulty Difficulty;
    }

    // ── アクティブミッション（実行時状態）──────────────────────────
    public class ActiveMission
    {
        public MissionDefinition Definition;
        public int  Progress;
        public bool IsFailed;
        public bool IsComplete;

        /// <summary>UI 表示用の進捗文字列。</summary>
        public string ProgressText()
        {
            if (IsFailed)   return "失敗";
            if (IsComplete) return "達成！";

            return Definition.Type switch
            {
                MissionType.ClearWithoutDying => "挑戦中",
                MissionType.ClearWithinTime   =>
                    $"残り {Mathf.Max(0, Definition.TargetValue - Progress)}秒",
                MissionType.ScoreTarget       =>
                    $"{Progress}/{Definition.TargetValue}点",
                _                             =>
                    $"{Progress}/{Definition.TargetValue}体",
            };
        }
    }

    // ── ミッションプール ──────────────────────────────────────────
    private static readonly MissionDefinition[] Pool =
    {
        // Easy
        new() { Type = MissionType.EatGhosts,   Description = "ゴーストを1体食べろ",  TargetValue = 1,   BonusScore = 300,  Difficulty = MissionDifficulty.Easy   },
        new() { Type = MissionType.ScoreTarget,  Description = "500点獲得せよ",        TargetValue = 500, BonusScore = 200,  Difficulty = MissionDifficulty.Easy   },

        // Medium
        new() { Type = MissionType.EatGhosts,                Description = "ゴーストを3体食べろ",         TargetValue = 3,  BonusScore = 800,  Difficulty = MissionDifficulty.Medium },
        new() { Type = MissionType.ClearWithoutDying,        Description = "残機を失わずクリア",          TargetValue = 1,  BonusScore = 1000, Difficulty = MissionDifficulty.Medium },
        new() { Type = MissionType.EatGhostsInOneFrightened, Description = "1フライテンドで2体食べろ",    TargetValue = 2,  BonusScore = 600,  Difficulty = MissionDifficulty.Medium },

        // Hard
        new() { Type = MissionType.EatGhostsInOneFrightened, Description = "1フライテンドで4体全員食べろ", TargetValue = 4,  BonusScore = 2000, Difficulty = MissionDifficulty.Hard  },
        new() { Type = MissionType.ClearWithinTime,           Description = "90秒以内にクリア",           TargetValue = 90, BonusScore = 1500, Difficulty = MissionDifficulty.Hard  },
    };

    // ── 実行時状態 ────────────────────────────────────────────────
    private readonly List<ActiveMission> _activeMissions = new();
    private float _playingTime;
    private int   _frightenedGhostCount; // 今回のフライテンドで食べた体数

    // ── イベント ──────────────────────────────────────────────────

    /// <summary>ミッションが達成されたときに発火。</summary>
    public event Action<ActiveMission> OnMissionCompleted;

    /// <summary>3 つすべてのミッションが達成されたときに発火。</summary>
    public event Action OnAllMissionsCompleted;

    #endregion

    #region 公開プロパティ・メソッド

    /// <summary>現在のアクティブミッション一覧（読み取り専用）。</summary>
    public IReadOnlyList<ActiveMission> ActiveMissions => _activeMissions;

    /// <summary>
    /// ミッションを抽選して開始します。
    /// ゲーム開始時に B_GameManager から呼んでください。
    /// </summary>
    public void StartMissions()
    {
        _activeMissions.Clear();
        _playingTime          = 0f;
        _frightenedGhostCount = 0;

        _activeMissions.Add(PickRandom(MissionDifficulty.Easy));
        _activeMissions.Add(PickRandom(MissionDifficulty.Medium));
        _activeMissions.Add(PickRandom(MissionDifficulty.Hard));
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
    }

    private void Start()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameStateChanged += HandleGameStateChanged;
            _gameManager.OnGhostEaten       += HandleGhostEaten;
        }
        if (_dotManager   != null) _dotManager.OnEnergizerEaten     += HandleEnergizerEaten;
        if (_scoreManager != null) _scoreManager.OnScoreChanged     += HandleScoreChanged;
    }

    private void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameStateChanged -= HandleGameStateChanged;
            _gameManager.OnGhostEaten       -= HandleGhostEaten;
        }
        if (_dotManager   != null) _dotManager.OnEnergizerEaten     -= HandleEnergizerEaten;
        if (_scoreManager != null) _scoreManager.OnScoreChanged     -= HandleScoreChanged;
    }

    private void Update()
    {
        if (_gameManager == null ||
            _gameManager.CurrentGameState != B_GameManager.GameState.Playing) return;

        _playingTime += Time.deltaTime;

        foreach (ActiveMission m in _activeMissions)
        {
            if (m.IsComplete || m.IsFailed) continue;
            if (m.Definition.Type != MissionType.ClearWithinTime) continue;

            m.Progress = Mathf.FloorToInt(_playingTime);

            // 制限時間超過で失敗
            if (_playingTime > m.Definition.TargetValue)
                m.IsFailed = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  イベントハンドラ
    // ─────────────────────────────────────────────────────────────

    private void HandleGhostEaten(int score, int _comboCount, Vector3 _worldPos)
    {
        _frightenedGhostCount++;

        foreach (ActiveMission m in _activeMissions)
        {
            if (m.IsComplete || m.IsFailed) continue;

            switch (m.Definition.Type)
            {
                case MissionType.EatGhosts:
                    m.Progress++;
                    if (m.Progress >= m.Definition.TargetValue) TryComplete(m);
                    break;

                case MissionType.EatGhostsInOneFrightened:
                    m.Progress = _frightenedGhostCount;
                    if (m.Progress >= m.Definition.TargetValue) TryComplete(m);
                    break;
            }
        }
    }

    private void HandleEnergizerEaten()
    {
        _frightenedGhostCount = 0;

        // EatGhostsInOneFrightened: 次のフライテンドで再挑戦できるようリセット
        foreach (ActiveMission m in _activeMissions)
        {
            if (m.IsComplete || m.IsFailed) continue;
            if (m.Definition.Type == MissionType.EatGhostsInOneFrightened)
                m.Progress = 0;
        }
    }

    private void HandleScoreChanged(int totalScore)
    {
        foreach (ActiveMission m in _activeMissions)
        {
            if (m.IsComplete || m.IsFailed) continue;
            if (m.Definition.Type != MissionType.ScoreTarget) continue;

            m.Progress = totalScore;
            if (totalScore >= m.Definition.TargetValue) TryComplete(m);
        }
    }

    private void HandleGameStateChanged(B_GameManager.GameState state)
    {
        if (state == B_GameManager.GameState.PacManDead)
        {
            foreach (ActiveMission m in _activeMissions)
            {
                if (!m.IsComplete && m.Definition.Type == MissionType.ClearWithoutDying)
                    m.IsFailed = true;
            }
            return;
        }

        if (state == B_GameManager.GameState.GameClear)
        {
            foreach (ActiveMission m in _activeMissions)
            {
                if (m.IsComplete || m.IsFailed) continue;

                switch (m.Definition.Type)
                {
                    case MissionType.ClearWithoutDying:
                        TryComplete(m);
                        break;

                    case MissionType.ClearWithinTime:
                        if (_playingTime <= m.Definition.TargetValue)
                            TryComplete(m);
                        else
                            m.IsFailed = true;
                        break;
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  補助
    // ─────────────────────────────────────────────────────────────

    private void TryComplete(ActiveMission m)
    {
        m.IsComplete = true;
        _scoreManager?.AddBonus(m.Definition.BonusScore);
        OnMissionCompleted?.Invoke(m);

        if (_activeMissions.TrueForAll(x => x.IsComplete))
            OnAllMissionsCompleted?.Invoke();
    }

    private static ActiveMission PickRandom(MissionDifficulty difficulty)
    {
        var candidates = System.Array.FindAll(Pool, d => d.Difficulty == difficulty);
        MissionDefinition def = candidates[UnityEngine.Random.Range(0, candidates.Length)];
        return new ActiveMission { Definition = def };
    }

    #endregion
}
