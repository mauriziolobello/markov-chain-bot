using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Walk-forward D+1 hit-rate: for each historical bar from (lookbackPeriod×2) onward,
/// builds the transition matrix on bars 0..t, predicts the dominant D+1 state,
/// and compares it against the actual label at bar t+1.
///
/// Runs once at startup (and optionally on each daily bar).
/// Complexity: O(N²) in bar count, acceptable for N ≤ 2000.
/// </summary>
public sealed class BacktestAccuracy
{
    private readonly ILogger _logger;

    /// <summary>Constructs the walk-forward accuracy calculator.</summary>
    public BacktestAccuracy(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes the D+1 prediction hit rate over the available history.
    /// </summary>
    /// <param name="allStates">Full classified state sequence (from MarketRegimeClassifier).</param>
    /// <param name="lookbackPeriod">Minimum warm-up window before the walk-forward starts.</param>
    /// <returns>Tuple of (hit rate as fraction 0–1, number of test samples).</returns>
    public (double HitRate, int SampleCount) Compute(MarketState[] allStates, int lookbackPeriod)
    {
        int startIdx = lookbackPeriod * 2;      // need at least this many bars for a meaningful matrix
        int endIdx   = allStates.Length - 2;    // last bar where we can check t+1

        if (startIdx >= endIdx)
        {
            _logger.LogWarning(nameof(Compute),
                $"Insufficient history for walk-forward backtest "
              + $"(need {startIdx + 1} bars, have {allStates.Length}).");
            return (0.0, 0);
        }

        int hits  = 0;
        int total = 0;

        for (int t = startIdx; t <= endIdx; t++)
        {
            // Build transition matrix from prefix allStates[0..t] (stateless helper)
            double[,] m = TransitionMatrix.BuildFrom(allStates, t + 1);

            // Predict: argmax of the row for allStates[t]
            int stateIdx = (int)allStates[t];
            int predictedIdx = 0;
            double maxProb = -1.0;
            for (int j = 0; j < 3; j++)
            {
                if (m[stateIdx, j] > maxProb)
                {
                    maxProb      = m[stateIdx, j];
                    predictedIdx = j;
                }
            }

            var predicted = (MarketState)predictedIdx;
            var actual    = allStates[t + 1];

            if (predicted == actual) hits++;
            total++;
        }

        double hitRate = total > 0 ? (double)hits / total : 0.0;
        _logger.LogInformation(nameof(Compute),
            $"Walk-forward D+1 accuracy: {hitRate:P1} over {total} samples "
          + $"({hits} correct).");
        return (hitRate, total);
    }
}
