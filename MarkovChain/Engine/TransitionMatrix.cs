using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// Builds a normalised 3×3 Markov transition matrix from a labelled state sequence
/// and provides incremental n-step probability forecasting.
/// Rows = from-state, Columns = to-state. Both indexed by (int)MarketState.
/// </summary>
public sealed class TransitionMatrix
{
    private double[,] _matrix = new double[3, 3];
    private readonly ILogger _logger;

    /// <summary>Constructs the transition matrix engine.</summary>
    public TransitionMatrix(ILogger logger)
    {
        _logger = logger;
        // Default: uniform (before any Build call)
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            _matrix[i, j] = 1.0 / 3.0;
    }

    /// <summary>
    /// The normalised 3×3 transition matrix. Access: Matrix[(int)fromState, (int)toState].
    /// </summary>
    public double[,] Matrix => _matrix;

    /// <summary>
    /// Counts all consecutive (state[t], state[t+1]) transitions in the sequence,
    /// then normalises each row to sum to 1.0.
    /// Rows with zero observed transitions default to uniform (1/3, 1/3, 1/3).
    /// </summary>
    public void Build(MarketState[] states)
    {
        var counts = new int[3, 3];
        for (int t = 0; t < states.Length - 1; t++)
            counts[(int)states[t], (int)states[t + 1]]++;

        for (int i = 0; i < 3; i++)
        {
            int rowSum = 0;
            for (int j = 0; j < 3; j++) rowSum += counts[i, j];

            if (rowSum == 0)
            {
                for (int j = 0; j < 3; j++) _matrix[i, j] = 1.0 / 3.0;
            }
            else
            {
                for (int j = 0; j < 3; j++)
                    _matrix[i, j] = (double)counts[i, j] / rowSum;
            }
        }

        _logger.LogInformation(nameof(Build),
            $"Transition matrix built from {states.Length} states.");
    }

    /// <summary>
    /// Computes the n-step forecast distribution starting from currentState.
    /// Uses incremental 1×3 × 3×3 multiplication: 9 mults per step, O(9n) total.
    /// Returns double[3]: [0]=P(Bull), [1]=P(Bear), [2]=P(Sideways).
    /// </summary>
    public double[] ForecastDistribution(MarketState currentState, int steps)
    {
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps), "steps must be >= 1");

        // Start as unit vector for currentState
        var dist = new double[3];
        dist[(int)currentState] = 1.0;

        // Multiply dist × A repeatedly
        for (int s = 0; s < steps; s++)
        {
            var next = new double[3];
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int i = 0; i < 3; i++)
                    sum += dist[i] * _matrix[i, j];
                next[j] = sum;
            }
            dist = next;
        }
        return dist;
    }

    /// <summary>
    /// Creates a new 3×3 normalised matrix from the first <paramref name="length"/>
    /// entries of <paramref name="states"/>. Stateless — used by BacktestAccuracy
    /// without mutating the main instance.
    /// </summary>
    public static double[,] BuildFrom(MarketState[] states, int length)
    {
        var counts = new int[3, 3];
        int limit = Math.Min(length, states.Length);
        for (int t = 0; t < limit - 1; t++)
            counts[(int)states[t], (int)states[t + 1]]++;

        var m = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            int rowSum = 0;
            for (int j = 0; j < 3; j++) rowSum += counts[i, j];

            if (rowSum == 0)
                for (int j = 0; j < 3; j++) m[i, j] = 1.0 / 3.0;
            else
                for (int j = 0; j < 3; j++) m[i, j] = (double)counts[i, j] / rowSum;
        }
        return m;
    }
}
