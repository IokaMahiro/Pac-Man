using UnityEngine;

/// <summary>
/// 迷路のタイルデータ・スポーン位置・スキャッターターゲットを保持するScriptableObject。
/// 迷路レイアウトは文字列配列(_rows)で定義し、ParseGrid()で TileType[,] に変換して使用する。
/// </summary>
/// <remarks>
/// 文字凡例（_rows内）:
///   #  … 壁 (Wall)
///   .  … 小ドット (Dot)
///   o  … エナジャイザー (Energizer)
///   (スペース) … 通路 (Path)
///   G  … ゴーストハウス内部 (GhostHouse)
///   -  … ゴーストハウスドア (GhostDoor)
///   T  … トンネル出入口 (Tunnel)
/// </remarks>
[CreateAssetMenu(fileName = "SO_MazeData", menuName = "PacMan/MazeData")]
public class SO_MazeData : ScriptableObject
{
    #region 定義

    /// <summary>タイルの種別</summary>
    public enum TileType
    {
        Wall,        // 壁
        Dot,         // 小ドット（10点）
        Energizer,   // エナジャイザー（50点）
        Path,        // 通路（空）
        GhostHouse,  // ゴーストハウス内部
        GhostDoor,   // ゴーストハウスドア（ゴーストのみ通過可）
        Tunnel,      // トンネル（ワープ通路）
    }

    /// <summary>迷路の列数（横タイル数）</summary>
    public const int Cols = 28;

    /// <summary>迷路の行数（縦タイル数）</summary>
    public const int Rows = 27;

    // -------------------------------------------------------
    // 迷路レイアウト（28列 × 27行）
    // 参考: The Pac-Man Dossier / anonimo0611 氏解説サイト
    // ※ 実際のアーケード版は31行。本実装は簡略版のため
    //    スポーン座標は Unity Inspector 上で確認・調整すること。
    // -------------------------------------------------------
    [Header("迷路レイアウト（28文字 × 27行）")]
    [SerializeField] private string[] _rows = new string[]
    {
        "############################", // 0  上端
        "#............##............#", // 1
        "#.####.#####.##.#####.####.#", // 2
        "#o####.#####.##.#####.####o#", // 3  エナジャイザー（col 1, 26）
        "#.####.#####.##.#####.####.#", // 4
        "#..........................#", // 5  上部横通路
        "#.####.##.########.##.####.#", // 6
        "#.####.##.########.##.####.#", // 7
        "#......##....##....##......#", // 8
        "######.#####.##.#####.######", // 9  T字路上
        "######.#####.##.#####.######", // 10
        "######.##          ##.######", // 11 ゴーストハウス上部開放エリア
        "######.## ###--### ##.######", // 12 ゴーストハウス天井・ドア（col 13-14）
        "T     .   #GGGGGG#   .     T", // 13 トンネル行 / ゴーストハウス内部
        "######.## #GGGGGG# ##.######", // 14 ゴーストハウス内部（2行目）
        "######.## ######## ##.######", // 15 ゴーストハウス床
        "######.##          ##.######", // 16 ゴーストハウス下部開放エリア
        "#............##............#", // 17 下部メイン横通路
        "#.####.#####.##.#####.####.#", // 18
        "#o..##................##..o#", // 19 エナジャイザー（col 1, 26）
        "###.##.##.########.##.##.###", // 20
        "###.##.##.########.##.##.###", // 21
        "#......##....##....##......#", // 22
        "#.##########.##.##########.#", // 23
        "#.##########.##.##########.#", // 24
        "#..........................#", // 25 下部横通路（パックマン初期位置付近）
        "############################", // 26 下端
    };

    [Header("スポーン位置（タイル座標 col, row）")]
    [Tooltip("パックマンの初期タイル座標。迷路レイアウトに合わせて調整してください。")]
    [SerializeField] private Vector2Int _pacManSpawn  = new Vector2Int(13, 25);

    [Tooltip("ブリンキーの初期タイル座標（ゴーストハウス外・上中央）。")]
    [SerializeField] private Vector2Int _blinkySpawn  = new Vector2Int(13, 11);

    [Tooltip("ピンキーの初期タイル座標（ゴーストハウス内・中央）。")]
    [SerializeField] private Vector2Int _pinkySpawn   = new Vector2Int(13, 13);

    [Tooltip("インキーの初期タイル座標（ゴーストハウス内・左）。")]
    [SerializeField] private Vector2Int _inkySpawn    = new Vector2Int(11, 13);

