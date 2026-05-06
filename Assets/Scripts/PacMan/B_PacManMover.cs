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


    /// <summary>
    /// 操作性改善関連
    /// </summary>
    //コーナーアシスト:タイル中心の手前この距離内で直角入力を受け付けて早めに旋回する
    [SerializeField, Range(0f, 1.5f)] private float _cornerAssistRadius = 0.5f;
    //入力バッファ保持時間（秒）: キーを離してからもこの時間だけ入力を記憶
    [SerializeField, Range(0f, 0.5f)] private float _inputBufferDuration = 0.3f;
    //旋回速度（Slerp 係数 × 秒）: 大きいほど素早く目標へ近づく、小さいほど緩やかに旋回する
    [SerializeField, Range(1f, 30f)] private float _rotationSpeed = 15f;

    // 旋回先の目標回転（SmoothRotation で毎フレーム補間する）
    private Quaternion _targetRotation = Quaternion.identity;

    // タイル座標・移動状態
    private Vector2Int _currentTile;   // パックマンが現在いるタイル
    private Vector2Int _targetTile;    // 移動先タイル（到達後に更新）
    private Vector2Int _currentDir;    // 現在の移動方向（タイル単位）
    private Vector2Int _bufferedDir;   // 次の交差点で試みる方向（プリターン用）


    // 入力バッファタイマー（キーを離した後も _inputBufferDuration 秒間バッファを保持）
    private float _inputBufferTimer;

    // ドット・エナジャイザー食べ後の停止フレーム残数
    private int _stopFramesRemaining;

    // タグ定数（Inspector の Tag 設定と一致させること）
    private const string TagDot = "Dot";
    private const string TagEnergizer = "Energizer";

    public event Action<bool> OnDotEaten;

    public event Action<GhostMover> OnGhostHit;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// パックマンを指定タイルに配置し、移動状態をリセットします。
    /// レベル開始・残機消失時に B_GameManager から呼んでください。
    /// </summary>
    /// <param name="spawnTile">配置するタイル座標</param>
    public void Initialize(Vector2Int spawnTile)
    {
        _currentTile = spawnTile;
        _targetTile = spawnTile;
        _currentDir = Vector2Int.zero;
        _bufferedDir = Vector2Int.zero;
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
        SmoothRotation();
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

    /// <summary>
    /// キーボード入力を読み取り _bufferedDir を更新します。
    /// キーを離してから _inputBufferDuration 秒間はバッファを保持するため、
    /// 素早いタップでも次のタイル到達まで入力が生き続けます。
    /// </summary>
    private void ReadInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        Vector2Int input = ReadDirectionalInput(kb);

        if (input != Vector2Int.zero)
        {
            // 新たな入力：バッファを更新してタイマーをリセット
            _bufferedDir = input;
            _inputBufferTimer = _inputBufferDuration;
        }
        else
        {
            // 入力なし：タイマーが切れたらバッファをクリア
            _inputBufferTimer -= Time.deltaTime;
            if (_inputBufferTimer <= 0f)
            {
                _bufferedDir = Vector2Int.zero;
                _inputBufferTimer = 0f;
            }
        }
    }

    /// <summary>現在押されているキーから方向ベクトルを返します。何も押されていなければ zero。</summary>
    private static Vector2Int ReadDirectionalInput(Keyboard kb)
    {
        if (kb.upArrowKey.isPressed || kb.wKey.isPressed) return new Vector2Int(0, -1);
        if (kb.downArrowKey.isPressed || kb.sKey.isPressed) return new Vector2Int(0, 1);
        if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) return new Vector2Int(-1, 0);
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) return new Vector2Int(1, 0);
        return Vector2Int.zero;
    }

    /// <summary>1 FixedUpdate フレーム分の移動処理を実行します。</summary>
    private void MoveStep()
    {
        Vector3 targetWorld = _mazeGenerator.TileToWorld(_targetTile);

        // Y 軸を無視した平面上の距離で判定
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        float dist = Vector3.Distance(flatPos, targetWorld);
        float step = _baseSpeed * _speedRate * Time.fixedDeltaTime;

        // Uターンを即座に反映する
        // 反対方向入力はタイル中心を待たず即座に反転する。
        // _targetTile を「来た道」に切り替えるだけでよい。
        if (_bufferedDir != Vector2Int.zero && _bufferedDir == -_currentDir)
        {
            _targetTile = _currentTile; // 進行方向を逆にする
            _currentDir = _bufferedDir;
            UpdateFacingDir();
            // バッファはクリアしない（到着後も方向を保持したいため）
        }

        // 曲がり角を曲がりやすく（早入力: コーナーアシスト）
        // 直角ターンに限り、タイル中心の手前 _cornerAssistRadius 以内で入力を受付て旋回
        if (_bufferedDir != Vector2Int.zero
            && _bufferedDir != _currentDir
            && _bufferedDir != -_currentDir   // U ターンは上で処理済み
            && dist <= _cornerAssistRadius
            && IsPassableFrom(_targetTile, _bufferedDir))
        {
            // タイル中心へスナップして旋回
            transform.position = targetWorld;
            _currentTile = _targetTile;
            HandleTunnelWarp();
            ChooseNextTile();
            return;
        }

        // 曲がり角を曲がりやすく（遅入力: ポストコーナーグレース）
        // タイル中心を通過した直後でも _cornerAssistRadius 以内なら直前タイルへ戻して旋回する。
        // 「通過したばかりのタイル（_currentTile）に曲がれる入力が来た」ケースを救済する。
        if (_bufferedDir != Vector2Int.zero
            && _bufferedDir != _currentDir
            && _bufferedDir != -_currentDir   // U ターンは上で処理済み
            && _currentTile != _targetTile    // 移動中（停止中は不要）
            && IsPassableFrom(_currentTile, _bufferedDir))
        {
            Vector3 currentWorld = _mazeGenerator.TileToWorld(_currentTile);
            Vector3 flatCurrent = new Vector3(currentWorld.x, 0f, currentWorld.z);
            float distToCurrent = Vector3.Distance(flatPos, flatCurrent);

            if (distToCurrent <= _cornerAssistRadius)
            {
                // 直前のタイル中心へ戻してから旋回
                transform.position = currentWorld;
                _targetTile = _currentTile;
                ChooseNextTile();
                return;
            }
        }

        // 通常移動
        // タイル中心到達でスナップ＋次方向決定
        // 「このフレームでタイル中心に到達可能」かどうかでスナップを判定する。
        // 固定しきい値を使うと 1 フレームの移動量 > しきい値 のとき
        if (dist <= step)
        {
            if (_currentTile != _targetTile)
            {
                transform.position = targetWorld;
                _currentTile = _targetTile;
                HandleTunnelWarp();
            }
            ChooseNextTile(); // 停止中の入力にも毎フレーム即応答
        }
        else
        {
            Vector3 moveDir = (targetWorld - transform.position).normalized;
            transform.position += moveDir * step;
        }
    }

    /// <summary>
    /// fromTile から dir 方向への移動がパックマンにとって通行可能か返します。
    /// トンネル境界越えを考慮します。
    /// </summary>
    private bool IsPassableFrom(Vector2Int fromTile, Vector2Int dir)
    {
        Vector2Int next = fromTile + dir;

        if (next.x < 0 || next.x >= SO_MazeData.Cols)
            return _mazeGenerator.MazeData.GetTile(fromTile) == SO_MazeData.TileType.Tunnel;

        return _mazeGenerator.MazeData.IsPassableForPacMan(next.x, next.y);
    }

    /// <summary>
    /// トンネルワープを処理します。
    /// _currentTile が迷路範囲外の場合、反対側のトンネルタイルへ瞬時に移動します。
    /// </summary>
    private void HandleTunnelWarp()
    {
        if (_currentTile.x < 0)
        {
            _currentTile = new Vector2Int(SO_MazeData.Cols - 1, _currentTile.y);
            _targetTile = _currentTile;
            transform.position = _mazeGenerator.TileToWorld(_currentTile);
        }
        else if (_currentTile.x >= SO_MazeData.Cols)
        {
            _currentTile = new Vector2Int(0, _currentTile.y);
            _targetTile = _currentTile;
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
        GhostMover ghost = other.GetComponent<GhostMover>();
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
    /// _targetRotation へ向けて毎フレーム _rotationSpeed 度/秒で滑らかに旋回します。
    /// Update() から呼ばれるため Time.deltaTime を使用します。
    /// </summary>
    private void SmoothRotation()
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, _rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// _currentDir に応じて _targetRotation を更新します。
    /// 実際の旋回は SmoothRotation() が毎フレーム補間して行います。
    /// タイル単位の方向ベクトルをワールドの forward に変換します。
    /// </summary>
    private void UpdateFacingDir()
    {
        if (_currentDir == Vector2Int.zero) return;

        _targetRotation = Quaternion.LookRotation(new Vector3(_currentDir.x, 0f, -_currentDir.y));
    }

    #endregion
}
