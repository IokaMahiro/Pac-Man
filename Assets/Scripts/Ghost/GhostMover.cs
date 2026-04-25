using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゴーストのグリッド移動・AI・フライテンドモードを管理するビヘイビア。
/// ChaseType を Inspector で設定することで 4 種類の異なる AI 動作を実現する。
/// 4 体すべてに同一コンポーネントをアタッチし、ChaseType で個性を与える。
/// </summary>
/// <remarks>
/// 移動ロジック:
///   タイル到達ごとに BFS でターゲットへの最短経路の「最初の一歩」方向を決定する。
///   Greedy と異なり迷路内の迂回を正しく処理できる。
///
/// モード:
///   Chase      … GetChaseTarget() が返すタイルへ BFS で追跡。
///   Frightened … 通行可能方向からランダム移動。
///
/// 食べられた後:
///   コライダーと描画を無効化し、_respawnDelay 秒後にスポーン位置へテレポート復活。
///
/// ChaseType 一覧:
///   Direct    … パックマンの現在タイルへ直行（Blinky 相当）。
///   LookAhead … パックマンの進行方向 3 タイル先を狙う（Pinky 相当）。
///   Mirror    … パックマンを中心に自分の逆側を狙う（Inky 相当）。
///   Shy       … 距離 8 以上なら直接追跡、未満なら自コーナーへ逃げる（Clyde 相当）。
/// </remarks>
public class GhostMover : MonoBehaviour
{
    #region 定義

    /// <summary>チェイス時の AI 種別</summary>
    public enum ChaseType
    {
        Direct,     // 直接追跡（Blinky）
        LookAhead,  // 先回り  （Pinky）
        Mirror,     // 回り込み（Inky）
        Shy,        // 臆病    （Clyde）
    }

    /// <summary>ゴーストの行動モード</summary>
    public enum GhostMode
    {
        Chase,      // 通常追跡
        Frightened, // 弱体化ランダム
    }

    [Header("AI 設定")]
    [SerializeField] private ChaseType _chaseType;

    [Tooltip("Shy タイプが逃げ込む自コーナーのタイル座標")]
    [SerializeField] private Vector2Int _cornerTile;

    [Header("参照")]
    [SerializeField] private B_MazeGenerator _mazeGenerator;
    [SerializeField] private B_PacManMover _pacManMover;

    [Header("速度")]
    [SerializeField] private float _baseSpeed = 9.47f;
    [SerializeField] private float _normalRate = 0.75f;
    [SerializeField] private float _frightenedRate = 0.25f;

    [Header("スポーン")]
    [SerializeField] private Vector2Int _spawnTile;

    [Tooltip("ゴーストハウス内にスポーンするゴーストは ON にする。ドアを一度通過するまで通行を許可する")]
    [SerializeField] private bool _startsInsideHouse;

    [Header("復活")]
    [Tooltip("食べられてからスポーン位置に復活するまでの秒数")]
    [SerializeField] private float _respawnDelay = 3.0f;

    // BFS の隣接探索順: 上 > 左 > 下 > 右
    private static readonly Vector2Int[] DirPriority =
    {
        new Vector2Int( 0, -1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 1,  0),
    };

    private Vector2Int _currentDir;
    private Vector2Int _targetTile;

    private bool _isEaten;
    private float _respawnTimer;
    private bool _hasPassedDoor; // ドアを通過済みなら true（再侵入防止）
    private Vector2Int _doorTile; // ゴーストドアのタイル座標（Initialize 時にキャッシュ）

    // ── デバッグ描画 ─────────────────────────────────────
    [Header("デバッグ描画")]
    [SerializeField] private bool _showDebugDraw = true;

    private Vector2Int _debugAiTarget; // AI が狙っているタイル（描画用キャッシュ）

    // ── マテリアル / ビジュアル ──────────────────────────
    private MaterialPropertyBlock _propBlock;
    private bool _isFlashing; // 点滅フラグ（フライテンド警告フェーズ）

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly Color FrightenedColor = new(0.10f, 0.20f, 1.00f); // 青
    private static readonly Color FlashColorA     = new(0.10f, 0.20f, 1.00f); // 青（点滅A）
    private static readonly Color FlashColorB     = new(1.00f, 1.00f, 1.00f); // 白（点滅B）

