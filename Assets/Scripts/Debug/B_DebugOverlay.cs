using UnityEngine;

/// <summary>
/// ゴーストの状態と AI ターゲットをゲームビューにオーバーレイ表示するデバッグコンポーネント。
/// </summary>
/// <remarks>
/// 提供する情報:
///   2-A ゴーストステート一覧 … Ghost / Mode / Tile / AITarget をリアルタイム表示。
///                               モード遷移バグの第一発見手段として使用する。
///   1-B ゴーストターゲット表示は Scene ビューの Gizmo（BaseGhost.OnDrawGizmos）で確認する。
///
/// 操作:
///   _toggleKey（デフォルト F1）でオーバーレイの表示/非表示を切り替える。
///
/// セットアップ:
///   シーン内の任意の空 GameObject にアタッチし、Inspector で 4 体のゴーストと
///   B_PacManMover を設定する。
/// </remarks>
public class B_DebugOverlay : MonoBehaviour
{
    #region 定義

    [Header("参照")]
    [SerializeField] private B_BlinkyAI    _blinky;
    [SerializeField] private B_PinkyAI     _pinky;
    [SerializeField] private B_InkyAI      _inky;
    [SerializeField] private B_ClydeAI     _clyde;
    [SerializeField] private B_PacManMover _pacManMover;

    [Header("表示設定")]
    [Tooltip("ゲーム開始時にオーバーレイを表示するか")]
    [SerializeField] private bool    _visibleOnStart = true;

