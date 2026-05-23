using LibraryLog;

namespace MarkovChainBot;

/// <summary>
/// 3-state Gaussian Hidden Markov Model trained with Baum-Welch EM.
/// Observations: daily log-returns  log(close[t] / close[t−1]).
/// Hidden states are aligned post-training to Bull/Bear/Sideways by learned mean.
/// All forward/backward passes operate in log-space for numerical stability.
/// </summary>
public sealed class GaussianHmm
{
    private const int    N   = 3;      // number of hidden states
    private const double EPS = 1e-10;  // numerical floor

    private readonly int    _maxIterations;
    private readonly double _convergenceThreshold;
    private readonly ILogger _logger;

    // ── Model parameters (learned by Baum-Welch) ─────────────────────────
    private double[]  _pi    = new double[N];      // initial state distribution
    private double[,] _a     = new double[N, N];   // transition matrix
    private double[]  _mu    = new double[N];      // Gaussian means
    private double[]  _sigma = new double[N];      // Gaussian std devs

    // ── Alignment map: raw HMM index → MarketState ───────────────────────
    private MarketState[] _stateMap = { MarketState.Bull, MarketState.Bear, MarketState.Sideways };

    /// <summary>True after at least one successful call to Train().</summary>
    public bool IsTrained { get; private set; }

    /// <summary>
    /// Current market state (most recent observation decoded by Viterbi).
    /// Updated after each call to Decode().
    /// </summary>
    public MarketState CurrentState { get; private set; } = MarketState.Sideways;

    /// <param name="maxIterations">Maximum Baum-Welch EM iterations.</param>
    /// <param name="convergenceThreshold">Stop when Δ log-likelihood &lt; this value.</param>
    /// <param name="logger">Logger instance.</param>
    public GaussianHmm(int maxIterations, double convergenceThreshold, ILogger logger)
    {
        _maxIterations        = maxIterations;
        _convergenceThreshold = convergenceThreshold;
        _logger               = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Trains the HMM on a sequence of daily log-returns using Baum-Welch EM.
    /// Call this before Decode() or GetCurrentStateProbs().
    /// </summary>
    /// <param name="observations">Log-returns, oldest first. Minimum ~10 values.</param>
    public void Train(double[] observations)
    {
        if (observations.Length < 10)
        {
            _logger.LogWarning(nameof(Train), "Insufficient observations for HMM training.");
            return;
        }

        Initialize(observations);

        double prevLogLik = double.NegativeInfinity;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            var (logAlpha, logBeta) = ForwardBackward(observations);
            double logLik = LogSumExpRow(logAlpha, observations.Length - 1);

            BaumWelchUpdate(observations, logAlpha, logBeta);

            if (iter > 5 && Math.Abs(logLik - prevLogLik) < _convergenceThreshold)
            {
                _logger.LogInformation(nameof(Train), $"Converged after {iter + 1} iterations.");
                break;
            }
            prevLogLik = logLik;
        }

        AlignStatesToMarket();
        IsTrained = true;
        _logger.LogInformation(nameof(Train),
            $"HMM trained. State map: 0={_stateMap[0]} μ={_mu[0]:F5}, "
          + $"1={_stateMap[1]} μ={_mu[1]:F5}, 2={_stateMap[2]} μ={_mu[2]:F5}");
    }

    /// <summary>
    /// Decodes the most probable state sequence via Viterbi (log-space).
    /// Returns states aligned to Bull/Bear/Sideways.
    /// Also updates CurrentState to the state of the last observation.
    /// </summary>
    public MarketState[] Decode(double[] observations)
    {
        if (!IsTrained || observations.Length == 0)
            return Array.Empty<MarketState>();

        int T = observations.Length;
        var logDelta = new double[T, N];
        var psi      = new int[T, N];

        // Initialise
        for (int i = 0; i < N; i++)
            logDelta[0, i] = Math.Log(_pi[i] + EPS)
                             + LogGaussian(observations[0], _mu[i], _sigma[i]);

        // Recursion
        for (int t = 1; t < T; t++)
        for (int j = 0; j < N; j++)
        {
            double best    = double.NegativeInfinity;
            int    bestIdx = 0;
            for (int i = 0; i < N; i++)
            {
                double v = logDelta[t - 1, i] + Math.Log(_a[i, j] + EPS);
                if (v > best) { best = v; bestIdx = i; }
            }
            logDelta[t, j] = best + LogGaussian(observations[t], _mu[j], _sigma[j]);
            psi[t, j]      = bestIdx;
        }

        // Backtrack
        var rawStates = new int[T];
        double maxFinal = double.NegativeInfinity;
        for (int i = 0; i < N; i++)
            if (logDelta[T - 1, i] > maxFinal)
            {
                maxFinal = logDelta[T - 1, i];
                rawStates[T - 1] = i;
            }
        for (int t = T - 2; t >= 0; t--)
            rawStates[t] = psi[t + 1, rawStates[t + 1]];

        // Map raw states to MarketState via alignment map
        var aligned = new MarketState[T];
        for (int t = 0; t < T; t++)
            aligned[t] = _stateMap[rawStates[t]];

        CurrentState = aligned[T - 1];
        return aligned;
    }

