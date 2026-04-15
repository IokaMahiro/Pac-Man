using System;
using UnityEngine;

/// <summary>
/// ドット・エナジャイザーの残数を管理し、関連する各種イベントを発火するビヘイビア。
/// </summary>
/// <remarks>
/// イベント購読先（各 Step で接続）:
///   OnScoreEarned      → B_ScoreManager  (Step 8)
///   OnEnergizerEaten   → B_GameManager   (Step 7) ゴーストをフライテンドモードへ
///   OnLevelClear       → B_GameManager   (Step 7) レベルクリア処理
///   OnBonusFruitSpawn  → B_BonusFruit    (Step 9) ボーナスシンボル出現
/// </remarks>
public class B_DotManager : MonoBehaviour
{
    #region 定義

    [SerializeField] private B_MazeGenerator _mazeGenerator;
    [SerializeField] private B_PacManMover   _pacManMover;

    // ドットカウンター
    private int _totalDots;
    private int _remainingDots;
    private int _eatenDots;       // 食べた累計数（ボーナスフルーツ出現判定用）

    // フルーツ出現しきい値インデックス（70 個目・170 個目）
    private int  _nextFruitIndex;
    private static readonly int[] FruitThresholds = { 70, 170 };

    // 各タイル種別の得点
    private const int DotScore       = 10;
    private const int EnergizerScore = 50;

    /// <summary>ドット or エナジャイザー取得時に発火。引数は加算する点数。</summary>
    public event Action<int> OnScoreEarned;

    /// <summary>
    /// エナジャイザー取得時に発火。
    /// B_GameManager がこれを受けて全ゴーストをフライテンドモードに切り替えます。
    /// </summary>
    public event Action OnEnergizerEaten;

    /// <summary>全ドット取得時（レベルクリア）に発火。</summary>
    public event Action OnLevelClear;

    /// <summary>
    /// ボーナスフルーツ出現タイミングに発火（累計 70 個目・170 個目消費時）。
    /// </summary>
    public event Action OnBonusFruitSpawn;

    #endregion

    #region 公開メソッド

    /// <summary>残ドット数を返します。ゴーストハウス退出判定などに使用します。</summary>
    public int RemainingDots => _remainingDots;

    /// <summary>
    /// ドットカウンターを初期状態にリセットします。
    /// レベル開始時に B_GameManager から呼んでください。
    /// </summary>
    public void Initialize()
    {
        CountTotalDots();
        _remainingDots  = _totalDots;
        _eatenDots      = 0;
        _nextFruitIndex = 0;
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_mazeGenerator == null)
        {
            Debug.LogError("[B_DotManager] _mazeGenerator がアタッチされていません。");
            return;
        }
        if (_pacManMover == null)
        {
            Debug.LogError("[B_DotManager] _pacManMover がアタッチされていません。");
            return;
        }

        Initialize();
    }

    private void Start()
    {
        // B_PacManMover.Awake() 完了後に購読するため Start() で登録する
        _pacManMover.OnDotEaten += HandleDotEaten;
    }

    private void OnDestroy()
    {
        // イベントの購読解除（メモリリーク防止）
        if (_pacManMover != null)
            _pacManMover.OnDotEaten -= HandleDotEaten;
    }

    /// <summary>SO_MazeData を走査し、ドット + エナジャイザーの総数を数えます。</summary>
    private void CountTotalDots()
    {
        _totalDots = 0;
        SO_MazeData data = _mazeGenerator.MazeData;

        for (int row = 0; row < SO_MazeData.Rows; row++)
        {
            for (int col = 0; col < SO_MazeData.Cols; col++)
            {
                SO_MazeData.TileType tile = data.GetTile(col, row);
                if (tile == SO_MazeData.TileType.Dot || tile == SO_MazeData.TileType.Energizer)
                    _totalDots++;
            }
        }
    }

    /// <summary>
    /// B_PacManMover.OnDotEaten の購読ハンドラ。
    /// 残数カウント・各種イベント発火を一括管理します。
    /// </summary>
    /// <param name="isEnergizer">true のときエナジャイザー取得</param>
    private void HandleDotEaten(bool isEnergizer)
    {
        _remainingDots--;
        _eatenDots++;

        // ① 得点イベント
        OnScoreEarned?.Invoke(isEnergizer ? EnergizerScore : DotScore);

        // ② エナジャイザー取得イベント
        if (isEnergizer)
            OnEnergizerEaten?.Invoke();

        // ③ ボーナスフルーツ出現チェック（70 個目・170 個目）
        if (_nextFruitIndex < FruitThresholds.Length
            && _eatenDots >= FruitThresholds[_nextFruitIndex])
        {
            _nextFruitIndex++;
            OnBonusFruitSpawn?.Invoke();
        }

        // ④ レベルクリア判定
        if (_remainingDots <= 0)
            OnLevelClear?.Invoke();
    }

    #endregion
}
