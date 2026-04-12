using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// パックマンのグリッド整合移動を制御するビヘイビア。
/// Rigidbody.MovePosition() を用い、タイル中心を経由して移動する。
/// </summary>
/// <remarks>
/// 移動ロジック概要:
///   1. Update() で入力を _bufferedDir に保存（プリターン対応）。
///   2. FixedUpdate() でタイル中心へ向かって Rigidbody.MovePosition() で移動。
///   3. タイル中心到達時:
///      a. _bufferedDir の方向が通行可能 → 方向確定・次タイルへ
///      b. 不可なら _currentDir を継続
///      c. 両方不可 → 停止
///   4. ドット・エナジャイザー取得はタイル中心到達時のタイル座標チェックで判定。
///      物理トリガーは使用しない。
///
/// Rigidbody 推奨設定:
///   isKinematic = true / useGravity = false
///   Constraints: Freeze Y Position, Freeze Rotation XYZ
/// </remarks>
public class B_PacManMover : MonoBehaviour
{
    #region 定義

    [Header("移動パラメータ")]
    [SerializeField] private float _baseSpeed = 9.47f; // 基準速度: 75.76 px/s ÷ 8 px/tile

    [Header("速度倍率 (Level1 通常: 0.80 / フライテンド中: 0.90)")]
    [SerializeField, Range(0f, 2f)] private float _speedRate = 0.80f;

    [SerializeField] private B_MazeGenerator _mazeGenerator;
    [SerializeField] private Rigidbody       _rb;

    // タイル座標・移動状態
    private Vector2Int _currentTile;   // パックマンが現在いるタイル
    private Vector2Int _targetTile;    // 移動先タイル（到達後に更新）
    private Vector2Int _currentDir;    // 現在の移動方向（タイル単位）
    private Vector2Int _bufferedDir;   // 次の交差点で試みる方向（プリターン用）

    // ドット・エナジャイザー食べ後の停止フレーム残数
    private int _stopFramesRemaining;

    /// <summary>
    /// ドット・エナジャイザーを食べたときに発火するイベント。
    /// 引数 isEnergizer が true のときエナジャイザー取得。
    /// B_DotManager（Step 3）でこのイベントを購読する。
    /// </summary>
    public event Action<bool> OnDotEaten;

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

        Vector3 spawnWorld     = _mazeGenerator.TileToWorld(spawnTile);
        _rb.position           = spawnWorld;
        transform.position     = spawnWorld;
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

    /// <summary>現在のタイル座標を返します。ゴースト衝突判定などに使用します。</summary>
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
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            Debug.LogError("[B_PacManMover] Rigidbody が見つかりません。");
            return;
        }
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
        Vector3 flatPos = new Vector3(_rb.position.x, 0f, _rb.position.z);
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
                _rb.MovePosition(targetWorld);
                _currentTile = _targetTile;

                HandleTunnelWarp();      // トンネル到達時のワープ
                CheckDotAtCurrentTile(); // ドット・エナジャイザーの取得判定
            }

            ChooseNextTile(); // 毎フレーム呼ぶことで、停止中の入力にも即応答
        }
        else
        {
            // ──── タイル中心へ向けて移動 ────
            Vector3 moveDir = (targetWorld - _rb.position).normalized;
            _rb.MovePosition(_rb.position + moveDir * step);
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

    /// <summary>
    /// 現在タイルのドット・エナジャイザーを取得します。
    /// タイルオブジェクトを削除し、停止フレームと OnDotEaten イベントを発火します。
    /// </summary>
    private void CheckDotAtCurrentTile()
    {
        // GetTileObject が null なら食べ済み or 元々ドットなし
        if (_mazeGenerator.GetTileObject(_currentTile.x, _currentTile.y) == null) return;

        SO_MazeData.TileType tileType = _mazeGenerator.MazeData.GetTile(_currentTile);
        if (tileType != SO_MazeData.TileType.Dot && tileType != SO_MazeData.TileType.Energizer) return;

        bool isEnergizer     = tileType == SO_MazeData.TileType.Energizer;
        _stopFramesRemaining = isEnergizer ? 3 : 1;

        // キャッシュを null にクリアしてから Destroy（二重取得を防ぐ）
        _mazeGenerator.RemoveTileObject(_currentTile.x, _currentTile.y);
        OnDotEaten?.Invoke(isEnergizer);
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

    #endregion
}
