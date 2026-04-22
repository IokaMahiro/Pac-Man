using UnityEngine;

/// <summary>
/// エナジャイザー（パワーエサ）を浮遊・回転・脈動させるアニメーター。
/// B_MazeGenerator がエナジャイザー生成時に AddComponent で自動アタッチします。
/// </summary>
public class B_EnergizerAnimator : MonoBehaviour
{
    // 浮き上がりの幅（ワールド単位）
    private const float FloatAmplitude = 0.1f;
    // 浮き上がりの速さ（rad/s）
    private const float FloatSpeed     = 2.0f;
    // Y 軸回転の速さ（deg/s）
    private const float RotateSpeed    = 90f;
    // スケール脈動の振れ幅（1 ± amplitude）
    private const float ScaleAmplitude = 0.5f;
    // スケール脈動の速さ（rad/s）
    private const float ScaleSpeed     = 3.0f;

    private float   _phase;
    private Vector3 _basePos;
    private Vector3 _baseScale;

    private void Awake()
    {
        _phase     = Random.Range(0f, Mathf.PI * 2f);
        _basePos   = transform.localPosition;
        _baseScale = transform.localScale;
    }

    private void Update()
    {
        // 浮き上がり（Y 軸）
        float yOffset = Mathf.Sin(Time.time * FloatSpeed + _phase) * FloatAmplitude;
        transform.localPosition = _basePos + Vector3.up * yOffset;

        // Y 軸回転
        transform.Rotate(0f, RotateSpeed * Time.deltaTime, 0f, Space.World);

        // スケール脈動
        float t = 1f + Mathf.Sin(Time.time * ScaleSpeed + _phase) * ScaleAmplitude;
        transform.localScale = _baseScale * t;
    }
}
