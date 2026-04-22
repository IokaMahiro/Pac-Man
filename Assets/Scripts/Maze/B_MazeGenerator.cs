using UnityEngine;

/// <summary>
/// SO_MazeData を元に迷路タイルを生成し、タイル座標とワールド座標の相互変換を提供するビヘイビア。
/// 他のスクリプトは本クラスを介して迷路情報にアクセスする。
/// </summary>
/// <remarks>
/// 座標系:
///   タイル (col=0, row=0) が迷路の左上。
///   ワールド空間は X-Z 平面（Y=0）を使用し、迷路中心がワールド原点になる。
///   col 増加方向 = +X（右）、row 増加方向 = +Z（奥／画面下方向）。
///
/// Prefab 要件:
///   _wallPrefab       … BoxCollider 付き静的オブジェクト（Rigidbody 不要）。
///                       Physics Material は摩擦0・反発0 を推奨。
///   _dotPrefab        … SphereCollider(IsTrigger) 付きオブジェクト。
///   _energizerPrefab  … SphereCollider(IsTrigger) 付きオブジェクト。
///   _ghostDoorPrefab  … BoxCollider(IsTrigger) 付きオブジェクト。
///                       ゴースト専用レイヤーで当たり判定を制御する。
/// </remarks>
public class B_MazeGenerator : MonoBehaviour
{
    #region 定義

    [Header("迷路データ")]
    [SerializeField] private SO_MazeData _mazeData;

    [Header("タイル Prefab")]
    [SerializeField] private GameObject _wallPrefab;
    [SerializeField] private GameObject _dotPrefab;
    [SerializeField] private GameObject _energizerPrefab;
    [SerializeField] private GameObject _ghostDoorPrefab;

    [Header("タイルサイズ（Unity ユニット）")]
    [SerializeField] private float _tileSize = 1.0f;

    // 生成済みタイル GameObject のキャッシュ（ドットの動的削除などに使用）
    private GameObject[,] _tileObjects;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// アタッチされている迷路データを返します。
    /// 他スクリプトがタイル種別を問い合わせる際に使用してください。
    /// </summary>
    public SO_MazeData MazeData => _mazeData;

    /// <summary>
    /// タイル座標をワールド座標（X-Z 平面）に変換します。
    /// 迷路の中心がワールド原点 (0, 0, 0) になります。
    /// </summary>
    /// <param name="col">タイル列（0 が左端）</param>
    /// <param name="row">タイル行（0 が上端）</param>
    public Vector3 TileToWorld(int col, int row)
    {
        float x =  (col - SO_MazeData.Cols * 0.5f + 0.5f) * _tileSize;
        float z = -(row - SO_MazeData.Rows * 0.5f + 0.5f) * _tileSize; // row 増加 = 画面下 = -Z
        return new Vector3(x, 0f, z);
    }

    /// <summary>
    /// タイル座標をワールド座標に変換します（Vector2Int 版）。
    /// </summary>
    public Vector3 TileToWorld(Vector2Int tile) => TileToWorld(tile.x, tile.y);

    /// <summary>
    /// ワールド座標を最近接タイル座標に変換します。
    /// </summary>
    public Vector2Int WorldToTile(Vector3 worldPos)
    {
        int col = Mathf.RoundToInt( worldPos.x / _tileSize + SO_MazeData.Cols * 0.5f - 0.5f);
        int row = Mathf.RoundToInt(-worldPos.z / _tileSize + SO_MazeData.Rows * 0.5f - 0.5f); // Z 反転
        return new Vector2Int(col, row);
    }

    /// <summary>
    /// 指定タイルの GameObject を返します。
    /// ドットを食べたときなど、動的にオブジェクトを削除する際に使用してください。
    /// </summary>
    /// <returns>該当オブジェクト。未生成・範囲外の場合は null。</returns>
    public GameObject GetTileObject(int col, int row)
    {
        if (_tileObjects == null) return null;
        if (col < 0 || col >= SO_MazeData.Cols || row < 0 || row >= SO_MazeData.Rows) return null;
        return _tileObjects[col, row];
    }