    [Tooltip("クライドの初期タイル座標（ゴーストハウス内・右）。")]
    [SerializeField] private Vector2Int _clydeSpawn   = new Vector2Int(15, 13);

    [Header("スキャッターターゲット（迷路外の到達不能タイル座標）")]
    [SerializeField] private Vector2Int _blinkyScatterTarget = new Vector2Int(27, -3); // 右上
    [SerializeField] private Vector2Int _pinkyScatterTarget  = new Vector2Int( 0, -3); // 左上
    [SerializeField] private Vector2Int _inkyScatterTarget   = new Vector2Int(27, 29); // 右下
    [SerializeField] private Vector2Int _clydeScatterTarget  = new Vector2Int( 0, 29); // 左下

    // パース済みグリッドのキャッシュ
    private TileType[,] _parsedGrid;

    #endregion

    #region 公開メソッド

    /// <summary>
    /// 指定タイル座標のタイル種別を返します。
    /// 範囲外の場合は Wall を返します。
    /// </summary>
    public TileType GetTile(int col, int row)
    {
        if (_parsedGrid == null) ParseGrid();
        if (col < 0 || col >= Cols || row < 0 || row >= Rows) return TileType.Wall;
        return _parsedGrid[col, row];
    }

    /// <summary>
    /// 指定タイル座標のタイル種別を返します（Vector2Int 版）。
    /// </summary>
    public TileType GetTile(Vector2Int tile) => GetTile(tile.x, tile.y);

    /// <summary>
    /// 指定タイルがパックマンにとって通行可能かを返します。
    /// Wall および GhostDoor は通行不可です。
    /// </summary>
    public bool IsPassableForPacMan(int col, int row)
    {
        TileType t = GetTile(col, row);
        return t != TileType.Wall && t != TileType.GhostDoor;
    }

    /// <summary>
    /// 指定タイルがゴーストにとって通行可能かを返します。
    /// Wall のみ通行不可です（GhostDoor はゴーストが通過できます）。
    /// </summary>
    public bool IsPassableForGhost(int col, int row)
    {
        return GetTile(col, row) != TileType.Wall;
    }

    /// <summary>パックマンの初期タイル座標</summary>
    public Vector2Int PacManSpawn          => _pacManSpawn;

    /// <summary>ブリンキーの初期タイル座標</summary>
    public Vector2Int BlinkySpawn          => _blinkySpawn;

    /// <summary>ピンキーの初期タイル座標</summary>
    public Vector2Int PinkySpawn           => _pinkySpawn;

    /// <summary>インキーの初期タイル座標</summary>
    public Vector2Int InkySpawn            => _inkySpawn;

    /// <summary>クライドの初期タイル座標</summary>
    public Vector2Int ClydeSpawn           => _clydeSpawn;

    /// <summary>ブリンキーのスキャッターターゲット</summary>
    public Vector2Int BlinkyScatterTarget  => _blinkyScatterTarget;

    /// <summary>ピンキーのスキャッターターゲット</summary>
    public Vector2Int PinkyScatterTarget   => _pinkyScatterTarget;

    /// <summary>インキーのスキャッターターゲット</summary>
    public Vector2Int InkyScatterTarget    => _inkyScatterTarget;

    /// <summary>クライドのスキャッターターゲット</summary>
    public Vector2Int ClydeScatterTarget   => _clydeScatterTarget;

    #endregion

    #region 非公開メソッド

    private void OnValidate()
    {
        // Inspector 上で_rowsを変更したときキャッシュを破棄して再パースを促す
        _parsedGrid = null;
    }

    private void ParseGrid()
    {
        _parsedGrid = new TileType[Cols, Rows];

        for (int row = 0; row < Rows; row++)
        {
            string line = (row < _rows.Length) ? _rows[row] : string.Empty;

            for (int col = 0; col < Cols; col++)
            {
                char c = (col < line.Length) ? line[col] : '#';
                _parsedGrid[col, row] = CharToTileType(c);
            }
        }
    }

    private static TileType CharToTileType(char c)
    {
        return c switch
        {
            '#' => TileType.Wall,
            '.' => TileType.Dot,
            'o' => TileType.Energizer,
            'G' => TileType.GhostHouse,
            '-' => TileType.GhostDoor,
            'T' => TileType.Tunnel,
            _   => TileType.Path,   // スペース・その他は通路扱い
        };
    }

    #endregion
}