    /// <summary>
    /// Returns P(Bull), P(Bear), P(Sideways) for the last observation
    /// using the Forward pass posterior.
    /// Returns double[3] indexed by (int)MarketState.
    /// </summary>
    public double[] GetCurrentStateProbs(double[] observations)
    {
        if (!IsTrained || observations.Length == 0)
            return new double[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 };

        int T = observations.Length;
        var (logAlpha, logBeta) = ForwardBackward(observations);

        // Posterior at time T-1: γ(T-1, i) ∝ α(T-1,i) × β(T-1,i)
        var logPost = new double[N];
        for (int i = 0; i < N; i++)
            logPost[i] = logAlpha[T - 1, i] + logBeta[T - 1, i];

        double logNorm = LogSumExp(logPost);

        // Re-index by MarketState (Bull=0, Bear=1, Sideways=2)
        var result = new double[3];
        for (int i = 0; i < N; i++)
            result[(int)_stateMap[i]] += Math.Exp(logPost[i] - logNorm);

        return result;
    }

    // ── Initialisation ────────────────────────────────────────────────────

    private void Initialize(double[] obs)
    {
        // Sort and partition into 3 equal segments; use segment means for μ
        var sorted = (double[])obs.Clone();
        Array.Sort(sorted);

        int third = Math.Max(1, sorted.Length / 3);

        // State 0: highest mean (Bull), State 1: lowest (Bear), State 2: middle (Sideways)
        _mu[0] = SegmentMean(sorted, 2 * third, sorted.Length);
        _mu[1] = SegmentMean(sorted, 0, third);
        _mu[2] = SegmentMean(sorted, third, 2 * third);

        double globalStd = Math.Max(StdDev(obs), EPS);
        for (int i = 0; i < N; i++)
        {
            _sigma[i] = globalStd * 0.8;
            _pi[i]    = 1.0 / N;
        }

        // A: persistence-biased (diagonal 0.6, off-diagonal 0.2)
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
            _a[i, j] = (i == j) ? 0.6 : 0.2;
    }

    // ── Forward-Backward ─────────────────────────────────────────────────

    private (double[,] logAlpha, double[,] logBeta) ForwardBackward(double[] obs)
    {
        int T        = obs.Length;
        var logAlpha = new double[T, N];
        var logBeta  = new double[T, N];

        // Forward
        for (int i = 0; i < N; i++)
            logAlpha[0, i] = Math.Log(_pi[i] + EPS)
                             + LogGaussian(obs[0], _mu[i], _sigma[i]);

        for (int t = 1; t < T; t++)
        for (int j = 0; j < N; j++)
        {
            var terms = new double[N];
            for (int i = 0; i < N; i++)
                terms[i] = logAlpha[t - 1, i] + Math.Log(_a[i, j] + EPS);
            logAlpha[t, j] = LogSumExp(terms) + LogGaussian(obs[t], _mu[j], _sigma[j]);
        }

        // Backward
        for (int i = 0; i < N; i++) logBeta[T - 1, i] = 0.0;  // log(1)

        for (int t = T - 2; t >= 0; t--)
        for (int i = 0; i < N; i++)
        {
            var terms = new double[N];
            for (int j = 0; j < N; j++)
                terms[j] = Math.Log(_a[i, j] + EPS)
                           + LogGaussian(obs[t + 1], _mu[j], _sigma[j])
                           + logBeta[t + 1, j];
            logBeta[t, i] = LogSumExp(terms);
        }

        return (logAlpha, logBeta);
    }

    // ── Baum-Welch M-step ─────────────────────────────────────────────────

    private void BaumWelchUpdate(double[] obs, double[,] logAlpha, double[,] logBeta)
    {
        int T = obs.Length;

        // ── Compute log γ(t, i) ──────────────────────────────────────────
        var logGamma = new double[T, N];
        for (int t = 0; t < T; t++)
        {
            var row = new double[N];
            for (int i = 0; i < N; i++)
                row[i] = logAlpha[t, i] + logBeta[t, i];
            double norm = LogSumExp(row);
            for (int i = 0; i < N; i++)
                logGamma[t, i] = row[i] - norm;
        }

        // ── Accumulate A numerator and denominator in one time-pass ───────
        // This avoids storing the full ξ[T, N, N] tensor (memory: O(N²) not O(T·N²))
        var aNum = new double[N, N];
        var aDen = new double[N];

        for (int t = 0; t < T - 1; t++)
        {
            // Compute all N×N log-ξ terms for this time step
            var xiTerms = new double[N * N];
            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                xiTerms[i * N + j] = logAlpha[t, i]
                                   + Math.Log(_a[i, j] + EPS)
                                   + LogGaussian(obs[t + 1], _mu[j], _sigma[j])
                                   + logBeta[t + 1, j];

            double xiNorm = LogSumExp(xiTerms);

            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                    aNum[i, j] += Math.Exp(xiTerms[i * N + j] - xiNorm);
                aDen[i] += Math.Exp(logGamma[t, i]);
            }
        }