    /// <summary>
    /// 指定タイルの GameObject を返します（Vector2Int 版）。
    /// </summary>
    public GameObject GetTileObject(Vector2Int tile) => GetTileObject(tile.x, tile.y);

    /// <summary>
    /// 指定タイルの GameObject を破棄し、キャッシュを null にクリアします。
    /// ドット・エナジャイザーを食べたときに B_PacManMover から呼んでください。
    /// </summary>
    public void RemoveTileObject(int col, int row)
    {
        if (_tileObjects == null) return;
        if (col < 0 || col >= SO_MazeData.Cols || row < 0 || row >= SO_MazeData.Rows) return;
        if (_tileObjects[col, row] == null) return;

        Destroy(_tileObjects[col, row]);
        _tileObjects[col, row] = null;
    }

    /// <summary>
    /// 指定タイルの GameObject を破棄し、キャッシュを null にクリアします（Vector2Int 版）。
    /// </summary>
    public void RemoveTileObject(Vector2Int tile) => RemoveTileObject(tile.x, tile.y);

    /// <summary>
    /// 全タイルオブジェクトを破棄し、迷路を最初から再生成します。
    /// レベルクリア後の新ステージ開始時に B_GameManager から呼んでください。
    /// </summary>
    public void RegenerateTiles()
    {
        if (_tileObjects != null)
        {
            for (int row = 0; row < SO_MazeData.Rows; row++)
            {
                for (int col = 0; col < SO_MazeData.Cols; col++)
                {
                    if (_tileObjects[col, row] != null)
                    {
                        Destroy(_tileObjects[col, row]);
                        _tileObjects[col, row] = null;
                    }
                }
            }
        }

        GenerateMaze();
    }

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        if (_mazeData == null)
        {
            Debug.LogError("[B_MazeGenerator] _mazeData がアタッチされていません。Inspector で設定してください。");
            return;
        }
        GenerateMaze();
    }

    private void GenerateMaze()
    {
        _tileObjects = new GameObject[SO_MazeData.Cols, SO_MazeData.Rows];

        // スポーン位置のタイルにはドットを生成しない（パックマン初期位置）
        Vector2Int pacManSpawn = _mazeData.PacManSpawn;

        for (int row = 0; row < SO_MazeData.Rows; row++)
        {
            for (int col = 0; col < SO_MazeData.Cols; col++)
            {
                SO_MazeData.TileType tileType = _mazeData.GetTile(col, row);

                // パックマンのスポーン位置はドットを生成しない
                if (tileType == SO_MazeData.TileType.Dot && col == pacManSpawn.x && row == pacManSpawn.y)
                {
                    continue;
                }

                GameObject prefab = GetPrefabForTile(tileType);
                if (prefab == null) continue;

                Vector3 worldPos = TileToWorld(col, row);
                GameObject obj   = Instantiate(prefab, worldPos, Quaternion.identity, transform);
                obj.name         = $"{tileType}_{col}_{row}";

                // アニメーターを動的にアタッチ
                if (tileType == SO_MazeData.TileType.Dot)
                    obj.AddComponent<B_DotAnimator>();
                else if (tileType == SO_MazeData.TileType.Energizer)
                    obj.AddComponent<B_EnergizerAnimator>();

                _tileObjects[col, row] = obj;
            }
        }
    }

    private GameObject GetPrefabForTile(SO_MazeData.TileType tileType)
    {
        return tileType switch
        {
            SO_MazeData.TileType.Wall       => _wallPrefab,
            SO_MazeData.TileType.Dot        => _dotPrefab,
            SO_MazeData.TileType.Energizer  => _energizerPrefab,
            SO_MazeData.TileType.GhostDoor  => _ghostDoorPrefab,
            // Path / GhostHouse / Tunnel はビジュアルオブジェクト不要のため null
            _                               => null,
        };
    }

    #endregion
}
