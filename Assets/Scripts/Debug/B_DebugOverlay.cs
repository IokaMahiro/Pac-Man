using UnityEngine;

/// <summary>
/// ゴーストの状態をゲームビューにオーバーレイ表示するデバッグコンポーネント。
/// </summary>
/// <remarks>
/// _toggleKey（デフォルト F1）でオーバーレイの表示 / 非表示を切り替える。
/// シーン内の任意の空 GameObject にアタッチし、Inspector で 4 体のゴーストと
/// B_PacManMover を設定してください。
/// </remarks>
public class B_DebugOverlay : MonoBehaviour
{
    #region 定義

    [Header("参照")]
    [SerializeField] private GhostMover[]  _ghosts;
    [SerializeField] private B_PacManMover _pacManMover;

    [Header("表示設定")]
    [Tooltip("ゲーム開始時にオーバーレイを表示するか")]
    [SerializeField] private bool    _visibleOnStart = true;

    [Tooltip("表示/非表示の切り替えキー")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

    private static readonly string[] GhostNames  = { "Blinky", "Pinky", "Inky", "Clyde" };

    private static readonly Color[] GhostColors =
    {
        new Color(1.00f, 0.20f, 0.20f), // Blinky: 赤
        new Color(1.00f, 0.50f, 0.80f), // Pinky:  ピンク
        new Color(0.30f, 0.90f, 1.00f), // Inky:   シアン
        new Color(1.00f, 0.60f, 0.10f), // Clyde:  オレンジ
    };

    private static readonly Color[] ModeColors =
    {
        new Color(1.00f, 0.40f, 0.40f), // Chase
        new Color(0.40f, 0.55f, 1.00f), // Frightened
    };

    private static readonly string[] ModeLabels = { "Chase", "Frightened" };

    // ── 表示状態 ─────────────────────────────
    private bool     _visible;
    private GUIStyle _style;
    private GUIStyle _boldStyle;

    // レイアウト定数
    private const float PanelX    = 10f;
    private const float PanelY    = 10f;
    private const float PanelW    = 270f;
    private const float LineH     = 22f;
    private const float ColName   = PanelX + 8f;
    private const float ColMode   = PanelX + 72f;
    private const float ColTile   = PanelX + 160f;
    private const float ColAlive  = PanelX + 220f;

    #endregion

    #region 非公開メソッド

    private void Awake()
    {
        _visible = _visibleOnStart;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey))
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible || !Application.isPlaying || _ghosts == null) return;

        EnsureStyles();

        int   rows   = 2 + _ghosts.Length + 2;
        float panelH = LineH * rows + 14f;

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
        GUI.Label(new Rect(ColName,  cy, 62f, LineH), "Ghost", _style);
        GUI.Label(new Rect(ColMode,  cy, 86f, LineH), "Mode",  _style);
        GUI.Label(new Rect(ColTile,  cy, 58f, LineH), "Tile",  _style);
        GUI.Label(new Rect(ColAlive, cy, 48f, LineH), "State", _style);
        cy += LineH;

        // ── ゴースト行 ─────────────────────────
        for (int i = 0; i < _ghosts.Length; i++)
        {
            GhostMover ghost = _ghosts[i];
            if (ghost == null) continue;

            int modeIdx = (int)ghost.CurrentMode;

            GUI.color = GhostColors[i % GhostColors.Length];
            string name = i < GhostNames.Length ? GhostNames[i] : $"Ghost{i}";
            GUI.Label(new Rect(ColName, cy, 62f, LineH), name, _style);

            GUI.color = modeIdx < ModeColors.Length ? ModeColors[modeIdx] : Color.white;
            string modeLabel = modeIdx < ModeLabels.Length ? ModeLabels[modeIdx] : "?";
            GUI.Label(new Rect(ColMode, cy, 86f, LineH), modeLabel, _style);

            GUI.color = Color.white;
            Vector2Int tile = ghost.CurrentTile;
            GUI.Label(new Rect(ColTile, cy, 58f, LineH), $"({tile.x},{tile.y})", _style);

            GUI.color = ghost.IsAlive
                ? new Color(0.5f, 1.0f, 0.5f)
                : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(ColAlive, cy, 48f, LineH),
                      ghost.IsAlive ? "Alive" : "Eaten", _style);

            cy += LineH;
        }

        // ── セパレータ + PacMan 行 ──────────────
        GUI.color = new Color(0.45f, 0.45f, 0.45f);
        GUI.Label(new Rect(ColName, cy, PanelW - 16f, LineH),
                  "──────────────────────────", _style);
        cy += LineH;

        if (_pacManMover != null)
        {
            Vector2Int pacTile = _pacManMover.CurrentTile;
            Vector2Int pacDir  = _pacManMover.CurrentDir;
            string     dirStr  = pacDir == Vector2Int.zero
                                 ? "STOP"
                                 : $"({pacDir.x:+0;-0},{pacDir.y:+0;-0})";

            GUI.color = new Color(1.0f, 1.0f, 0.0f);
            GUI.Label(new Rect(ColName, cy, 62f, LineH), "PacMan", _style);
            GUI.color = Color.white;
            GUI.Label(new Rect(ColMode, cy, 86f, LineH), dirStr,   _style);
            GUI.Label(new Rect(ColTile, cy, 58f, LineH), $"({pacTile.x},{pacTile.y})", _style);
        }
    }

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
