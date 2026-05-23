# Changelog

## 1.0.5 — 2026-05-23
- **Panel Corner** esteso da 4 a 9 posizioni di ancoraggio (griglia 3×3):
  TopLeft, TopRight, BottomLeft, BottomRight (invariati) +
  TopCenter, BottomCenter, MiddleLeft, MiddleRight, MiddleCenter (nuovi).
  Per le posizioni Center/Middle, PanelOffsetX e PanelOffsetY funzionano
  come spostamenti con segno (+/−) dal punto di ancoraggio anziché distanza
  dal bordo. Nessun cambiamento ai valori enum esistenti (0–3).

## 1.0.4 — 2026-05-23
- Aggiunto **RegimeBandRenderer**: disegna sul grafico il rettangolo ±Threshold%
  attorno al close di LookbackPeriod giorni fa. Il prezzo corrente dentro il
  rettangolo → SIDE; sopra la banda superiore → BULL; sotto la banda inferiore → BEAR.
  Colore: blu (SIDE), verde (BULL), rosso (BEAR). Linea tratteggiata grigia
  al close di riferimento (anchor neutrale). Il rettangolo si rinnova ad ogni
  barra daily e viene rimosso allo stop del bot.
  File: `MarkovChain/UI/RegimeBandRenderer.cs` (nuova classe).

## 1.0.3 — 2026-05-23
- Aggiunto parametro **Matrix Bars** (default 200, range 50–2000, gruppo Data):
  la matrice di transizione 3×3 viene costruita solo sulle ultime N barre classificate.
  Il classificatore etichetta comunque tutta la storia disponibile (necessaria per
  il backtest walk-forward), ma i conteggi di transizione usano solo la finestra recente.
  Questo elimina la dominanza storica di SIDE (0.93+) causata da anni di consolidamento
  che diluivano il segnale corrente.

## 1.0.2 — 2026-05-23
- Aggiunto parametro **Regime Threshold %** (default 5, range 1–30, gruppo Data):
  configura la soglia ±N% del modello base per asset diversi.
  Equity/indici: 5%. Crypto (BTC/ETH): 10–15%. Forex: 1–3%.
- Aggiunto parametro **HMM Normalize (z-score)** (default false, gruppo Data):
  divide ogni osservazione HMM per la deviazione standard rolling degli ultimi
  max(20, HmmWindowDays×4) ritorni. Rende l'HMM asset-agnostic: i cluster
  Bull/Bear/Sideways rappresentano "sopra/sotto/vicino la media" in unità di σ
  invece di percentuali assolute. Raccomandato ON per crypto.
- Fix: soglia regime non più hardcodata a ±5% — ora proviene dal nuovo parametro.

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
