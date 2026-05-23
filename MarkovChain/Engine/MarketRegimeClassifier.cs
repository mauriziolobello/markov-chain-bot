using cAlgo.API;
using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Classifies each completed daily bar as Bull, Bear, or Sideways using the
/// N-day rolling percentage return (threshold ±5 %).
/// Index 0 = oldest bar; the forming bar (Bars.Count−1) is always excluded.
/// </summary>
public sealed class MarketRegimeClassifier
{
    private const double BULL_THRESHOLD =  0.05;
    private const double BEAR_THRESHOLD = -0.05;

    private readonly Bars    _dailyBars;
    private readonly int     _lookbackPeriod;
    private readonly ILogger _logger;

    private MarketState[] _cachedStates = Array.Empty<MarketState>();

    /// <summary>Constructs the classifier.</summary>
    /// <param name="dailyBars">Daily OHLC series loaded via MarketData.GetBars.</param>
    /// <param name="lookbackPeriod">Number of days for the rolling return window.</param>
    /// <param name="logger">Logger instance.</param>
    public MarketRegimeClassifier(Bars dailyBars, int lookbackPeriod, ILogger logger)
    {
        _dailyBars      = dailyBars;
        _lookbackPeriod = lookbackPeriod;
        _logger         = logger;
    }

    /// <summary>
    /// The current market state (most recently completed daily bar).
    /// Updated after each call to ClassifyAll().
    /// </summary>
    public MarketState CurrentState { get; private set; } = MarketState.Sideways;

    /// <summary>
    /// Classifies all completed daily bars (indices 0 to Count−2).
    /// Bars with fewer than LookbackPeriod predecessors default to Sideways.
    /// </summary>
    public MarketState[] ClassifyAll()
    {
        // Exclude the forming bar: last completed = Count − 2
        int count = _dailyBars.Count - 1;
        if (count <= 0)
        {
            _logger.LogWarning(nameof(ClassifyAll), "Insufficient bars to classify.");
            return Array.Empty<MarketState>();
        }

        _cachedStates = new MarketState[count];

        for (int i = 0; i < count; i++)
        {
            if (i < _lookbackPeriod)
            {
                _cachedStates[i] = MarketState.Sideways;
                continue;
            }

            double prevClose = _dailyBars.ClosePrices[i - _lookbackPeriod];
            double currClose = _dailyBars.ClosePrices[i];

            if (prevClose <= 0)
            {
                _cachedStates[i] = MarketState.Sideways;
                continue;
            }

            double ret = (currClose - prevClose) / prevClose;

            _cachedStates[i] = ret >= BULL_THRESHOLD ? MarketState.Bull  :
                                ret <= BEAR_THRESHOLD ? MarketState.Bear  :
                                                        MarketState.Sideways;
        }

        CurrentState = _cachedStates[count - 1];

        _logger.LogInformation(nameof(ClassifyAll),
            $"Classified {count} bars. Current state: {CurrentState}");

        return _cachedStates;
    }

    /// <summary>
    /// Returns close prices for the N most recent completed bars as a double[].
    /// Oldest first. Used to build log-returns for the HMM.
    /// </summary>
    /// <param name="maxBars">Maximum number of bars to return.</param>
    public double[] GetRecentClosePrices(int maxBars)
    {
        // completed bars: indices 0..Count-2
        int available = _dailyBars.Count - 1;
        int take  = Math.Min(available, maxBars);
        int start = available - take;

        var result = new double[take];
        for (int i = 0; i < take; i++)
            result[i] = _dailyBars.ClosePrices[start + i];
        return result;
    }
}