    private Collider _col;
    private Renderer _rend;

    #endregion

    #region 公開プロパティ

    /// <summary>現在の行動モード。</summary>
    public GhostMode CurrentMode { get; private set; }

    /// <summary>現在のタイル座標。</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>食べられていない状態のとき true。</summary>
    public bool IsAlive => !_isEaten;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// スポーン位置に配置し Chase モードで開始します。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void Initialize()
    {
        _isEaten = false;
        _respawnTimer = 0f;
        CurrentMode = GhostMode.Chase;
        CurrentTile = _spawnTile;
        _targetTile = _spawnTile;
        _currentDir = Vector2Int.zero;
        _hasPassedDoor = !_startsInsideHouse; // ハウス外スポーンは最初からドアを封鎖
        CacheDoorTile();                      // ドア座標をキャッシュ

        // フライテンド中に Initialize が呼ばれる場合（死亡リスポーン等）に備え
        // 点滅フラグとマテリアルカラーを必ずリセットする
        _isFlashing = false;
        ResetMaterialColor();

        transform.position = _mazeGenerator.TileToWorld(_spawnTile);

        if (_col != null) _col.enabled = true;
        if (_rend != null) _rend.enabled = true;

        SetNextTarget();
    }

    /// <summary>
    /// フライテンドモードに切り替えます。可能なら現在方向を即時反転します（原作仕様）。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void SetFrightened()
    {
        if (_isEaten) return;

        // 新規フライテンドのときだけ方向反転
        if (CurrentMode != GhostMode.Frightened)
        {
            Vector2Int reversed = -_currentDir;
            if (_currentDir != Vector2Int.zero && IsPassable(CurrentTile + reversed))
            {
                _currentDir = reversed;
                _targetTile = CurrentTile + reversed;
                UpdateFacingDir();
            }
            CurrentMode = GhostMode.Frightened;
        }

        // 再エナジャイザー時も含め、常に点滅を止めて青に戻す
        _isFlashing = false;
        SetMaterialColor(FrightenedColor);
    }

    /// <summary>
    /// フライテンド警告フェーズ（残り数秒）を開始します。青↔白の点滅に切り替えます。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void SetFrightenedWarning()
    {
        if (_isEaten || CurrentMode != GhostMode.Frightened) return;
        _isFlashing = true;
    }

    /// <summary>
    /// フライテンドモードを解除して Chase モードへ戻します。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void ExitFrightened()
    {
        if (_isEaten || CurrentMode != GhostMode.Frightened) return;
        CurrentMode = GhostMode.Chase;
        _isFlashing = false;
        ResetMaterialColor();
        SetNextTarget();
    }

    /// <summary>
    /// パックマンに食べられた処理を行います。
    /// コライダーと描画を無効化し、_respawnDelay 秒後にスポーン位置へ復活します。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void OnEatenByPacMan()
    {
        if (_isEaten) return;

        _isEaten    = true;
        _isFlashing = false;
        _respawnTimer = _respawnDelay;
        ResetMaterialColor();

        if (_col  != null) _col.enabled  = false;
        if (_rend != null) _rend.enabled = false;
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        _col      = GetComponent<Collider>();
        _rend     = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        // フライテンド警告フェーズ: 0.2 秒ごとに青↔白を交互に点滅
        if (_isFlashing)
        {
            bool showBlue = ((int)(Time.time / 0.2f) % 2) == 0;
            SetMaterialColor(showBlue ? FlashColorA : FlashColorB);
        }
    }

    private void FixedUpdate()
    {
        if (_isEaten)
        {
            _respawnTimer -= Time.fixedDeltaTime;
            if (_respawnTimer <= 0f)
                Initialize();
            return;
        }

        MoveStep();

        if (_showDebugDraw) DrawDebugLines();
    }

    // 移動処理
    private void MoveStep()
    {
        Vector3 targetWorld = _mazeGenerator.TileToWorld(_targetTile);
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);