    [Tooltip("表示/非表示の切り替えキー")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

    // ── ゴーストメタデータ ────────────────────
    private BaseGhost[] _ghosts;

    private static readonly string[] GhostNames =
        { "Blinky", "Pinky", "Inky", "Clyde" };

    private static readonly Color[] GhostColors =
    {
        new Color(1.00f, 0.20f, 0.20f), // Blinky: 赤
        new Color(1.00f, 0.50f, 0.80f), // Pinky:  ピンク
        new Color(0.30f, 0.90f, 1.00f), // Inky:   シアン
        new Color(1.00f, 0.60f, 0.10f), // Clyde:  オレンジ
    };

    private static readonly Color[] ModeColors =
    {
        new Color(0.50f, 0.50f, 0.50f), // House
        new Color(1.00f, 0.90f, 0.20f), // ExitHouse
        new Color(0.30f, 0.90f, 0.30f), // Scatter
        new Color(1.00f, 0.40f, 0.40f), // Chase
        new Color(0.40f, 0.55f, 1.00f), // Frightened
        new Color(0.70f, 0.70f, 0.70f), // Dead
    };

    private static readonly string[] ModeLabels =
        { "House", "ExitHouse", "Scatter", "Chase", "Frightened", "Dead" };

    // ── 表示状態 ─────────────────────────────
    private bool     _visible;
    private GUIStyle _style;
    private GUIStyle _boldStyle;

    // レイアウト定数
    private const float PanelX     = 10f;
    private const float PanelY     = 10f;
    private const float PanelW     = 295f;
    private const float LineH      = 22f;
    private const float ColName    = PanelX + 8f;
    private const float ColMode    = PanelX + 72f;
    private const float ColTile    = PanelX + 160f;
    private const float ColTarget  = PanelX + 225f;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        _ghosts  = new BaseGhost[] { _blinky, _pinky, _inky, _clyde };
        _visible = _visibleOnStart;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey))
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible || !Application.isPlaying) return;

        EnsureStyles();

        // 行数: ヘッダー2行 + ゴースト4行 + セパレータ1行 + PacMan1行
        int   rows   = 2 + _ghosts.Length + 2;
        float panelH = LineH * rows + 14f;

        // 半透明背景
        GUI.color = new Color(0f, 0f, 0f, 0.68f);
        GUI.DrawTexture(new Rect(PanelX, PanelY, PanelW, panelH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cy = PanelY + 7f;

        // ── タイトル行 ────────────────────────
        GUI.color = Color.white;
        GUI.Label(new Rect(ColName, cy, PanelW - 16f, LineH),
                  $"[{_toggleKey}] GHOST DEBUG", _boldStyle);
        cy += LineH;

        // ── 列ヘッダー ─────────────────────────
        GUI.color = new Color(0.75f, 0.75f, 0.75f);
        GUI.Label(new Rect(ColName,   cy, 62f, LineH), "Ghost",    _style);
        GUI.Label(new Rect(ColMode,   cy, 86f, LineH), "Mode",     _style);
        GUI.Label(new Rect(ColTile,   cy, 62f, LineH), "Tile",     _style);
        GUI.Label(new Rect(ColTarget, cy, 62f, LineH), "AI Goal",  _style);
        cy += LineH;

        // ── ゴースト行 ─────────────────────────
        for (int i = 0; i < _ghosts.Length; i++)
        {
            BaseGhost ghost = _ghosts[i];
            if (ghost == null) continue;

            BaseGhost.GhostMode mode    = ghost.CurrentMode;
            int                 modeIdx = (int)mode;

            // Ghost 名（ゴーストカラー）
            GUI.color = GhostColors[i];
            GUI.Label(new Rect(ColName, cy, 62f, LineH), GhostNames[i], _style);

            // Mode（モードカラー）
            GUI.color = modeIdx < ModeColors.Length ? ModeColors[modeIdx] : Color.white;
            string modeLabel = modeIdx < ModeLabels.Length ? ModeLabels[modeIdx] : "?";
            GUI.Label(new Rect(ColMode, cy, 86f, LineH), modeLabel, _style);

            // Tile（現在タイル）
            GUI.color = Color.white;
            Vector2Int tile = ghost.CurrentTile;
            GUI.Label(new Rect(ColTile, cy, 62f, LineH), $"({tile.x},{tile.y})", _style);

            // AI Goal
            bool hasGoal = mode != BaseGhost.GhostMode.House &&
                           mode != BaseGhost.GhostMode.Frightened;
            if (hasGoal)
            {
                Vector2Int goal = ghost.InternalDebugAiGoal;
                GUI.color = new Color(0.90f, 0.90f, 0.60f);
                GUI.Label(new Rect(ColTarget, cy, 62f, LineH), $"({goal.x},{goal.y})", _style);
            }
            else
            {
                GUI.color = new Color(0.45f, 0.45f, 0.45f);
                GUI.Label(new Rect(ColTarget, cy, 62f, LineH), "—", _style);
            }

            cy += LineH;
        }

        // ── セパレータ + PacMan 行 ──────────────
        GUI.color = new Color(0.45f, 0.45f, 0.45f);
        GUI.Label(new Rect(ColName, cy, PanelW - 16f, LineH),
                  "─────────────────────────────", _style);
        cy += LineH;

        if (_pacManMover != null)
        {
            Vector2Int pacTile = _pacManMover.CurrentTile;
            Vector2Int pacDir  = _pacManMover.CurrentDir;
            string     dirStr  = pacDir == Vector2Int.zero
                                 ? "STOP"
                                 : $"({pacDir.x:+0;-0},{pacDir.y:+0;-0})";

            GUI.color = new Color(1.0f, 1.0f, 0.0f);
            GUI.Label(new Rect(ColName,  cy, 62f,  LineH), "PacMan", _style);
            GUI.color = Color.white;
            GUI.Label(new Rect(ColMode,  cy, 86f,  LineH), dirStr,   _style);
            GUI.Label(new Rect(ColTile,  cy, 62f,  LineH), $"({pacTile.x},{pacTile.y})", _style);
        }
    }

    /// <summary>GUIStyle を初回 OnGUI 時に生成します（コンストラクタでは生成不可）。</summary>
    private void EnsureStyles()
    {
        if (_style != null) return;

        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal,
        };
        _boldStyle = new GUIStyle(_style)
        {
            fontStyle = FontStyle.Bold,
        };
    }

    #endregion
}