        // ── Update π ─────────────────────────────────────────────────────
        double piSum = 0;
        for (int i = 0; i < N; i++)
        {
            _pi[i] = Math.Exp(logGamma[0, i]);
            piSum  += _pi[i];
        }
        if (piSum > EPS)
            for (int i = 0; i < N; i++) _pi[i] /= piSum;

        // ── Update A ─────────────────────────────────────────────────────
        for (int i = 0; i < N; i++)
        {
            double rowSum = 0;
            for (int j = 0; j < N; j++) rowSum += aNum[i, j];
            for (int j = 0; j < N; j++)
                _a[i, j] = rowSum > EPS ? aNum[i, j] / rowSum : 1.0 / N;
        }

        // ── Update μ and σ ────────────────────────────────────────────────
        for (int i = 0; i < N; i++)
        {
            double gSum  = 0;
            double wMean = 0;
            for (int t = 0; t < T; t++)
            {
                double g = Math.Exp(logGamma[t, i]);
                gSum   += g;
                wMean  += g * obs[t];
            }
            if (gSum > EPS) _mu[i] = wMean / gSum;

            double wVar = 0;
            for (int t = 0; t < T; t++)
            {
                double g = Math.Exp(logGamma[t, i]);
                double d = obs[t] - _mu[i];
                wVar += g * d * d;
            }
            _sigma[i] = gSum > EPS
                ? Math.Sqrt(Math.Max(wVar / gSum, EPS))
                : _sigma[i];
        }
    }

    // ── State alignment ───────────────────────────────────────────────────

    /// <summary>
    /// Maps raw HMM state indices 0/1/2 to Bull/Bear/Sideways by sorting learned means.
    /// Highest μ = Bull (most positive return), lowest μ = Bear, middle = Sideways.
    /// </summary>
    private void AlignStatesToMarket()
    {
        int[] order = { 0, 1, 2 };
        Array.Sort(order, (x, y) => _mu[y].CompareTo(_mu[x]));  // descending by mean

        _stateMap[order[0]] = MarketState.Bull;      // highest mean
        _stateMap[order[1]] = MarketState.Sideways;  // middle mean
        _stateMap[order[2]] = MarketState.Bear;      // lowest mean
    }

    // ── Math utilities ────────────────────────────────────────────────────

    private static double LogGaussian(double x, double mu, double sigma)
    {
        double s = sigma + 1e-12;
        double d = (x - mu) / s;
        // -0.5 * log(2π) ≈ -0.9189385332
        return -0.9189385332 - Math.Log(s) - 0.5 * d * d;
    }

    private static double LogSumExp(double[] a)
    {
        double max = double.NegativeInfinity;
        foreach (double v in a) if (v > max) max = v;
        if (double.IsNegativeInfinity(max)) return double.NegativeInfinity;
        double sum = 0;
        foreach (double v in a) sum += Math.Exp(v - max);
        return max + Math.Log(sum + EPS);
    }

    /// <summary>LogSumExp over a single row of a [T, N] matrix.</summary>
    private static double LogSumExpRow(double[,] m, int row)
    {
        double max = double.NegativeInfinity;
        for (int j = 0; j < N; j++) if (m[row, j] > max) max = m[row, j];
        if (double.IsNegativeInfinity(max)) return double.NegativeInfinity;
        double sum = 0;
        for (int j = 0; j < N; j++) sum += Math.Exp(m[row, j] - max);
        return max + Math.Log(sum + EPS);
    }

    private static double SegmentMean(double[] sorted, int from, int to)
    {
        if (from >= to || from >= sorted.Length) return 0;
        int end = Math.Min(to, sorted.Length);
        double s = 0;
        for (int i = from; i < end; i++) s += sorted[i];
        return s / (end - from);
    }

    private static double StdDev(double[] a)
    {
        if (a.Length < 2) return 0.01;
        double mean = 0;
        foreach (double v in a) mean += v;
        mean /= a.Length;
        double var = 0;
        foreach (double v in a) { double d = v - mean; var += d * d; }
        return Math.Sqrt(var / (a.Length - 1));
    }
}
