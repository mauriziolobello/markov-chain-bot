using cAlgo.API;
using LibraryLog;
using MarkovChainBot;

namespace cAlgo.Robots;

/// <summary>
/// MarkovChain — analytics-only bot using a Gaussian HMM + Markov chain transition matrix
/// to classify daily market regimes and forecast probabilistic outcomes.
/// No trade execution. All analysis is driven by daily bars regardless of chart timeframe.
/// </summary>
[Robot(AccessRights = AccessRights.None, AddIndicators = false)]
public partial class MarkovChain : Robot
{
    // ── Services ──────────────────────────────────────────────────────────
    private ILogger?                _logger;
    private Bars?                   _dailyBars;
    private MarketRegimeClassifier? _classifier;
    private GaussianHmm?            _hmm;
    private TransitionMatrix?       _transMatrix;
    private ForecastEngine?         _forecastEngine;
    private SignalGenerator?        _signalGenerator;
    private BacktestAccuracy?       _backtest;
    private MarkovPanelRenderer?    _panel;

    // ── Last computed results (refreshed on each daily bar close) ─────────
    private ForecastResult[]? _forecasts;
    private SignalResult?     _signal;
    private double            _hitRate;
    private int               _sampleCount;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>Initialises all services and runs the first analysis pass.</summary>
    protected override void OnStart()
    {
        // 1. Logger — DebugMode guards call sites below; the logger itself always writes
        _logger = new CTraderLogger((_, msg) => Print(msg));
        _logger.LogInformation(nameof(OnStart), "MarkovChain v1.0.0 starting…");

        // 2. Optimisation guard — never draw UI during parameter sweeps
        bool canDraw = RunningMode != RunningMode.Optimization;

        // 3. Load separate daily series — works on any chart timeframe
        _dailyBars = MarketData.GetBars(TimeFrame.Daily);

        // 4. Compose services — inject only specific dependencies (DIP, per CLAUDE.md)
        _classifier      = new MarketRegimeClassifier(_dailyBars, LookbackPeriod, RegimeThresholdPct, _logger);
        _transMatrix     = new TransitionMatrix(_logger);
        _hmm             = new GaussianHmm(maxIterations: 100, convergenceThreshold: 1e-6, _logger);
        _forecastEngine  = new ForecastEngine(_transMatrix, _logger);
        _signalGenerator = new SignalGenerator(_logger);
        _backtest        = new BacktestAccuracy(_logger);

        // 5. Initial computation on existing history
        RunAnalysis();

        // 6. Create and populate panel
        _panel = new MarkovPanelRenderer(
            Chart, canDraw, Corner,
            PanelOffsetX, PanelOffsetY,
            TableAlpha, MixAlpha, ForecastDays, _logger);
        _panel.Initialize();
        UpdatePanel();

        // 7. Subscribe to daily bar opened event for future updates
        //    (fires once per calendar day when the previous day's bar closes)
        _dailyBars.BarOpened += OnDailyBarOpened;

        _logger.LogInformation(nameof(OnStart), "MarkovChain ready.");
    }

    /// <summary>Called when the bot is stopped or removed from the chart.</summary>
    protected override void OnStop()
    {
        if (_dailyBars is not null)
            _dailyBars.BarOpened -= OnDailyBarOpened;

        _panel?.Destroy();
        _logger?.Dispose();
    }

    // ── Event handlers ────────────────────────────────────────────────────

    /// <summary>
    /// Fires when a new daily bar opens (= the previous daily bar just closed).
    /// This is the sole analysis trigger — OnBar() is not used for computation.
    /// </summary>
    private void OnDailyBarOpened(BarOpenedEventArgs args)
    {
        if (DebugMode)
            _logger?.LogDebug(nameof(OnDailyBarOpened),
                $"New daily bar opened. Daily count: {_dailyBars?.Count}");

        RunAnalysis();
        UpdatePanel();
    }

    // ── Core analysis pipeline ─────────────────────────────────────────────

