using cAlgo.API;
using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Collapsible Canvas panel displaying the Markov chain transition matrix,
/// n-step forecast, directional signal, and walk-forward D+1 accuracy.
///
/// All controls are created once in Initialize() and reused.
/// Update() only mutates Text/FillColor — no control creation at runtime.
/// </summary>
public sealed class MarkovPanelRenderer
{
    // ── Layout constants ──────────────────────────────────────────────────
    private const int PANEL_WIDTH    = 340;
    private const int HEADER_HEIGHT  = 30;
    private const int ROW_HEIGHT_STD = 22;
    private const int ROW_HEIGHT_MAT = 24;
    private const int SIGNAL_HEIGHT  = 28;
    private const int BOTTOM_PAD     = 12;
    private const int FONT_SIZE      = 11;
    private const int FONT_SIZE_HDR  = 12;
    private const string FONT_FAMILY = "Consolas";

    // Semantic state colours
    private static readonly Color COLOR_BULL = Color.FromArgb(255, 0,   200, 80);
    private static readonly Color COLOR_BEAR = Color.FromArgb(255, 200, 50,  50);
    private static readonly Color COLOR_SIDE = Color.FromArgb(255, 80,  130, 200);
    private static readonly Color COLOR_DIM  = Color.FromArgb(180, 160, 160, 160);
    private static readonly Color COLOR_DARK = Color.FromArgb(200, 20,  22,  28);

    // ── Dependencies ─────────────────────────────────────────────────────
    private readonly Chart       _chart;
    private readonly bool        _canDraw;
    private readonly PanelCorner _corner;
    private readonly int         _offsetX;
    private readonly int         _offsetY;
    private readonly int         _tableAlpha;
    private readonly int         _mixAlpha;
    private readonly int         _forecastDays;
    private readonly ILogger     _logger;

    // ── Canvas and top-level controls ─────────────────────────────────────
    private Canvas?     _canvas;
    private Button?     _toggleButton;
    private StackPanel? _contentPanel;
    private bool        _isExpanded = true;

    // ── Matrix grid controls (created once, mutated on update) ────────────
    private Rectangle[,]? _matrixBg   = new Rectangle[3, 3];
    private TextBlock[,]?  _matrixText = new TextBlock[3, 3];
    private TextBlock?     _stateRow;

    // ── Forecast / signal / accuracy controls ─────────────────────────────
    private TextBlock[]?  _forecastRows;
    private TextBlock?    _signalRow;
    private TextBlock?    _accuracyRow;

    private static readonly string[] STATE_LABELS = { "BULL", "BEAR", "SIDE" };

    /// <summary>Constructs the panel renderer.</summary>
    public MarkovPanelRenderer(
        Chart chart, bool canDraw, PanelCorner corner,
        int offsetX, int offsetY, int tableAlpha, int mixAlpha,
        int forecastDays, ILogger logger)
    {
        _chart        = chart;
        _canDraw      = canDraw;
        _corner       = corner;
        _offsetX      = offsetX;
        _offsetY      = offsetY;
        _tableAlpha   = Math.Clamp(tableAlpha, 0, 255);
        _mixAlpha     = Math.Clamp(mixAlpha,   0, 255);
        _forecastDays = Math.Clamp(forecastDays, 1, 5);
        _logger       = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Creates all Canvas controls and adds the panel to the chart.</summary>
    public void Initialize()
    {
        if (!_canDraw) return;

        int expandedH = ComputeExpandedHeight();

        _canvas = new Canvas
        {
            Width            = PANEL_WIDTH,
            Height           = expandedH,
            IsHitTestVisible = true
        };

        PositionCanvas();

        // ── Title / toggle button ─────────────────────────────────────────
        _toggleButton = new Button
        {
            Width           = PANEL_WIDTH,
            Height          = HEADER_HEIGHT,
            Text            = "▲ MARKOV CHAIN v1.0.0",
            BackgroundColor = COLOR_DARK,
            ForegroundColor = Color.White,
            FontFamily      = FONT_FAMILY,
            FontSize        = FONT_SIZE_HDR
        };
        _toggleButton.Click += OnToggleClick;
        _canvas.AddChild(_toggleButton);

        // ── Content StackPanel (removed on collapse) ──────────────────────
        _contentPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width       = PANEL_WIDTH,
            Top         = HEADER_HEIGHT
        };

        BuildTableSection();
        BuildMixSection();
        BuildAccuracySection();

        _canvas.AddChild(_contentPanel);
        _chart.AddControl(_canvas);

        _logger.LogInformation(nameof(Initialize), "Panel initialised.");
    }

