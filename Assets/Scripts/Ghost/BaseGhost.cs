using UnityEngine;

/// <summary>
/// 4体のゴーストに共通する移動・モード管理・パスファインディングを実装する抽象基底クラス。
/// 各ゴーストは GetChaseTarget() のみをオーバーライドしてチェイス AI を実装する。
/// </summary>
/// <remarks>
/// ステートマシン構成:
///   各 GhostMode に対応する IGhostState 実装クラスが速度・方向決定・自動遷移を担う。
///   ステートはフィールドを持たないため全ゴーストで単一インスタンスを共有する（s_states）。
///   外部からは SetMode() / ExitFrightened() で遷移し、
///   ステート内部からは InternalTransitionToState() で遷移する（U ターン・SetNextTarget なし）。
///
/// 移動ロジック概要（B_PacManMover と同じグリッド整合方式）:
///   タイル到達時に DecideNextDirection() を呼び、次のターゲットタイルを即決定（ルックアヘッド）。
///   方向優先順: 上 > 左 > 下 > 右。ユークリッド距離最小の方向を選択。
///   U ターン禁止（ExitHouse / Dead は許可）。赤ゾーン（上方向禁止タイル）考慮。
///
/// Rigidbody 推奨設定:
///   isKinematic = true / useGravity = false / Freeze Position Y / Freeze Rotation XYZ
/// </remarks>
public abstract class BaseGhost : MonoBehaviour
{
    #region 定義

    /// <summary>ゴーストの行動モード</summary>
    public enum GhostMode
    {
        House,       // ゴーストハウス内待機
        ExitHouse,   // ゴーストハウス退出中
        Scatter,     // 縄張りモード（迷路四隅を目指す）
        Chase,       // 追跡モード（ゴースト固有の AI ターゲット）
        Frightened,  // フライテンドモード（青・疑似ランダム移動）
        Dead,        // 捕食後（目玉のみでハウスへ帰還）
    }

    [Header("基準速度（B_PacManMover._baseSpeed と合わせる）")]
    [SerializeField] private float _baseSpeed = 9.47f;

    [Header("速度倍率（Level 1）")]
    [SerializeField] private float _normalSpeedRate     = 0.75f;
    [SerializeField] private float _tunnelSpeedRate     = 0.40f;
    [SerializeField] private float _frightenedSpeedRate = 0.50f;
    [SerializeField] private float _deadSpeedRate       = 1.50f;

    [SerializeField] protected B_MazeGenerator _mazeGenerator;
    [SerializeField] private   Rigidbody       _rb;

    [Header("参照（チェイス AI 用）")]
    [SerializeField] protected B_PacManMover _pacManMover;

    [Header("ゴーストハウス設定")]
    [Tooltip("ハウス出入口タイル（ドア直上の通路）")]
    [SerializeField] private Vector2Int _houseEntranceTile = new Vector2Int(13, 11);

    [Tooltip("ハウス内部の中央タイル（退出経路の中継点）")]
    [SerializeField] private Vector2Int _houseCenterTile = new Vector2Int(13, 13);

    [Header("赤ゾーン（上方向転換禁止タイル座標リスト）")]
    [Tooltip("Chase / Scatter 中に上方向へ転換できない T 字路タイルを列挙する。")]
    [SerializeField] private Vector2Int[] _redZoneTiles = new Vector2Int[]
    {
        new Vector2Int( 6, 13), new Vector2Int(21, 13),
        new Vector2Int( 6, 19), new Vector2Int(21, 19),
    };

    // ── 移動状態 ──────────────────────────────
    private GhostMode  _currentMode;
    private GhostMode  _modeBeforeFrightened;

    private Vector2Int _currentTile;
    private Vector2Int _targetTile;
    protected Vector2Int _currentDir;

    protected Vector2Int _scatterTarget;

    // ── ステートマシン ────────────────────────
    private IGhostState _state;

