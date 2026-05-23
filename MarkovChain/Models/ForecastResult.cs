namespace MarkovChainBot;

/// <summary>Probability distribution over Bull/Bear/Sideways for a specific future day.</summary>
public class ForecastResult
{
    /// <summary>Forecast horizon in days: 1 = tomorrow, 2 = day after, …</summary>
    public int Day { get; }

    /// <summary>Probability of Bull regime at this horizon.</summary>
    public double PBull { get; }

    /// <summary>Probability of Bear regime at this horizon.</summary>
    public double PBear { get; }

    /// <summary>Probability of Sideways regime at this horizon.</summary>
    public double PSideways { get; }

    /// <summary>Net directional bias: P(Bull) − P(Bear).</summary>
    public double NetSignal => PBull - PBear;

    /// <summary>Regime with highest probability at this horizon.</summary>
    public MarketState DominantState =>
        PBull >= PBear && PBull >= PSideways ? MarketState.Bull  :
        PBear >= PSideways                    ? MarketState.Bear  :
                                               MarketState.Sideways;

    /// <summary>Constructs a forecast result.</summary>
    public ForecastResult(int day, double pBull, double pBear, double pSideways)
    {
        Day       = day;
        PBull     = pBull;
        PBear     = pBear;
        PSideways = pSideways;
    }
}
