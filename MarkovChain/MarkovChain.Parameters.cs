using cAlgo.API;
using MarkovChainBot;

namespace cAlgo.Robots;

public partial class MarkovChain : Robot
{
    // ── Data ──────────────────────────────────────────────────────────────

    /// <summary>Number of most-recent daily bars to use for analysis.</summary>
    [Parameter("History Bars", DefaultValue = 500, MinValue = 100, MaxValue = 2000, Group = "Data")]
    public int HistoryBars { get; set; }

    /// <summary>Rolling return window in days for regime classification (Bull ≥ +5 %, Bear ≤ −5 %).</summary>
    [Parameter("Lookback Period (days)", DefaultValue = 20, MinValue = 5, MaxValue = 100, Group = "Data")]
    public int LookbackPeriod { get; set; }

    // ── Forecast ──────────────────────────────────────────────────────────

    /// <summary>Number of future days to forecast (1–5).</summary>
    [Parameter("Forecast Days", DefaultValue = 3, MinValue = 1, MaxValue = 5, Group = "Forecast")]
    public int ForecastDays { get; set; }

    // ── Logging ───────────────────────────────────────────────────────────

    /// <summary>When true, detailed debug messages are written to the cTrader journal.</summary>
    [Parameter("Debug Mode", DefaultValue = false, Group = "Logging")]
    public bool DebugMode { get; set; }

    // ── UI ────────────────────────────────────────────────────────────────

    /// <summary>Alpha channel (0–255) for the 3×3 transition matrix section background.</summary>
    [Parameter("Table Alpha", DefaultValue = 180, MinValue = 0, MaxValue = 255, Group = "UI")]
    public int TableAlpha { get; set; }

    /// <summary>Alpha channel (0–255) for the forecast / signal section background.</summary>
    [Parameter("Mix Alpha", DefaultValue = 220, MinValue = 0, MaxValue = 255, Group = "UI")]
    public int MixAlpha { get; set; }

    /// <summary>Chart corner where the analytics panel is anchored.</summary>
    [Parameter("Panel Corner", DefaultValue = PanelCorner.TopRight, Group = "UI")]
    public PanelCorner Corner { get; set; }

    /// <summary>
    /// Horizontal pixel offset from the selected corner toward the chart center.
    /// Positive = further from the edge. Range: −1000 to +1000.
    /// </summary>
    [Parameter("Panel Offset X", DefaultValue = 10, MinValue = -1000, MaxValue = 1000, Group = "UI")]
    public int PanelOffsetX { get; set; }

    /// <summary>
    /// Vertical pixel offset from the selected corner toward the chart center.
    /// Positive = further from the edge. Range: −1000 to +1000.
    /// </summary>
    [Parameter("Panel Offset Y", DefaultValue = 10, MinValue = -1000, MaxValue = 1000, Group = "UI")]
    public int PanelOffsetY { get; set; }
}
