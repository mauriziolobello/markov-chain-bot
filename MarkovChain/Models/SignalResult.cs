namespace MarkovChainBot;

/// <summary>Direction of the trading signal.</summary>
public enum SignalDirection { Long, Short, Neutral }

/// <summary>Output of the signal generator for the D+1 forecast horizon.</summary>
public class SignalResult
{
    /// <summary>P(Bull) − P(Bear) for D+1, range [−1, 1].</summary>
    public double Strength { get; }

    /// <summary>Long when Strength > 0, Short when &lt; 0, Neutral when near 0.</summary>
    public SignalDirection Direction { get; }

    /// <summary>True when base model and HMM agree on the current market state.</summary>
    public bool IsConfirmed { get; }

    /// <summary>Current state as classified by the rolling-return base model.</summary>
    public MarketState BaseModelState { get; }

    /// <summary>Current state decoded by Viterbi from the Gaussian HMM.</summary>
    public MarketState HmmState { get; }

    /// <summary>Constructs a signal result.</summary>
    public SignalResult(double strength, SignalDirection direction, bool isConfirmed,
        MarketState baseModelState, MarketState hmmState)
    {
        Strength       = strength;
        Direction      = direction;
        IsConfirmed    = isConfirmed;
        BaseModelState = baseModelState;
        HmmState       = hmmState;
    }
}