    /// <summary>
    /// 全モード共用のステートシングルトン配列。
    /// GhostMode の int 値をインデックスとして使用する（House=0 … Dead=5）。
    /// </summary>
    private static readonly IGhostState[] s_states = new IGhostState[]
    {
        new GhostStateHouse(),       // 0
        new GhostStateExitHouse(),   // 1
        new GhostStateScatter(),     // 2
        new GhostStateChase(),       // 3
        new GhostStateFrightened(),  // 4
        new GhostStateDead(),        // 5
    };

    // 方向優先順: 上 > 左 > 下 > 右（タイル空間。row 増加 = 画面下）
    private static readonly Vector2Int[] DirectionPriority =
    {
        new Vector2Int( 0, -1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 1,  0),
    };

    #endregion

    #region 公開プロパティ

    /// <summary>現在のタイル座標。B_GameManager のゴースト衝突判定に使用します。</summary>
    public Vector2Int CurrentTile => _currentTile;

    /// <summary>現在の行動モード。</summary>
    public GhostMode CurrentMode => _currentMode;

    #endregion

    #region ステートクラス向け内部アクセサ（internal）

    // ── 読み書き ──────────────────────────────
    /// <summary>現在の移動方向（ステートの Enter で上書き可能）。</summary>
    internal Vector2Int InternalCurrentDir
    {
        get => _currentDir;
        set => _currentDir = value;
    }

    // ── 読み取り専用 ──────────────────────────
    internal B_MazeGenerator InternalMazeGen      => _mazeGenerator;
    internal B_PacManMover   InternalPacMan        => _pacManMover;
    internal Vector2Int      InternalHouseEntrance => _houseEntranceTile;
    /// <summary>ハウス内部の中央タイル。Dead ゴーストの帰還先・ExitHouse の出発点として使用。</summary>
    internal Vector2Int      InternalHouseCenter   => _houseCenterTile;

    /// <summary>
    /// スキャッターターゲット。virtual GetScatterTarget() 経由のため
    /// B_BlinkyAI の Elroy2 オーバーライドが反映されます。
    /// </summary>
    internal Vector2Int InternalScatterTarget => GetScatterTarget();

    /// <summary>
    /// チェイスターゲット。abstract GetChaseTarget() 経由のため
    /// 各サブクラスの固有 AI が使用されます。
    /// </summary>
    internal Vector2Int InternalChaseTarget => GetChaseTarget();

    /// <summary>現在タイルがトンネル内かどうか。</summary>
    internal bool InternalIsInTunnel
        => _mazeGenerator != null &&
           _mazeGenerator.MazeData.GetTile(_currentTile) == SO_MazeData.TileType.Tunnel;

    /// <summary>
    /// 通常速度倍率。virtual GetNormalSpeedRate() 経由のため
    /// B_BlinkyAI のエルロイ加速が反映されます。
    /// </summary>
    internal float InternalNormalSpeedRate    => GetNormalSpeedRate();
    internal float InternalTunnelSpeedRate    => _tunnelSpeedRate;
    internal float InternalFrightenedRate     => _frightenedSpeedRate;
    internal float InternalDeadRate           => _deadSpeedRate;

    // ── パスファインディングヘルパー ────────────

    /// <summary>
    /// ターゲットへの最短方向を返します。ステートの DecideNextDirection から呼んでください。
    /// </summary>
    /// <param name="allowUTurn">true のとき U ターンを許可します（ExitHouse / Dead 用）。</param>
    internal Vector2Int InternalPathfindBest(
        Vector2Int from, Vector2Int incoming, Vector2Int target, bool allowUTurn)
        => DecideBestDirection(from, incoming, target, allowUTurn);

    /// <summary>フライテンド用ランダム方向を返します。</summary>
    internal Vector2Int InternalPathfindFrightened(Vector2Int from, Vector2Int incoming)
        => DecideFrightenedDirection(from, incoming);

    /// <summary>
    /// Dead ゴーストの BFS 用 通行可能判定。
    /// 赤ゾーン・U ターン制限を一切適用せず、壁判定のみを行います。
    /// </summary>
    internal bool InternalIsPassableForDeadGhost(Vector2Int tile)
    {
        if (tile.x < 0 || tile.x >= SO_MazeData.Cols ||
            tile.y < 0 || tile.y >= SO_MazeData.Rows)
            return false;
        return _mazeGenerator.MazeData.IsPassableForGhost(tile.x, tile.y);
    }

