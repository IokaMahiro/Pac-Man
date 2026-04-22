using System.Collections;
using UnityEngine;

/// <summary>
/// ゲームイベントに応じてカメラを振動させるビヘイビア。
/// </summary>
public class B_ScreenShaker : MonoBehaviour
{
    // 参照
    [SerializeField] private Camera           _camera;
    [SerializeField] private B_GameManager    _gameManager;
    [SerializeField] private B_MissionManager _missionManager;
    [SerializeField] private B_DotManager     _dotManager;

    // エナジャイザー取得時
    [SerializeField] private float _energizerDuration  = 0.15f;
    [SerializeField] private float _energizerMagnitude = 0.06f;

    // ミッション達成時
    [SerializeField] private float _missionDuration    = 0.25f;
    [SerializeField] private float _missionMagnitude   = 0.12f;

    // 死亡時
    [SerializeField] private float _deathDuration      = 0.12f;
    [SerializeField] private float _deathMagnitude     = 0.18f;

    // ゲームクリア・ゲームオーバー時
    [SerializeField] private float _clearDuration      = 0.35f;
    [SerializeField] private float _clearMagnitude     = 0.20f;

    private Coroutine _current;

    private void Start()
    {
        if (_dotManager     != null) _dotManager.OnEnergizerEaten       += OnEnergizerEaten;
        if (_missionManager != null) _missionManager.OnMissionCompleted += OnMissionCompleted;
        if (_gameManager    != null) _gameManager.OnGameStateChanged    += OnGameStateChanged;
    }

    private void OnDestroy()
    {
        if (_dotManager     != null) _dotManager.OnEnergizerEaten       -= OnEnergizerEaten;
        if (_missionManager != null) _missionManager.OnMissionCompleted -= OnMissionCompleted;
        if (_gameManager    != null) _gameManager.OnGameStateChanged    -= OnGameStateChanged;
    }

    private void OnEnergizerEaten() =>
        Shake(_energizerDuration, _energizerMagnitude);

    private void OnMissionCompleted(B_MissionManager.ActiveMission _) =>
        Shake(_missionDuration, _missionMagnitude);

    private void OnGameStateChanged(B_GameManager.GameState state)
    {
        switch (state)
        {
            case B_GameManager.GameState.PacManDead:
                Shake(_deathDuration, _deathMagnitude);
                break;
            case B_GameManager.GameState.GameClear:
            case B_GameManager.GameState.GameOver:
                Shake(_clearDuration, _clearMagnitude);
                break;
        }
    }

    /// <summary>
    /// 任意の強さで画面を振動させます。
    /// 呼び出し中に再度呼ぶと現在の振動をリセットして新しい振動を開始します。
    /// </summary>
    public void Shake(float duration, float magnitude)
    {
        if (_camera == null) return;
        if (_current != null) StopCoroutine(_current);
        _current = StartCoroutine(DoShake(duration, magnitude));
    }

    private IEnumerator DoShake(float duration, float magnitude)
    {
        Vector3 origPos = _camera.transform.localPosition;
        float   elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            // KillCam 中はカメラを別スクリプトが制御しているため振動を止める
            if (_gameManager != null &&
                _gameManager.CurrentGameState == B_GameManager.GameState.KillCam)
                break;

            float strength = magnitude * (1f - elapsed / duration); // 徐々に収まる

            _camera.transform.localPosition = origPos + new Vector3(
                Random.Range(-1f, 1f) * strength,
                Random.Range(-1f, 1f) * strength,
                0f);

            yield return null;
        }

        _camera.transform.localPosition = origPos;
        _current = null;
    }
}
