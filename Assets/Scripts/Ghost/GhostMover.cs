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

        transform.position = _mazeGenerator.TileToWorld(_spawnTile);

        if (_col != null) _col.enabled = true;
        if (_rend != null) _rend.enabled = true;

        SetNextTarget(); // BFS が初期方向と向きを決定
    }

    /// <summary>
    /// フライテンドモードに切り替えます。可能なら現在方向を即時反転します（原作仕様）。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void SetFrightened()
    {
        if (_isEaten) return;

        Vector2Int reversed = -_currentDir;
        if (_currentDir != Vector2Int.zero && IsPassable(CurrentTile + reversed))
        {
            _currentDir = reversed;
            _targetTile = CurrentTile + reversed;
            UpdateFacingDir();
        }

        CurrentMode = GhostMode.Frightened;
    }

    /// <summary>
    /// フライテンドモードを解除して Chase モードへ戻します。
    /// B_GameManager から呼んでください。
    /// </summary>
    public void ExitFrightened()
    {
        if (_isEaten || CurrentMode != GhostMode.Frightened) return;
        CurrentMode = GhostMode.Chase;
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

        _isEaten = true;
        _respawnTimer = _respawnDelay;

        if (_col != null) _col.enabled = false;
        if (_rend != null) _rend.enabled = false;
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _rend = GetComponent<Renderer>();
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
        _currentDir = CurrentMode == GhostMode.Frightened ? DecideRandDir() : BfsFirstStep(GetChaseTarget());

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
            ChaseType.Direct => _pacManMover.CurrentTile,
            ChaseType.LookAhead => _pacManMover.CurrentTile + _pacManMover.CurrentDir * 3,
            ChaseType.Mirror => _pacManMover.CurrentTile * 2 - CurrentTile,
            ChaseType.Shy => IsFarFromPacMan() ? _pacManMover.CurrentTile : _cornerTile,
            _ => _pacManMover.CurrentTile,
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

    // 弱体化時のランダム移動
    private Vector2Int DecideRandDir()
    {
        var candidates = new List<Vector2Int>(4);
        foreach (Vector2Int dir in DirPriority)
        {
            if (IsPassable(CurrentTile + dir))
                candidates.Add(dir);
        }
        return candidates.Count > 0
            ? candidates[Random.Range(0, candidates.Count)]
            : _currentDir;
    }

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

    #endregion
}
