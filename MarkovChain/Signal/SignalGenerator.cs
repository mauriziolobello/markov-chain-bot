using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Derives a directional trading signal from the D+1 forecast distribution.
///
/// Signal strength = P(Bull) − P(Bear), range [−1, 1].
/// The signal is "confirmed" when the base model and HMM agree on the current state.
///
/// Design choices implemented here (override or adjust NeutralThreshold as needed):
///   - Signals with |strength| &lt; NeutralThreshold → Neutral (filters noise).
///   - PSideways acts as a mild confidence reducer: the raw strength is scaled by
///     (1 − PSideways/2), so high sideways probability dampens the signal rather
///     than cancelling it entirely.
///   - Unconfirmed signals are still reported but marked IsConfirmed=false;
///     the panel displays a "?" suffix so the trader can decide whether to act.
/// </summary>
public sealed class SignalGenerator
{
    private readonly ILogger _logger;

    /// <summary>
    /// Signals with |adjusted strength| below this threshold are classified as Neutral.
    /// Default 0.10 (10 percentage points net bias required to trigger a directional signal).
    /// Increase to reduce signal frequency; decrease to be more sensitive.
    /// </summary>
    public double NeutralThreshold { get; set; } = 0.10;

    /// <summary>Constructs the signal generator.</summary>
    public SignalGenerator(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates the signal from the D+1 forecast and current regime states.
    /// </summary>
    /// <param name="d1Forecast">D+1 forecast (computed by ForecastEngine).</param>
    /// <param name="baseModelState">Current state from rolling-return classifier.</param>
    /// <param name="hmmState">Current state decoded by Viterbi from the HMM.</param>
    public SignalResult Compute(
        ForecastResult d1Forecast,
        MarketState    baseModelState,
        MarketState    hmmState)
    {
        // Raw net directional bias
        double rawStrength = d1Forecast.PBull - d1Forecast.PBear;

        // Dampen by sideways probability: high PSideways = market uncertainty
        // Factor in [0.5, 1.0]: full strength when PSideways=0, half-strength when PSideways=1
        double confidenceFactor = 1.0 - d1Forecast.PSideways * 0.5;
        double adjustedStrength = rawStrength * confidenceFactor;

        // Classify direction
        SignalDirection direction =
            adjustedStrength >  NeutralThreshold ? SignalDirection.Long  :
            adjustedStrength < -NeutralThreshold ? SignalDirection.Short :
                                                   SignalDirection.Neutral;

        // Confirmation: both models agree on the current state
        bool isConfirmed = baseModelState == hmmState;

        _logger.LogInformation(nameof(Compute),
            $"Signal: {direction}  raw={rawStrength:+0.000;-0.000;0.000} "
          + $"adj={adjustedStrength:+0.000;-0.000;0.000} "
          + $"confirmed={isConfirmed} (Base={baseModelState} HMM={hmmState})");

        return new SignalResult(adjustedStrength, direction, isConfirmed,
            baseModelState, hmmState);
    }
}
