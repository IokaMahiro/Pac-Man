using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// パックマンのグリッド整合移動を制御するビヘイビア。
/// transform.position を直接代入してタイル中心を経由して移動する。
/// </summary>
/// <remarks>
/// 移動ロジック概要:
///   1. Update() で入力を _bufferedDir に保存（プリターン対応）。
///   2. FixedUpdate() でタイル中心へ向かって transform.position で移動。
///   3. タイル中心到達時:
///      a. _bufferedDir の方向が通行可能 → 方向確定・transform.rotation 更新・次タイルへ
///      b. 不可なら _currentDir を継続
///      c. 両方不可 → 停止
///   4. ドット・エナジャイザー取得は OnTriggerEnter（物理トリガー）で検出。
///      タグ "Dot" / "Energizer" で種別を判別する。
///      ゴーストとの衝突も OnTriggerEnter で検出し、OnGhostHit イベントを発火する。
///
/// Prefab / Layer 要件:
///   PacMan        … SphereCollider(IsTrigger=ON) / Rigidbody(isKinematic=ON, 移動には不使用)
///   Dot Prefab    … SphereCollider(IsTrigger=ON, Radius≈0.05) / Tag: Dot
///   Energizer     … SphereCollider(IsTrigger=ON, Radius≈0.10) / Tag: Energizer
///   Ghost Prefab  … SphereCollider(IsTrigger=ON)
/// </remarks>
public class B_PacManMover : MonoBehaviour
{
    #region 定義

    [Header("移動パラメータ")]
    [SerializeField] private float _baseSpeed = 9.47f; // 基準速度: 75.76 px/s ÷ 8 px/tile

    [Header("速度倍率 (Level1 通常: 0.80 / フライテンド中: 0.90)")]
    [SerializeField, Range(0f, 2f)] private float _speedRate = 0.80f;

    [SerializeField] private B_MazeGenerator _mazeGenerator;

    // タイル座標・移動状態
    private Vector2Int _currentTile;   // パックマンが現在いるタイル
    private Vector2Int _targetTile;    // 移動先タイル（到達後に更新）
    private Vector2Int _currentDir;    // 現在の移動方向（タイル単位）
    private Vector2Int _bufferedDir;   // 次の交差点で試みる方向（プリターン用）

    // ドット・エナジャイザー食べ後の停止フレーム残数
    private int _stopFramesRemaining;

    // タグ定数（Inspector の Tag 設定と一致させること）
    private const string TagDot       = "Dot";
    private const string TagEnergizer = "Energizer";

    public event Action<bool> OnDotEaten;

    public event Action<BaseGhost> OnGhostHit;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// パックマンを指定タイルに配置し、移動状態をリセットします。
    /// レベル開始・残機消失時に B_GameManager から呼んでください。
    /// </summary>
    /// <param name="spawnTile">配置するタイル座標</param>
    public void Initialize(Vector2Int spawnTile)
    {
        _currentTile         = spawnTile;
        _targetTile          = spawnTile;
        _currentDir          = Vector2Int.zero;
        _bufferedDir         = Vector2Int.zero;
        _stopFramesRemaining = 0;

        transform.position = _mazeGenerator.TileToWorld(spawnTile);
    }

    /// <summary>
    /// 移動速度倍率を変更します。
    /// エナジャイザー取得時（0.90）などに B_GameManager から呼んでください。
    /// </summary>
    /// <param name="rate">速度倍率（0 以上）</param>
    public void SetSpeedRate(float rate)
    {
        if (rate < 0f) return;
        _speedRate = rate;
    }

    /// <summary>現在のタイル座標を返します。</summary>
    public Vector2Int CurrentTile => _currentTile;