    /// <summary>
    /// Updates all display controls with the latest model results.
    /// Only mutates Text and color properties — no new controls created.
    /// </summary>
    public void Update(
        double[,]        transitionMatrix,
        MarketState      baseModelState,
        MarketState      hmmState,
        ForecastResult[] forecasts,
        SignalResult     signal,
        double           hitRate,
        int              sampleCount)
    {
        if (!_canDraw || _canvas is null) return;

        // ── 3×3 Matrix cells ─────────────────────────────────────────────
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            double val = transitionMatrix[i, j];
            _matrixText![i, j].Text = $"{val:F2}";

            // Diagonal: background intensity ∝ persistence probability
            if (i == j)
            {
                int intensity = Math.Clamp((int)(val * 200), 20, 200);
                _matrixBg![i, j].FillColor =
                    i == 0 ? Color.FromArgb(intensity, 0,   200, 80) :   // Bull  → green
                    i == 1 ? Color.FromArgb(intensity, 200, 50,  50) :   // Bear  → red
                             Color.FromArgb(intensity, 80,  130, 200);   // Side  → blue
            }
        }

        // ── State + confirmation row ──────────────────────────────────────
        bool confirmed    = signal.IsConfirmed;
        string confirmMark = confirmed ? "✓ CONFIRMED" : "✗ DIVERGING";
        _stateRow!.Text            = $"Base:{StateLabel(baseModelState)}  "
                                   + $"HMM:{StateLabel(hmmState)}  {confirmMark}";
        _stateRow.ForegroundColor  = confirmed
            ? Color.FromArgb(255, 0, 200, 80)
            : Color.FromArgb(255, 200, 50, 50);

        // ── Forecast rows ─────────────────────────────────────────────────
        for (int d = 0; d < Math.Min(forecasts.Length, _forecastDays); d++)
        {
            var f = forecasts[d];
            _forecastRows![d].Text = $"D+{f.Day}: {f.PBull:P0} Bull"
                                   + $"  {f.PBear:P0} Bear"
                                   + $"  {f.PSideways:P0} Side";
            _forecastRows[d].ForegroundColor = StateColor(f.DominantState);
        }

        // ── Signal row ────────────────────────────────────────────────────
        string dirStr = signal.Direction == SignalDirection.Long  ? "LONG "
                      : signal.Direction == SignalDirection.Short ? "SHORT"
                      :                                             "NEUT.";
        string confSuffix = signal.IsConfirmed ? "  ✓" : "  ?";
        _signalRow!.Text           = $"SIGNAL: {dirStr}  {signal.Strength:+0.000;-0.000;0.000}{confSuffix}";
        _signalRow.ForegroundColor = signal.Direction == SignalDirection.Long  ? COLOR_BULL
                                   : signal.Direction == SignalDirection.Short ? COLOR_BEAR
                                   :                                              COLOR_DIM;