    /// <summary>
    /// Full pipeline: classify → build matrix → train HMM → decode → forecast → signal → backtest.
    /// Re-runs on every daily bar close to keep the model current.
    /// </summary>
    private void RunAnalysis()
    {
        if (_classifier is null || _transMatrix is null || _hmm is null
         || _forecastEngine is null || _signalGenerator is null || _backtest is null)
            return;

        // 1. Classify all completed daily bars using the base model (rolling return)
        MarketState[] states = _classifier.ClassifyAll();
        if (states.Length < LookbackPeriod * 2)
        {
            _logger?.LogWarning(nameof(RunAnalysis),
                $"Need {LookbackPeriod * 2} bars minimum. Have {states.Length}. Skipping.");
            return;
        }

        // 2. Build transition matrix from base-model state sequence
        _transMatrix.Build(states);

        // 3. Build HMM observations.
        //    Base: rolling log-return log(close[t] / close[t−HmmWindowDays]).
        //    Optional z-score normalisation: divide each obs by rolling std of
        //    the last stdWindow daily log-returns to make the model asset-agnostic
        //    (converts absolute % into "standard deviations from recent mean").
        double[] closes     = _classifier.GetRecentClosePrices(HistoryBars + HmmWindowDays);
        double[] logReturns = ComputeRollingLogReturns(closes, HmmWindowDays);

        if (HmmNormalize && logReturns.Length >= 2)
            logReturns = NormalizeToZScore(logReturns);

        // 4. Train HMM on log-returns; Decode() sets _hmm.CurrentState via Viterbi
        if (logReturns.Length >= 10)
        {
            _hmm.Train(logReturns);
            _hmm.Decode(logReturns);  // must call after Train() to update CurrentState
        }
        else
        {
            _logger?.LogWarning(nameof(RunAnalysis),
                "Insufficient log-returns for HMM training.");
        }

        // 5. Forecast D+1..D+ForecastDays using base-model current state
        _forecasts = _forecastEngine.Compute(_classifier.CurrentState, ForecastDays);

        // 6. Signal: P(Bull)−P(Bear) with HMM confirmation
        _signal = _signalGenerator.Compute(
            _forecasts[0],
            _classifier.CurrentState,
            _hmm.CurrentState);

        // 7. Walk-forward D+1 hit rate (cheap enough to run on every daily bar)
        (_hitRate, _sampleCount) = _backtest.Compute(states, LookbackPeriod);

        if (DebugMode)
            _logger?.LogDebug(nameof(RunAnalysis),
                $"Done. Base={_classifier.CurrentState}(thr=±{RegimeThresholdPct}%) "
              + $"HMM={_hmm.CurrentState}(win={HmmWindowDays}d,norm={HmmNormalize},obs={logReturns.Length}) "
              + $"Signal={_signal.Direction}({_signal.Strength:+0.000;-0.000;0.000}) "
              + $"Accuracy={_hitRate:P1}");
    }

    /// <summary>Pushes the latest results to the panel renderer.</summary>
    private void UpdatePanel()
    {
        if (_panel is null || _transMatrix is null || _forecasts is null || _signal is null)
            return;

        _panel.Update(
            _transMatrix.Matrix,
            _classifier!.CurrentState,
            _hmm!.CurrentState,
            _forecasts,
            _signal,
            _hitRate,
            _sampleCount);
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    /// <summary>
    /// Computes daily log-returns from close prices (oldest first).
    /// Returns array of length closes.Length − 1.
    /// </summary>
    private static double[] ComputeLogReturns(double[] closes)
    {
        if (closes.Length < 2) return Array.Empty<double>();
        var ret = new double[closes.Length - 1];
        for (int i = 1; i < closes.Length; i++)
        {
            double prev = closes[i - 1];
            double curr = closes[i];
            ret[i - 1] = prev > 0 ? Math.Log(curr / prev) : 0.0;
        }
        return ret;
    }

    /// <summary>
    /// Normalises an array of log-returns to z-scores using a rolling window.
    /// Each element is divided by the standard deviation of the surrounding
    /// max(20, 4×HmmWindowDays) observations, making the HMM asset-agnostic.
    ///
    /// Why this helps on high-volatility assets (crypto):
    /// The Gaussian clusters in the HMM are calibrated to the training data.
    /// On BTC, the "Bear" cluster might be centred at −30 % per 5-day window
    /// because of historical crashes. A mild −2 % decline falls in Sideways.
    /// After z-scoring, the clusters become "above/below/near 0 σ", so the
    /// same mild −2 % decline reads as −1.5 σ in a low-volatility month and
    /// correctly lands in the Bear cluster for that context.
    /// </summary>
    private static double[] NormalizeToZScore(double[] returns)
    {
        const double MinStd = 1e-10;
        int n      = returns.Length;
        int stdWin = Math.Max(20, n / 4);   // rolling std window: at least 20 bars
        var result = new double[n];

        for (int i = 0; i < n; i++)
        {
            int from  = Math.Max(0, i - stdWin + 1);
            int count = i - from + 1;

            // Compute mean and std over [from..i]
            double sum = 0;
            for (int k = from; k <= i; k++) sum += returns[k];
            double mean = sum / count;

            double var = 0;
            for (int k = from; k <= i; k++) { double d = returns[k] - mean; var += d * d; }
            double std = count > 1 ? Math.Sqrt(var / (count - 1)) : MinStd;

            result[i] = std > MinStd ? returns[i] / std : 0.0;
        }
        return result;
    }

    /// <summary>
    /// Computes N-day rolling log-returns from close prices (oldest first).
    /// Each observation[i] = log(closes[i + windowDays] / closes[i]).
    /// Returns array of length closes.Length − windowDays.
    ///
    /// Why rolling returns instead of 1-day returns for the HMM:
    /// The HMM's Gaussian clusters are calibrated to the distribution of the
    /// training data. On assets with high volatility (e.g. BTC), 1-day returns
    /// overlap heavily across regimes, making Bull/Bear/Sideways hard to separate.
    /// An N-day window acts as a low-pass filter: it accumulates directional drift
    /// while partially cancelling same-amplitude symmetric noise. The result is
    /// better cluster separation and a more sensitive regime decoder.
    /// </summary>
    private static double[] ComputeRollingLogReturns(double[] closes, int windowDays)
    {
        int n = Math.Max(1, windowDays);
        if (closes.Length <= n) return Array.Empty<double>();

        var ret = new double[closes.Length - n];
        for (int i = 0; i < ret.Length; i++)
        {
            double past = closes[i];
            double curr = closes[i + n];
            ret[i] = past > 0 ? Math.Log(curr / past) : 0.0;
        }
        return ret;
    }
}
