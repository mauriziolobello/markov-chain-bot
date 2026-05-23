using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Computes probability forecasts for days 1..N by incrementally multiplying
/// the current-state distribution by the transition matrix.
/// Each step is a 1×3 × 3×3 multiply: 9 operations per day, O(9 × forecastDays) total.
/// </summary>
public sealed class ForecastEngine
{
    private readonly TransitionMatrix _transitionMatrix;
    private readonly ILogger          _logger;

    /// <summary>Constructs the forecast engine.</summary>
    public ForecastEngine(TransitionMatrix transitionMatrix, ILogger logger)
    {
        _transitionMatrix = transitionMatrix;
        _logger           = logger;
    }

    /// <summary>
    /// Returns forecast distributions for days 1 through forecastDays.
    /// Uses incremental 1×3 matrix multiplication: dist_n = dist_{n-1} × A.
    /// </summary>
    /// <param name="currentState">The current market state (D+0).</param>
    /// <param name="forecastDays">Number of forward days (1–5).</param>
    public ForecastResult[] Compute(MarketState currentState, int forecastDays)
    {
        int days    = Math.Max(1, Math.Min(forecastDays, 5));
        var results = new ForecastResult[days];

        for (int d = 1; d <= days; d++)
        {
            double[] dist = _transitionMatrix.ForecastDistribution(currentState, d);
            results[d - 1] = new ForecastResult(d, dist[0], dist[1], dist[2]);

            _logger.LogInformation(nameof(Compute),
                $"D+{d}: Bull={dist[0]:P1} Bear={dist[1]:P1} Side={dist[2]:P1}");
        }

        return results;
    }
}
