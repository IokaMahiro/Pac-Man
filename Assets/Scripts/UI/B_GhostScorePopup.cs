using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// ゴーストを食べたとき、撃破位置にワールド空間で浮かび上がるスコアポップアップ。
/// B_GameHUD が Instantiate した後 Play() を呼び出してください。
/// </summary>
/// <remarks>
/// 【プレハブの作り方】
///   1. GameObject を作成し、本スクリプトをアタッチ
///   2. 子に TextMeshPro (World Space) コンポーネントを追加し _text に設定
///   3. プレハブ化して B_GameHUD の GhostScorePopupPrefab に登録
/// LateUpdate でメインカメラの方向を向くため、どの視点からでも読めます。
/// </remarks>
public class B_GhostScorePopup : MonoBehaviour
{
    // ワールド空間 TextMeshPro（World Space / non-UGUI）
    [SerializeField] private TextMeshPro _text;

    // 浮上高さ（ワールド単位）
    private const float FloatHeight = 0.5f;
    // 表示時間（実時間・秒）
    private const float Duration = 0.9f;

    // コンボ数別の文字色（配列インデックス = comboCount - 1）
    private static readonly Color[] ComboColors =
    {
        Color.white,                  // ×1 : 200
        new Color(1f, 0.92f, 0.16f),  // ×2 : 400 黄
        new Color(1f, 0.55f, 0.10f),  // ×3 : 800 橙
        new Color(1f, 0.20f, 0.20f),  // ×4 : 1600 赤
    };

    /// <summary>ポップアップアニメーションを開始します。</summary>
    /// <param name="score">表示する得点</param>
    /// <param name="comboCount">連続撃破数（1〜）</param>
    public void Play(int score, int comboCount)
    {
        if (_text == null) return;

        _text.text = score.ToString("N0");

        int idx = Mathf.Clamp(comboCount - 1, 0, ComboColors.Length - 1);
        _text.color = ComboColors[idx];

        // コンボが増えるほど文字を大きくして「稼げた」感を強調
        _text.fontSize = 5f + (comboCount - 1) * 0.5f;

        StartCoroutine(Animate());
    }

    /// <summary>ドット取得用の小さく速いポップアップを再生します。</summary>
    public void PlayDot(int score)
    {
        if (_text == null) return;
        _text.text     = $"+{score}";
        _text.color    = new Color(0.85f, 0.85f, 0.85f, 1f); // 薄い白
        _text.fontSize = 10f;
        StartCoroutine(AnimateDot(0.6f, 0.5f));
    }

    private IEnumerator AnimateDot(float duration,float height)
    {
        Vector3 origin  = transform.position;
        Color   col     = _text.color;

        float   elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            transform.position = origin + Vector3.up * (height * Mathf.Sqrt(t));
            float alpha = Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.35f) / 0.65f);
            _text.color = new Color(col.r, col.g, col.b, alpha);
            yield return null;
        }
        Destroy(gameObject);
    }

    private void LateUpdate()
    {
        // 常にメインカメラに正対させてどの視点でも読めるようにする
        if (Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;
    }

    private IEnumerator Animate()
    {
        Vector3 origin  = transform.position;
        Color   col     = _text.color;
        float   elapsed = 0f;

        while (elapsed < Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / Duration;

            // √t で序盤は速く・後半ゆっくり浮上
            transform.position = origin + Vector3.up * (FloatHeight * Mathf.Sqrt(t));

            // 45% を過ぎたらフェードアウト
            float alpha = Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.45f) / 0.55f);
            _text.color = new Color(col.r, col.g, col.b, alpha);

            yield return null;
        }

        Destroy(gameObject);
    }
}
