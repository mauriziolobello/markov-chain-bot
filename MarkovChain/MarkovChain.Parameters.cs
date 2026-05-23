using cAlgo.API;
using MarkovChainBot;

namespace cAlgo.Robots;

public partial class MarkovChain : Robot
{
    // ── Data ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Number of most-recent daily close prices fed to the HMM for training.
    /// Does NOT limit the regime classifier or the walk-forward backtest —
    /// both use all available daily bars. Only the HMM observation window is capped here.
    /// </summary>
    [Parameter("HMM Training Bars", DefaultValue = 500, MinValue = 100, MaxValue = 2000, Group = "Data")]
    public int HistoryBars { get; set; }

    /// <summary>Rolling return window in days for regime classification.</summary>
    [Parameter("Lookback Period (days)", DefaultValue = 20, MinValue = 5, MaxValue = 100, Group = "Data")]
    public int LookbackPeriod { get; set; }

    /// <summary>
    /// Number of most-recent classified bars used to BUILD the transition matrix.
    /// The classifier still labels all available history (needed for the walk-forward
    /// backtest), but only this many recent states feed into the 3×3 count matrix.
    ///
    /// Why this matters: with 2000 daily bars, BTC history includes the 2019 bear,
    /// the 2020 crash, and multiple consolidation years. The SIDE persistence inflates
    /// to 0.90+ because consolidation historically dominates. A shorter window (100–300)
    /// focuses the matrix on the current market regime, producing actionable forecasts.
    ///
    /// Rule of thumb: 100 bars ≈ 5 months · 200 bars ≈ 10 months · 500 bars = full history.
    /// </summary>
    [Parameter("Matrix Bars", DefaultValue = 200, MinValue = 50, MaxValue = 2000, Group = "Data")]
    public int MatrixBars { get; set; }

    /// <summary>
    /// ±Threshold (%) for the rolling-return base model.
    /// Return ≥ +threshold → Bull; ≤ −threshold → Bear; otherwise Sideways.
    /// Equity indices: 5 %. Crypto (BTC/ETH): 10–15 %. Forex majors: 1–3 %.
    /// </summary>
    [Parameter("Regime Threshold %", DefaultValue = 5, MinValue = 1, MaxValue = 30, Group = "Data")]
    public int RegimeThresholdPct { get; set; }

    /// <summary>
    /// Window size for rolling log-returns fed to the HMM (days).
    /// Each HMM observation = log(close[t] / close[t−N]).
    /// Larger values smooth noise and improve trend-direction sensitivity;
    /// smaller values react faster but include more whipsaw.
    /// </summary>
    [Parameter("HMM Window Days", DefaultValue = 5, MinValue = 1, MaxValue = 20, Group = "Data")]
    public int HmmWindowDays { get; set; }

    /// <summary>
    /// When true, each HMM observation is divided by the rolling standard deviation of
    /// returns over the past max(20, HmmWindowDays×4) bars.
    /// This converts raw returns to z-scores, making the HMM asset-agnostic:
    /// Bull/Bear/Sideways clusters represent "above/below/near average volatility"
    /// rather than absolute percentage moves.
    /// Recommended ON for crypto; OFF for equity indices.
    /// </summary>
    [Parameter("HMM Normalize (z-score)", DefaultValue = false, Group = "Data")]
    public bool HmmNormalize { get; set; }

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
    [Parameter("Panel Corner", DefaultValue = PanelCorner.BottomCenter, Group = "UI")]
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