    // ── ステート遷移（OnTileReached からの内部遷移用）──

    /// <summary>
    /// Enter / Exit のみを実行するステート遷移。
    /// U ターンも SetNextTarget も行いません。
    /// MoveStep 内の OnTileReached 後に SetNextTarget が呼ばれるため、
    /// タイル到達時の自動遷移（ExitHouse → Scatter、Dead → House）にのみ使用してください。
    /// </summary>
    internal void InternalTransitionToState(GhostMode newMode)
    {
        _state?.Exit(this);
        _currentMode = newMode;
        _state       = s_states[(int)newMode];
        _state.Enter(this);
    }

    #endregion

    #region 公開メソッド

    /// <summary>
    /// ゴーストを指定タイルに配置し、移動状態を初期化します。
    /// B_GameManager から呼んでください。
    /// </summary>
    /// <param name="scatterTarget">
    /// スキャッターターゲット。SO_MazeData の XxxScatterTarget を渡してください。
    /// </param>
    public void Initialize(Vector2Int spawnTile, Vector2Int initialDir, GhostMode initialMode,
                           Vector2Int scatterTarget)
    {
        _scatterTarget        = scatterTarget;
        _currentTile          = spawnTile;
        _targetTile           = spawnTile;
        _currentDir           = initialDir;
        _modeBeforeFrightened = GhostMode.Scatter;

        Vector3 spawnWorld = _mazeGenerator.TileToWorld(spawnTile);
        _rb.position       = spawnWorld;
        transform.position = spawnWorld;

        InternalTransitionToState(initialMode);

        if (initialMode != GhostMode.House)
            SetNextTarget();
    }

    /// <summary>
    /// モードを切り替えます。
    /// Scatter ↔ Chase 切替時・Frightened 開始時に即時 U ターンします。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void SetMode(GhostMode newMode)
    {
        if (_currentMode == newMode) return;

        GhostMode prevMode = _currentMode;

        // フライテンド: ハウス内・退出中・帰還中は適用しない
        if (newMode == GhostMode.Frightened &&
            (prevMode == GhostMode.House    ||
             prevMode == GhostMode.ExitHouse ||
             prevMode == GhostMode.Dead))
            return;

        // フライテンド解除時の復帰先を記録
        if (newMode == GhostMode.Frightened)
            _modeBeforeFrightened = prevMode;

        // U ターン: ローミングモード間の切り替え時に即反転
        bool wasRoaming = prevMode == GhostMode.Scatter   ||
                          prevMode == GhostMode.Chase      ||
                          prevMode == GhostMode.Frightened;

        if (wasRoaming && (newMode == GhostMode.Scatter   ||
                           newMode == GhostMode.Chase      ||
                           newMode == GhostMode.Frightened))
        {
            ReverseDirection(); // _currentDir と _targetTile を直接更新
        }

        InternalTransitionToState(newMode);

        // ExitHouse / Dead は Enter 後に SetNextTarget で初期ターゲットを確定する
        // Scatter / Chase は ReverseDirection が targetTile を設定済みのため不要
        // Frightened は ReverseDirection が targetTile を設定済みのため不要
        if (newMode == GhostMode.ExitHouse || newMode == GhostMode.Dead)
            SetNextTarget();
    }

    /// <summary>
    /// フライテンド状態を解除し、元のモードへ復帰します。
    /// U ターンなし（原作仕様）。B_GameManager のタイマーから呼んでください。
    /// </summary>
    public void ExitFrightened()
    {
        if (_currentMode != GhostMode.Frightened) return;
        InternalTransitionToState(_modeBeforeFrightened);
        SetNextTarget();
    }

    #endregion

    #region 非公開メソッド

    /// <summary>チェイスモード時のターゲットタイルを返します（各サブクラスで実装）。</summary>
    protected abstract Vector2Int GetChaseTarget();

