# Changelog

## 1.0.1 — 2026-05-22
- Aggiunto parametro **HMM Window Days** (default 5, range 1–20) nel gruppo Data.
- Il modello HMM viene ora addestrato su log-return rolling a N giorni
  (`log(close[t] / close[t-N])`) invece di log-return giornalieri singoli.
  Questo migliora la separazione tra i cluster Bull/Bear/Sideways su asset
  ad alta volatilità (es. BTC), dove i ritorni a 1 giorno si sovrappongono
  eccessivamente rendendo difficile distinguere i regimi.

## 1.0.0 — 2026-05-22
- Implementazione iniziale: HMM Gaussiano (Baum-Welch + Viterbi), catena di Markov,
  pannello Canvas con matrice 3×3, forecast n-step, segnale e backtest accuracy.
