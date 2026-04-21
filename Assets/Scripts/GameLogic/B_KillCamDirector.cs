using System.Collections;
using UnityEngine;

/// <summary>
/// ゴーストを食べたときのキルカメラ演出を制御するビヘイビア。
/// </summary>
/// <remarks>
/// 演出フロー:
///   Phase 1 … トップダウン → シネマ斜め俯瞰位置へ移動（TimeScale が 1 → minScale へ低下）
///   Phase 2 … シネマ位置でホールド
///   Phase 3 … ゴーストへズームイン
///   Phase 4 … ズーム位置でホールド
///   Phase 5 … 元のカメラ位置へ復帰（TimeScale が minScale → 1 へ回復）
///
/// B_GameManager から StartCoroutine(Play(...)) で呼び出してください。
/// _camera が未設定の場合は演出をスキップします。
/// </remarks>
public class B_KillCamDirector : MonoBehaviour
{
    #region 定義

    // 参照
    [SerializeField] private Camera _camera;

    // パックマン背後への後退距離
    [SerializeField] private float _backOffset = 5f;
    // パックマン↔ゴースト軸に対して横方向のオフセット
    [SerializeField] private float _lateralOffset = 3f;
    // 横オフセットの左右をランダムに切り替える
    [SerializeField] private bool _randomizeSide = true;
    // シネマカメラの高さ（Y）
    [SerializeField] private float _cinematicHeight = 4f;
    // シネマ位置への移動秒数
    [SerializeField] private float _moveDuration = 0.5f;

    // シネマ位置で静止する秒数（実時間）
    [SerializeField] private float _holdDuration = 0.25f;

    // ゴーストから手前で止まる距離（シネマ位置→ゴーストの直線上）
    [SerializeField] private float _zoomBackOffset = 2f;
    // ズームイン移動秒数
    [SerializeField] private float _zoomDuration = 0.3f;

    // ズーム位置で静止する秒数（実時間）
    [SerializeField] private float _zoomHoldDuration = 0.2f;

    // 元のカメラ位置への復帰秒数
    [SerializeField] private float _returnDuration = 0.4f;

    // スロー最小倍率（0 に近いほど遅くなる）
    [SerializeField] private float _minTimeScale = 0.04f;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// キルカメラ演出を再生します。
    /// 演出が完了するまで yield return してください。
    /// </summary>
    public IEnumerator Play(Vector3 ghostWorldPos, Vector3 pacManWorldPos)
    {
        if (_camera == null) yield break;

        Vector3 origPos = _camera.transform.position;
        Quaternion origRot = _camera.transform.rotation;
        float origFov = _camera.fieldOfView;

        // ── カメラ位置を計算 ──────────────────────────────────────────
        // パックマン → ゴースト方向（プレイヤーの "前方" に相当）
        Vector3 toGhost = ghostWorldPos - pacManWorldPos;
        toGhost.y = 0f;
        if (toGhost.sqrMagnitude < 0.001f) toGhost = Vector3.forward;
        toGhost = toGhost.normalized;

        // XZ 平面上の横軸（toGhost を 90° 回転）
        Vector3 perp = new(-toGhost.z, 0f, toGhost.x);
        float sideSign = (_randomizeSide && UnityEngine.Random.value/*Random.valueは0〜1の範囲でランダムな値を返す*/ > 0.5f) ? -1f : 1f;

        // シネマ位置: パックマン背後 + 横オフセットの合成
        Vector3 cinPos = pacManWorldPos - toGhost * _backOffset + perp * _lateralOffset * sideSign + Vector3.up * _cinematicHeight;
        Quaternion cinRot = LookAt(cinPos, ghostWorldPos + Vector3.up * 0.5f);
        float cinFov = 55f;

        // ズーム位置: cinPos → ゴーストへの直線上で _zoomBackOffset だけ手前に止まる
        Vector3 cinToGhost = (ghostWorldPos - cinPos).normalized;
        Vector3 zoomPos = ghostWorldPos - cinToGhost * _zoomBackOffset;
        Quaternion zoomRot = LookAt(zoomPos, ghostWorldPos + Vector3.up * 0.5f);
        float zoomFov = 28f;

        // ── Phase 1: シネマ位置へ移動 + スローイン ────────────────────
        yield return Tween(origPos, origRot, origFov, 1f, cinPos, cinRot, cinFov, _minTimeScale, _moveDuration);

        // ── Phase 2: ホールド ─────────────────────────────────────────
        yield return new WaitForSecondsRealtime(_holdDuration);

        // ── Phase 3: ズームイン ───────────────────────────────────────
        yield return Tween(cinPos, cinRot, cinFov, _minTimeScale, zoomPos, zoomRot, zoomFov, _minTimeScale, _zoomDuration);

        // ── Phase 4: ズームホールド ───────────────────────────────────
        yield return new WaitForSecondsRealtime(_zoomHoldDuration);

        // ── Phase 5: 元に戻る + スローアウト ─────────────────────────
        yield return Tween(zoomPos, zoomRot, zoomFov, _minTimeScale, origPos, origRot, origFov, 1f, _returnDuration);

        // ── 完全リセット ──────────────────────────────────────────────
        _camera.transform.SetPositionAndRotation(origPos, origRot);
        _camera.fieldOfView = origFov;
        SetTimeScale(1f);
    }

    #endregion

    #region 非公開メソッド

    /// <summary>カメラ位置・回転・FOV・TimeScale を補間するコルーチン。</summary>
    private IEnumerator Tween(
        Vector3 fromPos, Quaternion fromRot, float fromFov, float fromTS,
        Vector3 toPos, Quaternion toRot, float toFov, float toTS,
        float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float e = EaseInOut(Mathf.Clamp01(elapsed / duration));

            _camera.transform.position = Vector3.Lerp(fromPos, toPos, e);
            _camera.transform.rotation = Quaternion.Slerp(fromRot, toRot, e);
            _camera.fieldOfView = Mathf.Lerp(fromFov, toFov, e);
            SetTimeScale(Mathf.Lerp(fromTS, toTS, e));

            yield return null;
        }
    }

    private static void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale;
    }

    private static Quaternion LookAt(Vector3 from, Vector3 target) =>
        Quaternion.LookRotation(target - from, Vector3.up);

    /// <summary>滑らかな Ease In-Out（2 次関数）。</summary>
    private static float EaseInOut(float t) =>
        t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

    #endregion
}


//public static Quaternion LookRotation(vec3 forward, vec3 up)
//{
//    forward = vector.normalize(forward);

//    var right = vector.normalize(vector.cross(up, forward));
//    var newUp = vector.normalize(vector.cross(forward, right));

//    mat4 mat4 = new mat4();
//    mat4.m0 = new(right, 0);
//    mat4.m1 = new(newUp, 0);
//    mat4.m2 = new(forward, 0);
//    mat4.m3 = new(0, 0, 0, 1);

//    return quaternion.makeRotationMatrix(mat4);
//}//もしかしたらmat4にmat4 = transpose(mat4)いるかも。