    /// <summary>
    /// 現在の移動方向をタイル単位で返します。
    /// B_PinkyAI・B_InkyAI のチェイスターゲット計算（先読み）に使用します。
    /// </summary>
    public Vector2Int CurrentDir => _currentDir;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_mazeGenerator == null)
        {
            Debug.LogError("[B_PacManMover] _mazeGenerator がアタッチされていません。");
            return;
        }

        Initialize(_mazeGenerator.MazeData.PacManSpawn);
    }

    private void Update()
    {
        ReadInput();
    }

    private void FixedUpdate()
    {
        // ドット食べ後の停止フレームをカウントダウン
        if (_stopFramesRemaining > 0)
        {
            _stopFramesRemaining--;
            return;
        }

        // 方向が定まっていない場合はスキップ
        if (_currentDir == Vector2Int.zero && _bufferedDir == Vector2Int.zero) return;

        MoveStep();
    }

    /// <summary>キーボード入力を読み取り _bufferedDir を更新します。</summary>
    private void ReadInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if      (kb.upArrowKey.isPressed    || kb.wKey.isPressed) _bufferedDir = new Vector2Int( 0, -1);
        else if (kb.downArrowKey.isPressed  || kb.sKey.isPressed) _bufferedDir = new Vector2Int( 0,  1);
        else if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) _bufferedDir = new Vector2Int(-1,  0);
        else if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) _bufferedDir = new Vector2Int( 1,  0);
    }

    /// <summary>1 FixedUpdate フレーム分の移動処理を実行します。</summary>
    private void MoveStep()
    {
        Vector3 targetWorld = _mazeGenerator.TileToWorld(_targetTile);

        // Y 軸を無視した平面上の距離で判定
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        float   dist    = Vector3.Distance(flatPos, targetWorld);
        float   step    = _baseSpeed * _speedRate * Time.fixedDeltaTime;

        // 「このフレームでタイル中心に到達可能」かどうかでスナップを判定する。
        // 固定しきい値を使うと 1 フレームの移動量 > しきい値 のとき
        // 中心を行き過ぎ → 引き返し → 行き過ぎ の振動が起きるためこの方式を採用。
        if (dist <= step)
        {
            // ──── タイル中心到達時の処理 ────
            // currentTile と targetTile が異なる場合のみ到達イベントを処理
            // （停止中の再呼び出しで二重処理しないようにする）
            if (_currentTile != _targetTile)
            {
                transform.position = targetWorld;
                _currentTile       = _targetTile;

                HandleTunnelWarp(); // トンネル到達時のワープ
                // ドット取得は OnTriggerEnter（物理トリガー）で処理する
            }

            ChooseNextTile(); // 毎フレーム呼ぶことで、停止中の入力にも即応答
        }
        else
        {
            // ──── タイル中心へ向けて移動 ────
            Vector3 moveDir = (targetWorld - transform.position).normalized;
            transform.position += moveDir * step;
        }
    }

    /// <summary>
    /// トンネルワープを処理します。
    /// _currentTile が迷路範囲外の場合、反対側のトンネルタイルへ瞬時に移動します。
    /// </summary>
    private void HandleTunnelWarp()
    {
        if (_currentTile.x < 0)
        {
            _currentTile       = new Vector2Int(SO_MazeData.Cols - 1, _currentTile.y);
            _targetTile        = _currentTile;
            transform.position = _mazeGenerator.TileToWorld(_currentTile);
        }
        else if (_currentTile.x >= SO_MazeData.Cols)
        {
            _currentTile       = new Vector2Int(0, _currentTile.y);
            _targetTile        = _currentTile;
            transform.position = _mazeGenerator.TileToWorld(_currentTile);
        }
    }

    /// <summary>
    /// コライダーとの接触を物理トリガーで検出します。
    /// ドット / エナジャイザー / ゴーストを Tag で判別して処理します。
    /// </summary>
    /// <remarks>
    /// OnTriggerEnter は MonoBehaviour.enabled=false でも物理エンジンから呼ばれるため、
    /// 演出中（Ready/PacManDead/LevelClear）の誤検出を防ぐために先頭で enabled チェックを行う。
    /// </remarks>
    private void OnTriggerEnter(Collider other)
    {
        if (!enabled) return;

        // ── ドット / エナジャイザー ──────────────────────
        // Energizer を先に評価することで、Dot 時の CompareTag 呼び出しを最小化する。
        bool isEnergizer = other.CompareTag(TagEnergizer);
        if (isEnergizer || other.CompareTag(TagDot))
        {
            _stopFramesRemaining = isEnergizer ? 3 : 1;
            Vector2Int tile = _mazeGenerator.WorldToTile(other.transform.position);
            _mazeGenerator.RemoveTileObject(tile);
            OnDotEaten?.Invoke(isEnergizer);
            return;
        }

        // ── ゴースト ────────────────────────────────────
        BaseGhost ghost = other.GetComponent<BaseGhost>();
        if (ghost != null)
            OnGhostHit?.Invoke(ghost);
    }

    /// <summary>
    /// 次のターゲットタイルを決定します。
    /// バッファード方向（プリターン）→ 現在方向の順に試み、
    /// どちらも不可なら停止します。
    /// </summary>
    private void ChooseNextTile()
    {
        // プリターン: バッファード方向を優先
        if (_bufferedDir != Vector2Int.zero && TrySetTargetTile(_bufferedDir))
        {
            _currentDir = _bufferedDir;
            UpdateFacingDir();
            return;
        }
        // 現在方向を継続
        if (_currentDir != Vector2Int.zero && TrySetTargetTile(_currentDir)) return;

        // 壁などで停止
        _currentDir = Vector2Int.zero;
    }

    /// <summary>
    /// 指定方向のタイルが通行可能なら _targetTile に設定し true を返します。
    /// トンネル境界越えを考慮します。
    /// </summary>
    /// <param name="dir">移動方向（タイル単位）</param>
    private bool TrySetTargetTile(Vector2Int dir)
    {
        Vector2Int next = _currentTile + dir;

        // トンネル境界越え: 現在タイルがトンネルなら範囲外への移動を許可
        // TileToWorld は範囲外の col を正しくワールド座標に変換できる
        if (next.x < 0 || next.x >= SO_MazeData.Cols)
        {
            if (_mazeGenerator.MazeData.GetTile(_currentTile) == SO_MazeData.TileType.Tunnel)
            {
                _targetTile = next;
                return true;
            }
            return false;
        }

        if (!_mazeGenerator.MazeData.IsPassableForPacMan(next.x, next.y)) return false;

        _targetTile = next;
        return true;
    }

    /// <summary>
    /// _currentDir に応じて transform.rotation を更新します。
    /// タイル単位の方向ベクトルをワールドの forward に変換します。
    /// </summary>
    private void UpdateFacingDir()
    {
        if (_currentDir == Vector2Int.zero) return;
        transform.rotation = Quaternion.LookRotation(
            new Vector3(_currentDir.x, 0f, _currentDir.y));
    }

    #endregion
}
