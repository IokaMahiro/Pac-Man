using UnityEngine;

/// <summary>
/// ドット（エサ）をゆっくり脈動させるアニメーター。
/// B_MazeGenerator がドット生成時に AddComponent で自動アタッチします。
/// </summary>
public class B_DotAnimator : MonoBehaviour
{
    // スケール脈動の振れ幅（1 ± amplitude）
    private const float Amplitude = 0.4f;
    // 脈動の速さ（rad/s）
    private const float Speed     = 2.5f;

    // ドットごとに位相をずらして一斉脈動を防ぐ
    private float _phase;
    private Vector3 _baseScale;

    private void Awake()
    {
        _phase     = Random.Range(0f, Mathf.PI * 2f);
        _baseScale = transform.localScale;
    }

    private void Update()
    {
        float t = 1f + Mathf.Sin(Time.time * Speed + _phase) * Amplitude;
        transform.localScale = _baseScale * t;
    }
}