        // ── Accuracy row ──────────────────────────────────────────────────
        _accuracyRow!.Text = $"D+1 ACCURACY: {hitRate:P1}  (N={sampleCount})";
    }

    /// <summary>Removes the panel from the chart.</summary>
    public void Destroy()
    {
        if (_canvas is not null)
        {
            _chart.RemoveControl(_canvas);
            _canvas = null;
        }
    }

    // ── Section builders (called once in Initialize) ──────────────────────

    private void BuildTableSection()
    {
        Color tableBg = Color.FromArgb(_tableAlpha, 20, 22, 28);

        int tableH = ROW_HEIGHT_STD          // "TRANSITION MATRIX" header
                   + 18                      // column header row
                   + 3 * ROW_HEIGHT_MAT      // 3 data rows
                   + ROW_HEIGHT_STD;         // state + confirmation row

        var tableCanvas = new Canvas
        {
            Width           = PANEL_WIDTH,
            Height          = tableH,
            BackgroundColor = tableBg
        };
        _contentPanel!.AddChild(tableCanvas);

        // "TRANSITION MATRIX" section title
        tableCanvas.AddChild(new TextBlock
        {
            Text            = "TRANSITION MATRIX",
            Left            = 8, Top = 4,
            FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
            ForegroundColor = COLOR_DIM
        });

        // Column headers
        int colLeft0 = 80;
        string[] colHdrs = { " BULL", " BEAR", " SIDE" };
        for (int j = 0; j < 3; j++)
            tableCanvas.AddChild(new TextBlock
            {
                Text            = colHdrs[j],
                Left            = colLeft0 + j * 85,
                Top             = ROW_HEIGHT_STD + 2,
                FontFamily      = FONT_FAMILY, FontSize = 10,
                ForegroundColor = StateColor((MarketState)j)
            });

        // 3 matrix rows
        int rowTop = ROW_HEIGHT_STD + 18;
        for (int i = 0; i < 3; i++)
        {
            int top = rowTop + i * ROW_HEIGHT_MAT;

            // Row label
            tableCanvas.AddChild(new TextBlock
            {
                Text            = STATE_LABELS[i],
                Left            = 8, Top = top + 4,
                FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
                ForegroundColor = StateColor((MarketState)i)
            });

            // 3 cells per row
            for (int j = 0; j < 3; j++)
            {
                int cellLeft = colLeft0 + j * 85;

                // Background (diagonal cells get coloured highlight)
                _matrixBg![i, j] = new Rectangle
                {
                    Width           = 78, Height = ROW_HEIGHT_MAT - 2,
                    Left            = cellLeft, Top = top,
                    FillColor       = Color.FromArgb(20, 40, 40, 40),
                    StrokeColor     = Color.FromArgb(40, 80, 80, 80),
                    StrokeThickness = 1
                };
                tableCanvas.AddChild(_matrixBg[i, j]);

                // Cell text
                _matrixText![i, j] = new TextBlock
                {
                    Text            = "0.00",
                    Left            = cellLeft + 8, Top = top + 5,
                    FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
                    ForegroundColor = Color.White
                };
                tableCanvas.AddChild(_matrixText[i, j]);
            }
        }

        // State + confirmation row
        _stateRow = new TextBlock
        {
            Text            = "Base:----  HMM:----",
            Left            = 8, Top = rowTop + 3 * ROW_HEIGHT_MAT + 4,
            FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
            ForegroundColor = COLOR_DIM
        };
        tableCanvas.AddChild(_stateRow);
    }

    private void BuildMixSection()
    {
        Color mixBg = Color.FromArgb(_mixAlpha, 25, 25, 35);

        int mixH = ROW_HEIGHT_STD                   // "FORECAST" header
                 + _forecastDays * ROW_HEIGHT_STD   // forecast rows
                 + SIGNAL_HEIGHT;                   // signal row

        var mixCanvas = new Canvas
        {
            Width           = PANEL_WIDTH,
            Height          = mixH,
            BackgroundColor = mixBg
        };
        _contentPanel!.AddChild(mixCanvas);

        // "FORECAST" header
        mixCanvas.AddChild(new TextBlock
        {
            Text            = "FORECAST",
            Left            = 8, Top = 4,
            FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
            ForegroundColor = COLOR_DIM
        });

        // Forecast rows
        _forecastRows = new TextBlock[_forecastDays];
        for (int d = 0; d < _forecastDays; d++)
        {
            _forecastRows[d] = new TextBlock
            {
                Text            = $"D+{d + 1}: --% Bull  --% Bear  --% Side",
                Left            = 8,
                Top             = ROW_HEIGHT_STD + d * ROW_HEIGHT_STD + 2,
                FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
                ForegroundColor = Color.White
            };
            mixCanvas.AddChild(_forecastRows[d]);
        }

        // Signal row
        _signalRow = new TextBlock
        {
            Text            = "SIGNAL: ----",
            Left            = 8,
            Top             = ROW_HEIGHT_STD + _forecastDays * ROW_HEIGHT_STD + 4,
            FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE_HDR,
            ForegroundColor = COLOR_DIM
        };
        mixCanvas.AddChild(_signalRow);
    }

    private void BuildAccuracySection()
    {
        Color accBg = Color.FromArgb(_mixAlpha, 20, 20, 30);

        var accCanvas = new Canvas
        {
            Width           = PANEL_WIDTH,
            Height          = ROW_HEIGHT_STD + BOTTOM_PAD,
            BackgroundColor = accBg
        };
        _contentPanel!.AddChild(accCanvas);

        _accuracyRow = new TextBlock
        {
            Text            = "D+1 ACCURACY: --%  (N=0)",
            Left            = 8, Top = 4,
            FontFamily      = FONT_FAMILY, FontSize = FONT_SIZE,
            ForegroundColor = COLOR_DIM
        };
        accCanvas.AddChild(_accuracyRow);
    }

    // ── Collapse / expand ─────────────────────────────────────────────────

    private void OnToggleClick(ButtonClickEventArgs _)
    {
        _isExpanded = !_isExpanded;

        if (_isExpanded)
        {
            _canvas!.Height     = ComputeExpandedHeight();
            _toggleButton!.Text = "▲ MARKOV CHAIN v1.0.0";
            _canvas.AddChild(_contentPanel!);
        }
        else
        {
            _canvas!.Height     = HEADER_HEIGHT;
            _toggleButton!.Text = "▼ MARKOV CHAIN v1.0.0";
            _canvas.RemoveChild(_contentPanel!);
        }
    }

    // ── Canvas positioning ─────────────────────────────────────────────────

    private void PositionCanvas()
    {
        // Positive offsetX/Y = move toward chart center from the selected corner
        switch (_corner)
        {
            case PanelCorner.TopLeft:
                _canvas!.HorizontalAlignment = HorizontalAlignment.Left;
                _canvas.VerticalAlignment    = VerticalAlignment.Top;
                _canvas.Margin = new Thickness(Math.Max(0, _offsetX), Math.Max(0, _offsetY), 0, 0);
                break;

            case PanelCorner.TopRight:
                _canvas!.HorizontalAlignment = HorizontalAlignment.Right;
                _canvas.VerticalAlignment    = VerticalAlignment.Top;
                _canvas.Margin = new Thickness(0, Math.Max(0, _offsetY), Math.Max(0, _offsetX), 0);
                break;

            case PanelCorner.BottomLeft:
                _canvas!.HorizontalAlignment = HorizontalAlignment.Left;
                _canvas.VerticalAlignment    = VerticalAlignment.Bottom;
                _canvas.Margin = new Thickness(Math.Max(0, _offsetX), 0, 0, Math.Max(0, _offsetY));
                break;

            case PanelCorner.BottomRight:
            default:
                _canvas!.HorizontalAlignment = HorizontalAlignment.Right;
                _canvas.VerticalAlignment    = VerticalAlignment.Bottom;
                _canvas.Margin = new Thickness(0, 0, Math.Max(0, _offsetX), Math.Max(0, _offsetY));
                break;
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    private int ComputeExpandedHeight()
    {
        int tableH = ROW_HEIGHT_STD + 18 + 3 * ROW_HEIGHT_MAT + ROW_HEIGHT_STD;
        int mixH   = ROW_HEIGHT_STD + _forecastDays * ROW_HEIGHT_STD + SIGNAL_HEIGHT;
        int accH   = ROW_HEIGHT_STD + BOTTOM_PAD;
        return HEADER_HEIGHT + tableH + mixH + accH;
    }

    private static Color StateColor(MarketState s) =>
        s == MarketState.Bull ? COLOR_BULL
                              : s == MarketState.Bear ? COLOR_BEAR : COLOR_SIDE;

    private static string StateLabel(MarketState s) =>
        s == MarketState.Bull ? "BULL" : s == MarketState.Bear ? "BEAR" : "SIDE";
}