    /// <summary>
    /// スキャッターモード時のターゲットタイルを返します。
    /// B_BlinkyAI が Elroy2 時にチェイスターゲットへ差し替えるためオーバーライド可能。
    /// </summary>
    protected virtual Vector2Int GetScatterTarget() => _scatterTarget;

    /// <summary>
    /// 通常移動時の速度倍率を返します。
    /// B_BlinkyAI がエルロイ加速のためにオーバーライドします。
    /// </summary>
    protected virtual float GetNormalSpeedRate() => _normalSpeedRate;

    /// <summary>サブクラスの Awake 処理をオーバーライドします（_scatterTarget の設定など）。</summary>
    protected virtual void OnAwake() { }

    private void Awake()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            Debug.LogError($"[{GetType().Name}] Rigidbody が見つかりません。");
        if (_mazeGenerator == null)
            Debug.LogError($"[{GetType().Name}] _mazeGenerator がアタッチされていません。");

        // デフォルトステートを設定（Initialize 前に FixedUpdate が呼ばれた場合の安全策）
        _state = s_states[(int)GhostMode.House];

        OnAwake();
    }

    private void FixedUpdate()
    {
        if (_mazeGenerator == null || _rb == null) return;
        if (_currentMode == GhostMode.House) return;
        MoveStep();
    }

    // ─────────────────────────────────────────
    //  移動処理
    // ─────────────────────────────────────────

    private void MoveStep()
    {
        Vector3 targetWorld = _mazeGenerator.TileToWorld(_targetTile);
        Vector3 flatPos     = new Vector3(_rb.position.x, 0f, _rb.position.z);
        float   speed       = _baseSpeed * _state.GetSpeedRate(this);
        float   step        = speed * Time.fixedDeltaTime;
        float   dist        = Vector3.Distance(flatPos, targetWorld);

        if (dist <= step)
        {
            _rb.MovePosition(targetWorld);
            _currentTile = _targetTile;

            HandleTunnelWarp();
            _state.OnTileReached(this); // 自動モード遷移（ExitHouse→Scatter, Dead→House）

            if (_currentMode != GhostMode.House)
                SetNextTarget();
        }
        else
        {
            Vector3 moveDir = (targetWorld - _rb.position).normalized;
            _rb.MovePosition(_rb.position + moveDir * step);
        }
    }

    /// <summary>現在のステートに従い、次のターゲットタイルと移動方向を設定します。</summary>
    private void SetNextTarget()
    {
        Vector2Int newDir = _state.DecideNextDirection(this, _currentTile, _currentDir);
        _currentDir = newDir;

        Vector2Int next = _currentTile + _currentDir;

        // トンネル境界越え
        if ((next.x < 0 || next.x >= SO_MazeData.Cols) &&
            _mazeGenerator.MazeData.GetTile(_currentTile) == SO_MazeData.TileType.Tunnel)
        {
            _targetTile = next;
            return;
        }

        _targetTile = next;
    }

    // ─────────────────────────────────────────
    //  パスファインディング（IGhostState から internal 経由で呼ばれる）
    // ─────────────────────────────────────────

    /// <summary>
    /// ターゲットへのユークリッド距離が最小になる方向を返します。
    /// 同距離の場合は 上 > 左 > 下 > 右 の優先順を適用します。
    /// </summary>
    /// <param name="allowUTurn">true のとき U ターンを許可します（ExitHouse / Dead 用）。</param>
    private Vector2Int DecideBestDirection(
        Vector2Int fromTile, Vector2Int incomingDir, Vector2Int target, bool allowUTurn)
    {
        Vector2Int bestDir  = Vector2Int.zero;
        float      bestDist = float.MaxValue;

        foreach (Vector2Int dir in DirectionPriority)
        {
            if (!allowUTurn && dir == -incomingDir) continue;

            Vector2Int testTile = fromTile + dir;
            if (!IsPassableForGhost(testTile, dir, fromTile)) continue;

            float dx   = testTile.x - target.x;
            float dz   = testTile.y - target.y;
            float dist = dx * dx + dz * dz;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestDir  = dir;
            }
        }

        return bestDir != Vector2Int.zero ? bestDir : -incomingDir;
    }

    /// <summary>
    /// フライテンド中のランダム方向を返します。
    /// U ターンと壁方向は除外します。
    /// </summary>
    private Vector2Int DecideFrightenedDirection(Vector2Int fromTile, Vector2Int incomingDir)
    {
        var candidates = new System.Collections.Generic.List<Vector2Int>(4);
        foreach (Vector2Int dir in DirectionPriority)
        {
            if (dir == -incomingDir) continue;
            if (IsPassableForGhost(fromTile + dir, dir, fromTile))
                candidates.Add(dir);
        }

        if (candidates.Count == 0) return -incomingDir;
        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>
    /// 指定タイルへの移動がゴーストにとって通行可能かを返します。
    /// Scatter / Chase 中に赤ゾーンの上方向転換禁止を適用します。
    /// </summary>
    /// <remarks>
    /// GhostDoor は ExitHouse / Dead モード専用。
    /// Scatter / Chase / Frightened 中はドアを通過できない（ハウス再侵入防止）。
    /// ExitHouse は BFS で InternalIsPassableForDeadGhost を使うためここを通らない。
    /// Dead も同様。
    /// </remarks>
    private bool IsPassableForGhost(Vector2Int tile, Vector2Int dir, Vector2Int fromTile)
    {
        if (tile.x < 0 || tile.x >= SO_MazeData.Cols ||
            tile.y < 0 || tile.y >= SO_MazeData.Rows)
            return false;

        if (!_mazeGenerator.MazeData.IsPassableForGhost(tile.x, tile.y)) return false;

        // GhostDoor は ExitHouse / Dead 以外では通行不可
        // （Scatter/Chase/Frightened 中にゴーストがハウスに再侵入するのを防ぐ）
        if (_mazeGenerator.MazeData.GetTile(tile) == SO_MazeData.TileType.GhostDoor &&
            _currentMode != GhostMode.ExitHouse &&
            _currentMode != GhostMode.Dead)
            return false;

        // 赤ゾーン: Chase / Scatter 中は上方向転換禁止
        if (dir == new Vector2Int(0, -1) &&
            (_currentMode == GhostMode.Chase || _currentMode == GhostMode.Scatter) &&
            IsRedZoneTile(fromTile))
            return false;

        return true;
    }

    private bool IsRedZoneTile(Vector2Int tile)
    {
        if (_redZoneTiles == null) return false;
        foreach (Vector2Int redTile in _redZoneTiles)
            if (redTile == tile) return true;
        return false;
    }

    // ─────────────────────────────────────────
    //  補助処理
    // ─────────────────────────────────────────

    /// <summary>現在位置を基点に移動方向を 180 度反転し、ターゲットを更新します。</summary>
    private void ReverseDirection()
    {
        _currentDir = -_currentDir;
        Vector2Int reversed = _currentTile + _currentDir;

        if (reversed.x >= 0 && reversed.x < SO_MazeData.Cols &&
            reversed.y >= 0 && reversed.y < SO_MazeData.Rows &&
            _mazeGenerator.MazeData.IsPassableForGhost(reversed.x, reversed.y))
        {
            _targetTile = reversed;
        }
    }

    private void HandleTunnelWarp()
    {
        if (_currentTile.x < 0)
        {
            _currentTile       = new Vector2Int(SO_MazeData.Cols - 1, _currentTile.y);
            _targetTile        = _currentTile;
            _rb.position       = _mazeGenerator.TileToWorld(_currentTile);
            transform.position = _rb.position;
        }
        else if (_currentTile.x >= SO_MazeData.Cols)
        {
            _currentTile       = new Vector2Int(0, _currentTile.y);
            _targetTile        = _currentTile;
            _rb.position       = _mazeGenerator.TileToWorld(_currentTile);
            transform.position = _rb.position;
        }
    }

    #endregion
}
