using cAlgo.API;
using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Draws a ±Threshold% price band on the main chart to visualise the regime zone
/// evaluated by <see cref="MarketRegimeClassifier"/>.
///
/// Geometry:
///   referenceClose  = DailyBars.ClosePrices[lastCompleted − LookbackPeriod]
///   upperBand       = referenceClose × (1 + threshold)   → above this → Bull
///   lowerBand       = referenceClose × (1 − threshold)   → below this → Bear
///   inside the band                                       → Sideways
///
/// The rectangle spans horizontally from the reference bar (N days ago) to a few
/// days past the current bar so the live price is always inside the visual frame.
///
/// Fill colour reflects the current classified state:
///   Sideways → semi-transparent blue
///   Bull     → semi-transparent green  (price broke above the band)
///   Bear     → semi-transparent red    (price broke below the band)
///
/// A dotted grey line at referenceClose marks the neutral anchor point.
/// All chart objects are tracked and removed on Destroy().
/// </summary>
public sealed class RegimeBandRenderer
{
    // ── Chart object names (stable across updates) ─────────────────────────
    private const string BAND_NAME       = "MarkovRegimeBand";
    private const string REF_LINE_NAME   = "MarkovRefClose";
    private const string UPPER_LINE_NAME = "MarkovUpperThresh";
    private const string LOWER_LINE_NAME = "MarkovLowerThresh";

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly Chart   _chart;
    private readonly bool    _canDraw;
    private readonly ILogger _logger;

    // ── Tracked chart objects ──────────────────────────────────────────────
    private ChartRectangle? _band;
    private ChartTrendLine? _refLine;
    private ChartTrendLine? _upperLine;
    private ChartTrendLine? _lowerLine;

    /// <summary>Constructs the regime band renderer.</summary>
    public RegimeBandRenderer(Chart chart, bool canDraw, ILogger logger)
    {
        _chart   = chart;
        _canDraw = canDraw;
        _logger  = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Redraws the ±threshold band for the current daily bar.
    /// Call once per daily bar close, after RunAnalysis().
    /// </summary>
    /// <param name="dailyBars">The daily bar series (from MarketData.GetBars).</param>
    /// <param name="lookbackPeriod">Number of days in the rolling return window.</param>
    /// <param name="thresholdFraction">
    /// Regime threshold as a fraction (e.g. 0.07 for 7%).
    /// Pass <c>RegimeThresholdPct / 100.0</c>.
    /// </param>
    /// <param name="currentState">Current classified regime (colours the band).</param>
    public void Update(
        Bars        dailyBars,
        int         lookbackPeriod,
        double      thresholdFraction,
        MarketState currentState)
    {
        Destroy();          // always redraw from scratch
        if (!_canDraw) return;

        // ── Compute price levels ──────────────────────────────────────────
        int lastIdx = dailyBars.Count - 2;          // most recent completed bar
        int refIdx  = lastIdx - lookbackPeriod;
        if (refIdx < 0) return;

        double refClose  = dailyBars.ClosePrices[refIdx];
        double upperBand = refClose * (1.0 + thresholdFraction);
        double lowerBand = refClose * (1.0 - thresholdFraction);

        // ── Time span: reference bar → current bar + small right margin ───
        DateTime refTime = dailyBars.OpenTimes[refIdx];
        // Extend rightward by ~25% of the lookback period so the current bar
        // sits visibly inside the rectangle (not right at the edge).
        int     rightPad = Math.Max(3, lookbackPeriod / 4);
        DateTime endTime = dailyBars.OpenTimes[lastIdx].AddDays(rightPad);

        // ── Choose colours by current regime ─────────────────────────────
        // The fill/border of the rectangle uses the cTrader convention:
        // IsFilled = true → fill uses the same colour passed to DrawRectangle.
        // Apply transparency via the alpha channel.
        Color bandFill =
            currentState == MarketState.Bull ? Color.FromArgb(40,  0, 200,  80) :  // green
            currentState == MarketState.Bear ? Color.FromArgb(40, 220, 50,  50) :  // red
                                               Color.FromArgb(40,  80, 130, 200);  // blue

        Color threshColor =
            currentState == MarketState.Bull ? Color.FromArgb(120,  0, 200,  80) :
            currentState == MarketState.Bear ? Color.FromArgb(120, 220, 50,  50) :
                                               Color.FromArgb(120,  80, 130, 200);

        Color refColor = Color.FromArgb(70, 160, 160, 160);    // grey dotted line

        // ── Draw band rectangle ───────────────────────────────────────────
        _band = _chart.DrawRectangle(BAND_NAME, refTime, upperBand, endTime, lowerBand, bandFill);
        _band.IsFilled = true;

        // ── Draw upper threshold edge (dashed, coloured) ──────────────────
        _upperLine = _chart.DrawTrendLine(UPPER_LINE_NAME,
            refTime, upperBand, endTime, upperBand, threshColor);
        _upperLine.LineStyle = LineStyle.Dots;
        _upperLine.Thickness = 1;

        // ── Draw lower threshold edge (dashed, coloured) ──────────────────
        _lowerLine = _chart.DrawTrendLine(LOWER_LINE_NAME,
            refTime, lowerBand, endTime, lowerBand, threshColor);
        _lowerLine.LineStyle = LineStyle.Dots;
        _lowerLine.Thickness = 1;

        // ── Draw reference close (neutral anchor, grey dotted) ────────────
        _refLine = _chart.DrawTrendLine(REF_LINE_NAME,
            refTime, refClose, endTime, refClose, refColor);
        _refLine.LineStyle = LineStyle.Lines;
        _refLine.Thickness = 1;

        _logger.LogInformation(nameof(Update),
            $"Band: ref={refClose:F0} upper={upperBand:F0}(+{thresholdFraction:P0}) "
          + $"lower={lowerBand:F0}(-{thresholdFraction:P0}) state={currentState}");
    }

    /// <summary>Removes all chart objects drawn by this renderer.</summary>
    public void Destroy()
    {
        if (_band       is not null) { _chart.RemoveObject(BAND_NAME);       _band       = null; }
        if (_refLine    is not null) { _chart.RemoveObject(REF_LINE_NAME);   _refLine    = null; }
        if (_upperLine  is not null) { _chart.RemoveObject(UPPER_LINE_NAME); _upperLine  = null; }
        if (_lowerLine  is not null) { _chart.RemoveObject(LOWER_LINE_NAME); _lowerLine  = null; }
    }
}