        float speed = _baseSpeed * (CurrentMode == GhostMode.Frightened ? _frightenedRate : _normalRate);
        float step = speed * Time.fixedDeltaTime;
        float dist = Vector3.Distance(flatPos, targetWorld);

        if (dist <= step)
        {
            transform.position = targetWorld;
            CurrentTile = _targetTile;
            HandleTunnelWarp();

            // ドアタイルを通過した瞬間に封鎖（以降はハウスへ再侵入不可）
            if (!_hasPassedDoor && _mazeGenerator.MazeData.GetTile(CurrentTile) == SO_MazeData.TileType.GhostDoor)
                _hasPassedDoor = true;

            SetNextTarget();
        }
        else
        {
            transform.position += (targetWorld - transform.position).normalized * step;
        }
    }

    private void SetNextTarget()
    {
        Vector2Int uTurn = -_currentDir; // Uターン禁止方向（現在の逆）

        if (!_hasPassedDoor)
        {
            // ── ハウス内: ドアタイルへ BFS で確実に向かう ──────────
            _currentDir    = BfsFirstStep(_doorTile);
            _debugAiTarget = _doorTile;
        }
        else
        {
            // ── ハウス外: 通常 AI ──────────────────────────────────
            bool useRandom = CurrentMode == GhostMode.Frightened
                             || (_chaseType == ChaseType.Shy && !IsFarFromPacMan());

            if (useRandom)
            {
                _currentDir    = DecideRandDir(uTurn);
                _debugAiTarget = CurrentTile + _currentDir; // ランダム時は直近の一歩
            }
            else
            {
                Vector2Int chaseTarget = GetChaseTarget();
                _debugAiTarget = chaseTarget;
                _currentDir    = BfsFirstStep(chaseTarget);
            }
        }

        // BFS が Uターン方向を返した場合、他の選択肢があれば上書き
        if (_currentDir == uTurn)
            _currentDir = DecideRandDir(uTurn);

        // 無効方向（zero・壁）フォールバック
        if (_currentDir == Vector2Int.zero || !IsPassable(CurrentTile + _currentDir))
            _currentDir = DecideRandDir(uTurn);

        UpdateFacingDir();

        Vector2Int next = CurrentTile + _currentDir;

        // トンネル境界越え
        if ((next.x < 0 || next.x >= SO_MazeData.Cols) &&
            _mazeGenerator.MazeData.GetTile(CurrentTile) == SO_MazeData.TileType.Tunnel)
        {
            _targetTile = next;
            return;
        }

        _targetTile = next;
    }


    private Vector2Int GetChaseTarget()
    {
        if (_pacManMover == null) return CurrentTile;

        return _chaseType switch
        {
            ChaseType.Direct    => _pacManMover.CurrentTile,
            ChaseType.LookAhead => _pacManMover.CurrentTile + _pacManMover.CurrentDir * 3,
            
            // 迷路外に出ないよう ClampToMaze でクランプする
            ChaseType.Mirror    => ClampToMaze(_pacManMover.CurrentTile * 2 - CurrentTile),
            
            // Shy: 近距離時は SetNextTarget でランダム徘徊に切り替えるため常にパックマンを返す
            ChaseType.Shy       => _pacManMover.CurrentTile,
            _                   => _pacManMover.CurrentTile,
        };
    }

    private bool IsFarFromPacMan()
    {
        const float Threshold = 8f;
        float dx = CurrentTile.x - _pacManMover.CurrentTile.x;
        float dz = CurrentTile.y - _pacManMover.CurrentTile.y;
        return dx * dx + dz * dz >= Threshold * Threshold;
    }

    /// <summary>
    /// BFS で goal への最短経路を探索し、最初の一歩の方向を返します。
    /// goal に到達済みまたは経路なしの場合は現在方向を継続します。
    /// </summary>
    private Vector2Int BfsFirstStep(Vector2Int goal)
    {
        if (CurrentTile == goal) return _currentDir;

        // parent[tile] = そのタイルへ来た一手前のタイル（CurrentTile は自己参照で番兵）
        var parent = new Dictionary<Vector2Int, Vector2Int> { [CurrentTile] = CurrentTile };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(CurrentTile);

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            foreach (Vector2Int dir in DirPriority)
            {
                Vector2Int next = cur + dir;
                if (parent.ContainsKey(next) || !IsPassable(next))
                {
                    continue;
                }

                parent[next] = cur;

                if (next == goal)
                {
                    // goal から CurrentTile まで親を辿り、最初の一歩を特定する
                    Vector2Int step = goal;
                    while (parent[step] != CurrentTile)
                    {
                        step = parent[step];
                    }

                    return step - CurrentTile;
                }

                queue.Enqueue(next);
            }
        }

        return _currentDir; // 経路なし
    }

    /// <summary>
    /// 通行可能な方向からランダムに一方向を選びます。
    /// exclude に指定した方向は候補から除外します（Uターン禁止に使用）。
    /// 候補がゼロになる場合（行き止まり）は除外を解除して再選択します。
    /// </summary>
    private Vector2Int DecideRandDir(Vector2Int exclude = default)
    {
        var candidates = new List<Vector2Int>(4);
        foreach (Vector2Int dir in DirPriority)
        {
            if (dir == exclude) continue;
            if (IsPassable(CurrentTile + dir))
                candidates.Add(dir);
        }

        // 行き止まりなら Uターン禁止を解除して再選択
        if (candidates.Count == 0)
        {
            foreach (Vector2Int dir in DirPriority)
                if (IsPassable(CurrentTile + dir))
                    candidates.Add(dir);
        }

        return candidates.Count > 0
            ? candidates[Random.Range(0, candidates.Count)]
            : _currentDir;
    }

    /// <summary>
    /// 迷路を走査してゴーストドアのタイル座標を _doorTile にキャッシュします。
    /// ドアが見つからない場合は _spawnTile をフォールバックに使います。
    /// </summary>
    private void CacheDoorTile()
    {
        for (int row = 0; row < SO_MazeData.Rows; row++)
        {
            for (int col = 0; col < SO_MazeData.Cols; col++)
            {
                if (_mazeGenerator.MazeData.GetTile(col, row) == SO_MazeData.TileType.GhostDoor)
                {
                    _doorTile = new Vector2Int(col, row);
                    return;
                }
            }
        }
        _doorTile = _spawnTile; // fallback
    }

    /// <summary>タイル座標を迷路の有効範囲内にクランプします。</summary>
    private static Vector2Int ClampToMaze(Vector2Int tile) =>
        new(Mathf.Clamp(tile.x, 0, SO_MazeData.Cols - 1),
            Mathf.Clamp(tile.y, 0, SO_MazeData.Rows - 1));

    // 通れるか（迷路外・壁・ドア封鎖を考慮）
    private bool IsPassable(Vector2Int tile)
    {
        if (tile.x < 0 || tile.x >= SO_MazeData.Cols ||
            tile.y < 0 || tile.y >= SO_MazeData.Rows)
            return false;
        if (!_mazeGenerator.MazeData.IsPassableForGhost(tile.x, tile.y)) return false;

        // ドア通過済みならゴーストドアを封鎖（ハウスへの再侵入防止）
        if (_hasPassedDoor &&
            _mazeGenerator.MazeData.GetTile(tile) == SO_MazeData.TileType.GhostDoor)
            return false;

        return true;
    }

    private void HandleTunnelWarp()
    {
        if (CurrentTile.x < 0)
        {
            CurrentTile = new Vector2Int(SO_MazeData.Cols - 1, CurrentTile.y);
            _targetTile = CurrentTile;
            transform.position = _mazeGenerator.TileToWorld(CurrentTile);
        }
        else if (CurrentTile.x >= SO_MazeData.Cols)
        {
            CurrentTile = new Vector2Int(0, CurrentTile.y);
            _targetTile = CurrentTile;
            transform.position = _mazeGenerator.TileToWorld(CurrentTile);
        }
    }

    private void UpdateFacingDir()
    {
        if (_currentDir == Vector2Int.zero) return;
        transform.rotation = Quaternion.LookRotation(
            new Vector3(_currentDir.x, 0f, _currentDir.y));
    }

    // ─────────────────────────────────────────────────────────────
    //  マテリアル制御
    // ─────────────────────────────────────────────────────────────

    /// <summary>MaterialPropertyBlock でマテリアルカラーを上書きします（共有マテリアル非破壊）。</summary>
    private void SetMaterialColor(Color color)
    {
        if (_rend == null || _propBlock == null) return;
        _propBlock.SetColor(BaseColorId, color);
        _rend.SetPropertyBlock(_propBlock);
    }

    /// <summary>PropertyBlock を除去してマテリアル元のカラーに戻します。</summary>
    private void ResetMaterialColor()
    {
        if (_rend == null) return;
        _rend.SetPropertyBlock(null);
    }

    // ─────────────────────────────────────────────────────────────
    //  デバッグ描画
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Game ビュー・Scene ビュー両方に見える Debug.DrawLine でデバッグラインを描画します。
    /// FixedUpdate から毎フレーム呼ばれます。
    /// </summary>
    private void DrawDebugLines()
    {
        if (_mazeGenerator == null) return;

        // Y をわずかに上げて床面と重ならないようにする
        const float Y = 0.08f;
        Vector3 origin     = new(transform.position.x, Y, transform.position.z);
        Vector3 aiTarget   = _mazeGenerator.TileToWorld(_debugAiTarget);
        Vector3 nextTarget = _mazeGenerator.TileToWorld(_targetTile);
        aiTarget.y   = Y;
        nextTarget.y = Y;

        Color ghostColor = GizmoColor();

        // AI ターゲットへの線（ゴースト固有カラー）
        // フライテンド中・ハウス内は青/黄で状態を区別する
        Color lineColor = !_hasPassedDoor         ? Color.yellow
                        : CurrentMode == GhostMode.Frightened ? new Color(0.3f, 0.5f, 1f)
                        : ghostColor;

        Debug.DrawLine(origin, aiTarget, lineColor, Time.fixedDeltaTime);

        // 即時ターゲット（次タイル）への短い白線
        Debug.DrawLine(origin, nextTarget, new Color(1f, 1f, 1f, 0.4f), Time.fixedDeltaTime);

        // AI ターゲット位置に小さな十字（X・Z 軸）
        float cross = 0.18f;
        Debug.DrawLine(aiTarget + new Vector3(-cross, 0, 0),
                       aiTarget + new Vector3( cross, 0, 0), lineColor, Time.fixedDeltaTime);
        Debug.DrawLine(aiTarget + new Vector3(0, 0, -cross),
                       aiTarget + new Vector3(0, 0,  cross), lineColor, Time.fixedDeltaTime);
    }

    /// <summary>
    /// Scene ビューに Gizmos（球・ワイヤー円）を描画します。
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!_showDebugDraw || _mazeGenerator == null) return;

        Color c = GizmoColor();

        // AI ターゲットに半透明の球
        Gizmos.color = new Color(c.r, c.g, c.b, 0.75f);
        Vector3 aiWorld = _mazeGenerator.TileToWorld(_debugAiTarget);
        aiWorld.y = 0.08f;
        Gizmos.DrawSphere(aiWorld, 0.22f);

        // Shy（Clyde）: 追跡切替の境界円（8 タイル半径）
        if (_chaseType == ChaseType.Shy)
        {
            float tileSize = Vector3.Distance(
                _mazeGenerator.TileToWorld(Vector2Int.zero),
                _mazeGenerator.TileToWorld(Vector2Int.right));
            Gizmos.color = new Color(c.r, c.g, c.b, 0.18f);
            Gizmos.DrawWireSphere(transform.position, 8f * tileSize);
        }
    }

    /// <summary>ChaseType ごとのデバッグカラーを返します。</summary>
    private Color GizmoColor() => _chaseType switch
    {
        ChaseType.Direct    => new Color(1.0f, 0.2f, 0.2f), // 赤   (Blinky)
        ChaseType.LookAhead => new Color(1.0f, 0.5f, 0.8f), // ピンク(Pinky)
        ChaseType.Mirror    => new Color(0.3f, 0.9f, 1.0f), // シアン(Inky)
        ChaseType.Shy       => new Color(1.0f, 0.6f, 0.1f), // 橙   (Clyde)
        _                   => Color.white,
    };

    #endregion
}
