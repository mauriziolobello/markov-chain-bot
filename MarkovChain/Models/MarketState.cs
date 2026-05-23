namespace MarkovChainBot;

/// <summary>The three market regime states used by base model, HMM, and transition matrix.</summary>
/// <remarks>Explicit integer values ensure stable array indexing in 3×3 matrix operations.</remarks>
public enum MarketState
{
    Bull     = 0,
    Bear     = 1,
    Sideways = 2
}
