using System;
using UnityEngine;

/// <summary>
/// スコア・ハイスコアを管理するビヘイビア。
/// ドット / エナジャイザー / ゴースト食べの得点を集計し、ハイスコアを PlayerPrefs に永続保存する。
/// </summary>
/// <remarks>
/// イベント購読元:
///   B_DotManager.OnScoreEarned  … ドット(10pt) / エナジャイザー(50pt)
///   B_GameManager.OnGhostEaten  … ゴースト(200 / 400 / 800 / 1600pt)
///
/// イベント発火先:
///   OnScoreChanged    → B_UIView  (スコア表示更新)
///   OnHighScoreChanged → B_UIView (ハイスコア表示更新)
/// </remarks>
public class B_ScoreManager : MonoBehaviour
{
    #region 定義

    [SerializeField] private B_DotManager  _dotManager;
    [SerializeField] private B_GameManager _gameManager;

    private int _currentScore;
    private int _highScore;

    private const string HighScoreKey = "PacMan_HighScore";

    /// <summary>スコアが変わったときに発火。引数: 新しいスコア。</summary>
    public event Action<int> OnScoreChanged;

    /// <summary>ハイスコアが更新されたときに発火。引数: 新しいハイスコア。</summary>
    public event Action<int> OnHighScoreChanged;

    #endregion

    #region 公開プロパティ

    /// <summary>現在のスコア。</summary>
    public int CurrentScore => _currentScore;

    /// <summary>セーブ済みのハイスコア。</summary>
    public int HighScore => _highScore;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// スコアを 0 にリセットします。
    /// ゲームオーバー後の再スタート時に B_GameManager から呼んでください（将来実装）。
    /// </summary>
    public void ResetScore()
    {
        _currentScore = 0;
        OnScoreChanged?.Invoke(_currentScore);
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_dotManager  == null) Debug.LogError("[B_ScoreManager] _dotManager が未設定です。");
        if (_gameManager == null) Debug.LogError("[B_ScoreManager] _gameManager が未設定です。");

        _highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    private void Start()
    {
        if (_dotManager  != null) _dotManager.OnScoreEarned += AddScore;
        if (_gameManager != null) _gameManager.OnGhostEaten += AddScore;
    }

    private void OnDestroy()
    {
        if (_dotManager  != null) _dotManager.OnScoreEarned -= AddScore;
        if (_gameManager != null) _gameManager.OnGhostEaten -= AddScore;
    }

    /// <summary>アプリ終了時にハイスコアをディスクへ書き出します。</summary>
    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

    /// <summary>得点を加算し、必要に応じてハイスコアをメモリ上で更新します。</summary>
    private void AddScore(int points)
    {
        _currentScore += points;
        OnScoreChanged?.Invoke(_currentScore);

        if (_currentScore > _highScore)
        {
            _highScore = _currentScore;
            PlayerPrefs.SetInt(HighScoreKey, _highScore); // メモリ上に記録（ディスク書き込みは OnApplicationQuit）
            OnHighScoreChanged?.Invoke(_highScore);
        }
    }

    #endregion
}
