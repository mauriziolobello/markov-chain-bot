## 🏗️ Principi Architetturali

### Organizzazione Classi

1. **Classi Separate per Entità**
   - Ogni entità ha la propria classe dedicata
   - File separato per ogni classe
   - Favorisce testabilità, manutenibilità e separazione delle responsabilità

2. **Partial Class solo per la Classe Principale**
   - La classe dell'indicatore può essere suddivisa in partial class

3. **Non usare Partial Class per Entità aggiuntive**

Questo approccio:
- Elimina duplicazione di codice
- Facilita l'aggiunta di nuove formazioni
- Mantiene consistenza visiva tra formazioni simili

### Namespace

1. **Classe Principale in `cAlgo`**
   - Se crei classi (oltre quella dell'indicatore), definisci un namespace separato da `cAlgo` (richiesto da cTrader)
   - Tutte le classi aggiuntive useranno il nuovo il namespace
   - Questo separa la logica di dominio dal framework cTrader
   - Importare con `using <namespace>;` nei file che ne hanno bisogno

### SOLID Principles

Il progetto segue rigorosamente i principi SOLID:

1. **Single Responsibility Principle (SRP)**
2. **Open/Closed Principle**
3. **Liskov Substitution Principle**
4. **Interface Segregation Principle**
5. **Dependency Inversion Principle**

#### DIP in cTrader: non iniettare `Robot` nelle classi di servizio

**REGOLA FONDAMENTALE**: Non passare mai l'istanza `Robot` intera alle classi di servizio (manager, calculator, chart manager, ecc.). `Robot` è un God-object con decine di responsabilità. Dipendere da esso viola DIP e rende le classi non testabili.

**Pattern corretto — inietta solo ciò che serve:**

```csharp
// ❌ SBAGLIATO: dipendenza dall'intera istanza Robot
public class MyManager
{
    private readonly Robot _robot;
    public MyManager(Robot robot) { _robot = robot; }

    // usa _robot.Symbol.Bid, _robot.ModifyPosition(), _robot.Chart, ...
}

// ✅ CORRETTO: dipendenze specifiche e minime
public class MyManager
{
    private readonly Symbol _symbol;          // per Bid/Ask/PipSize
    private readonly ITradeExecutor _executor; // per ModifyPosition
    private readonly Chart _chart;            // per operazioni grafiche

    public MyManager(Symbol symbol, ITradeExecutor executor, Chart chart) { ... }
}
```

**Linee guida pratiche:**

| Necessità | Cosa iniettare |
|---|---|
| Prezzi correnti (Bid/Ask) e pip | `Symbol symbol` |
| Disegno su chart | `Chart chart` |
| Accesso alle candele | `Bars bars` |
| Modifica posizioni broker | `ITradeExecutor` (interfaccia custom) |
| Calcoli su costi (commissioni, swap) | `ICostCalculator` (interfaccia custom) |
| Log | `ILogService` (già presente nel progetto) |

**Adattatore per `ModifyPosition`:**

Quando un manager ha bisogno di modificare posizioni, crea un'interfaccia sottile e il relativo adattatore — una volta sola per tutto il progetto:

```csharp
// Interfaccia (Management/ITradeExecutor.cs)
public interface ITradeExecutor
{
    TradeResult ModifyPosition(Position position, double? stopLoss,
        double? takeProfit, ProtectionType protectionType);
}

// Adattatore (Management/RobotTradeExecutor.cs)
internal class RobotTradeExecutor : ITradeExecutor
{
    private readonly Robot _robot;
    internal RobotTradeExecutor(Robot robot) { _robot = robot; }
    public TradeResult ModifyPosition(Position pos, double? sl, double? tp, ProtectionType pt)
        => _robot.ModifyPosition(pos, sl, tp, pt);
}

// In OnStart — unico punto di accoppiamento con Robot
_myManager = new MyManager(Symbol, new RobotTradeExecutor(this), _logService);
```

**Perché è importante:**
- Le classi di servizio diventano testabili (si possono passare mock di `ITradeExecutor`, `Symbol`, ecc.)
- Il punto di accoppiamento con `Robot` è concentrato in `OnStart`, non disperso nel codice
- Aggiungere una nuova implementazione (es. per backtesting) richiede solo un nuovo adattatore

---

## 🎯 Regole di Codifica

### Naming Conventions

- **Campi privati:** `_camelCase` (es. `_congestionZones`)
- **Proprietà pubbliche:** `PascalCase` (es. `CongestionColor`)
- **Metodi:** `PascalCase` (es. `ProcessBar()`)
- **Parametri:** `PascalCase` nei `[Parameter]`, `camelCase` nei metodi
- **Costanti:** `UPPER_CASE` (es. `MIN_BARS_IN_CONGESTION`)

### Documentazione

- **Tutti i metodi** devono avere `/// <summary>` XML documentation
- **Parametri complessi** devono avere descrizioni dettagliate
- **Logica non ovvia** deve essere commentata inline

### Gestione Oggetti Grafici

**IMPORTANTE:** Tutti gli oggetti grafici (`Chart.Draw*`) devono essere tracciati:
Prevedi un parametro che limiti il numero di candele che mostrano la formazione di oggetti grafici

```csharp
// ✅ CORRETTO
zone.Rectangle = Chart.DrawRectangle(...);

// ❌ SBAGLIATO (memory leak)
Chart.DrawRectangle(...);
```

**Cleanup obbligatorio:**
```csharp
if (zone.Rectangle != null)
{
    Chart.RemoveObject(zone.Rectangle.Name);
    zone.Rectangle = null;
}
```

**Fill e trasparenza degli oggetti chart (`DrawRectangle`, ecc.):**

Gli oggetti grafici chart (non i controlli Canvas) non hanno una proprietà `FillColor` separata. Per riempire un rettangolo è sufficiente impostare `IsFilled = true`: il riempimento usa automaticamente il colore del bordo.

Per controllare la trasparenza, applicare l'alpha **al colore passato a `DrawRectangle`**, non tramite una proprietà separata.

```csharp
// ✅ CORRETTO — trasparenza applicata al colore del bordo
int alpha = (int)(opacityPercent * 255.0 / 100.0);
var rect = Chart.DrawRectangle(name, t1, y1, t2, y2,
    Color.FromArgb(alpha, color.R, color.G, color.B));
rect.IsFilled = true;

// ❌ SBAGLIATO — FillColor non esiste sugli oggetti chart
rect.FillColor = someColor;

// ❌ SBAGLIATO — la trasparenza non viene applicata se si passa il colore pieno
var rect = Chart.DrawRectangle(name, t1, y1, t2, y2, color); // colore a 100%
rect.IsFilled = true;
```

**Nota:** `FillColor` esiste solo sui controlli Canvas (`Rectangle`, `Border`, ecc.), non sugli oggetti `Chart.Draw*`.

---

## 🔢 Versioning

### Formato Semantico: `major.minor.fix`

- **major:** Cambiamenti architetturali importanti
- **minor:** Nuove funzionalità
- **fix:** Bug fix e piccoli miglioramenti

### Regole di Aggiornamento

**SEMPRE** aggiornare versione quando:
- ✅ Aggiungi nuove funzionalità
- ✅ Correggi bug
- ✅ Modifichi comportamento esistente
- ✅ Aggiungi parametri configurabili

**Aggiorna il changelog in un file separato dal nome CHANGELOG.md:**
```csharp
/// Changelog:
/// 1.1.0 - Refactoring: classe base RangeFormation, rendering unificato
/// 1.0.0 - Implementazione iniziale: Congestion e Trading Range
```

---

## 📝 Unicode e Logging

### Caratteri Speciali

**SEMPRE** usare escape sequences `\uxxxx`:

```csharp
// ✅ CORRETTO
Print("\u2554\u2550\u2557"); // ╔═╗

// ❌ SBAGLIATO (problemi rendering)
Print("╔═╗");
```

### Box Drawing Characters

- Top-left: `\u2554` (╔)
- Top-right: `\u2557` (╗)
- Bottom-left: `\u255A` (╚)
- Bottom-right: `\u255D` (╝)
- Horizontal: `\u2550` (═)
- Vertical: `\u2551` (║)

---

## 🔧 cTrader Specifics

### Vincoli Piattaforma

- **AccessRights:** `None` (no file system, no network)
- **LocalStorage:** Disponibile per persistenza dati
- **No reflection:** Limitazioni su runtime type inspection

### Best Practices

1. **Evita `Thread.Sleep()`** → Blocca UI
2. **Non rimuovere oggetti grafici** se non necessario
3. **Traccia tutti gli oggetti** creati dinamicamente

### Canvas — Pannello a Scomparsa con Chart Custom

#### API Canvas disponibili in cTrader

**Controlli UI disponibili:**
- `Canvas` — contenitore con posizionamento assoluto (`Left`, `Top` dei figli)
- `StackPanel` — layout a flusso (orizzontale/verticale)
- `Button` — click handler
- `TextBlock` — testo (`ForegroundColor`, `FontSize`, `Left`, `Top`)
- ~~`Slider`~~ — **NON ESISTE** nell'API cTrader Canvas. Usare `[−]`/`[+]` Button come alternativa.
- `Border` — rettangolo con bordo (`BackgroundColor`, `BorderColor`)
- `Rectangle` — forma (`FillColor`, `StrokeColor`, `StrokeThickness`, `Left`, `Top`, `Width`, `Height`)
- `Line` — segmento diagonale (`X1`, `Y1`, `X2`, `Y2`, `StrokeColor`, `StrokeThickness`)

**Limitazione critica `IndicatorArea`:** ha solo `DrawHorizontalLine`, `DrawText`, `DrawIcon`, `DrawStaticText`, `SetYRange`. **Non ha `DrawTrendLine`**. Per segmenti diagonali in un pannello custom, usare `Line` dentro un Canvas (non `IndicatorArea`).

#### Pattern: Canvas a scomparsa (toggle expand/collapse)

```csharp
// Inizializzazione (in Initialize())
private Canvas   _canvas;
private Button   _toggleButton;
private bool     _isExpanded = false;

private void InitializeCanvas()
{
    Color bgColor = Color.FromArgb(
        (int)(255 * BackgroundOpacity / 100.0),
        BackgroundColor.R, BackgroundColor.G, BackgroundColor.B);

    _canvas = new Canvas
    {
        Width           = CanvasWidth,
        Height          = CANVAS_HEIGHT_COLLAPSED,
        BackgroundColor = bgColor,
        IsHitTestVisible = true
    };

    PositionCanvas();  // imposta HorizontalAlignment, VerticalAlignment, Margin

    _toggleButton = new Button
    {
        Width      = CanvasWidth - 2 * PADDING,
        Height     = BUTTON_HEIGHT,
        Text       = "\u25BC My Indicator",
        Margin     = new Thickness(PADDING, PADDING, 0, 0),
        BackgroundColor = Color.FromArgb(150, 50, 50, 50),
        ForegroundColor = TextColor
    };
    _toggleButton.Click += OnToggleClick;
    _canvas.AddChild(_toggleButton);

    Chart.AddControl(_canvas);
}

private void OnToggleClick(ButtonClickEventArgs _)
{
    _isExpanded = !_isExpanded;
    if (_isExpanded) ExpandCanvas();
    else             CollapseCanvas();
}

private void ExpandCanvas()
{
    _canvas.Height = CANVAS_HEIGHT_EXPANDED;
    _toggleButton.Text = "\u25B2 My Indicator";
    _canvas.AddChild(_contentPanel);   // aggiunge figli espansi
    RefreshAll();
}

private void CollapseCanvas()
{
    _canvas.Height = CANVAS_HEIGHT_COLLAPSED;
    _toggleButton.Text = "\u25BC My Indicator";
    _canvas.RemoveChild(_contentPanel); // rimuove figli — rimangono in memoria
}
```

**Note pattern:**
- I figli rimossi con `RemoveChild` **non vengono distrutti** — il loro stato persiste.
  Quando si aggiungono di nuovo con `AddChild`, i dati sono intatti.
- Il Canvas usa posizionamento assoluto: i figli diretti usano `Left`/`Top` per posizione.
- `StackPanel` usa `Margin` per spaziatura tra elementi (non `Left`/`Top`).
- Cleanup in `Destroy()`: `Chart.RemoveControl(_canvas)`.

#### Pattern: Custom chart nel Canvas con Line

```csharp
private Canvas             _chartCanvas;
private List<ControlBase>  _chartLines = new();

private void InitializeChartCanvas()
{
    _chartCanvas = new Canvas
    {
        Width  = chartWidth,
        Height = CHART_HEIGHT,
        Left   = PADDING,
        Top    = yOffset
    };

    // Sfondo scuro
    _chartCanvas.AddChild(new Rectangle
    {
        Width  = chartWidth, Height = CHART_HEIGHT,
        FillColor   = Color.FromArgb(40, 30, 30, 30),
        StrokeColor = Color.FromArgb(80, 80, 80, 80),
        StrokeThickness = 1
    });
}

private void RedrawLines(List<(double x1,double y1,double x2,double y2,Color c)> segments)
{
    // Cleanup
    foreach (var ln in _chartLines)
        _chartCanvas.RemoveChild(ln);
    _chartLines.Clear();

    // Ridisegna
    foreach (var (x1, y1, x2, y2, color) in segments)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            StrokeColor     = color,
            StrokeThickness = 2
        };
        _chartCanvas.AddChild(line);
        _chartLines.Add(line);
    }
}
```

**Fallback se `Line` non disponibile:** usare Bresenham con Rectangle 2×2px:

```csharp
private void DrawLineBresenham(Canvas canvas, int x1, int y1, int x2, int y2, Color color)
{
    int dx = Math.Abs(x2-x1), sx = x1<x2 ? 1 : -1;
    int dy = -Math.Abs(y2-y1), sy = y1<y2 ? 1 : -1;
    int err = dx+dy, x = x1, y = y1;
    while (true)
    {
        canvas.AddChild(new Rectangle
            { Width=2, Height=2, FillColor=color, Left=x-1, Top=y-1 });
        if (x==x2 && y==y2) break;
        int e2 = 2*err;
        if (e2 >= dy) { err += dy; x += sx; }
        if (e2 <= dx) { err += dx; y += sy; }
    }
}
```

#### Canvas e IsOverlay

- `IsOverlay = true` (overlay) → `Chart.AddControl()` aggiunge il Canvas sul chart principale.
  Il Canvas galleggia sopra le candele nel corner configurato.
- `IsOverlay = false` (sub-pane) → `Chart.AddControl()` aggiunge il Canvas sul **chart principale**,
  non nel sub-pane. Il sub-pane mostra solo le `[Output]` series.
- **Conclusione pratica:** se vuoi un Canvas con chart custom senza interferire con le candele,
  usa `IsOverlay = true` e posiziona il Canvas in un corner.

---

### ChartArea.SetYRange() - Limitazione Trade Environment

**CRITICO:** `ChartArea.SetYRange()` ha comportamenti diversi tra Algo e Trade:

**Ambiente Algo (Automate):**
- ✅ `ChartArea.SetYRange()` funziona correttamente
- ✅ Auto-zoom dinamico funziona come previsto

**Ambiente Trade:**
- ❌ `ChartArea.SetYRange()` viene **ignorato silenziosamente**
- ❌ Tutte le barre vengono disegnate sovrapposte nello stesso spazio verticale
- ❌ Risultato: grafico illeggibile con elementi impilati

```csharp
// Questo codice funziona SOLO in Algo environment
ChartArea.SetYRange(bottomY, topY);  // Ignorato in Trade!
```

**Soluzione:**
```csharp
// Disabilitare auto-zoom per Trade environment
[Parameter("Enable Auto Zoom", DefaultValue = false, Group = "Zoom")]
public bool EnableAutoZoom { get; set; }

// Aggiungere warning nel log
if (EnableAutoZoom)
{
    Print("WARNING: Auto Zoom may not work in Trade environment");
}
```

**Best practice:**
- Default `EnableAutoZoom = false` per compatibilità Trade
- Documentare la limitazione nel README
- Aggiungere warning nel log se auto-zoom è abilitato
- Utente può abilitare manualmente auto-zoom se usa solo Algo environment

### DrawText — Controllo Font Size

`Chart.DrawText(...)` restituisce un oggetto `ChartText` sul quale è possibile impostare `FontSize`:

```csharp
// ✅ Pattern compatto: imposta font size inline
_chart.DrawText(name, "Stop Loss", barIndex, price, Color.OrangeRed).FontSize = 10;

// Equivalente esplicito:
ChartText ct = _chart.DrawText(name, "Stop Loss", barIndex, price, Color.OrangeRed);
ct.FontSize = 10;
```

Se non stai memorizzando il riferimento (perché usi `RemoveObject(name)` per il cleanup), il pattern inline è preferibile.

---

### LocalStorage Key Validation

**CRITICO:** Le chiavi di LocalStorage hanno regole di validazione molto rigide:

**Caratteri permessi:**
- ✅ Lettere latine (a-z, A-Z)
- ✅ Numeri (0-9)
- ✅ Spazi (ma NON all'inizio o alla fine)

**Caratteri NON permessi:**
- ❌ Underscore `_`
- ❌ Trattini `-`
- ❌ Slash `/`
- ❌ Qualsiasi altro carattere speciale

```csharp
// ❌ SBAGLIATO - Genera errore di validazione
string key = "Footprint_BTCUSD";  // underscore non permesso
string key = "Footprint-BTCUSD";  // trattino non permesso

// ✅ CORRETTO - Usa spazi come separatori
string key = "Footprint BTCUSD";  // spazio permesso
string key = "FootprintBTCUSD";   // nessun separatore

// ✅ CORRETTO - Sanificazione del nome simbolo
public static string GenerateStorageKey(string symbol)
{
    StringBuilder sanitized = new StringBuilder();
    foreach (char c in symbol)
    {
        if (char.IsLetterOrDigit(c))
            sanitized.Append(c);
    }
    return $"Footprint {sanitized}";  // Usa spazio come separatore
}
```

**Errore tipico se si viola la regola:**
```
[Storage] Error saving: Le chiavi possono contenere solamente caratteri latini,
numeri e spazi. Non sono consentiti spazi all'inizio e alla fine della chiave. (Parameter 'key')
```

**Best practice:**
- Sanifica sempre i nomi dei simboli rimuovendo caratteri speciali
- Usa spazi o nessun separatore nelle chiavi
- Testa la generazione della chiave con simboli che contengono `/`, `-`, o altri caratteri speciali

### Gestione Indici Candele

**IMPORTANTE:** cTrader reindicizza periodicamente le candele durante refresh/reload del grafico. Gli indici delle barre **NON sono stabili** nel tempo.

**Regola fondamentale:**
- ❌ **NON memorizzare** indici delle candele (`int index`)
- ✅ **Memorizzare** DateTime delle candele (`DateTime barTime`)

```csharp
// ❌ SBAGLIATO - L'indice può cambiare dopo refresh
public int MeasuringBarIndex { get; }

// ✅ CORRETTO - Il DateTime è stabile
public DateTime MeasuringBarTime { get; }
```

**Quando serve l'indice**, ricavarlo al momento:
```csharp
int index = Bars.OpenTimes.GetIndexByTime(barTime);
```

Questo garantisce che l'indicatore funzioni correttamente anche dopo lunghi periodi di esecuzione.

---

## ⚠️ Common Pitfalls

### Memory Leaks

```csharp
// ❌ MEMORY LEAK
for (int i = 0; i < 1000; i++)
{
    Chart.DrawLine(...); // Non tracciato!
}

// ✅ CORRETTO
private List<ChartObject> _lines = new List<ChartObject>();
_lines.Add(Chart.DrawLine(...));
```

### Index Out of Bounds

```csharp
// ❌ PERICOLOSO
int idx = zone.MeasuringBarIndex + 2;
var time = Bars.OpenTimes[idx]; // Può crashare!

// ✅ SICURO
if (zone.MeasuringBarIndex + 2 < Bars.Count)
{
    var time = Bars.OpenTimes[zone.MeasuringBarIndex + 2];
}
```

### Performance

```csharp
// ❌ LENTO (ogni tick)
foreach (var zone in AllZones)
{
    Chart.RemoveObject(...);
    Chart.DrawRectangle(...);
}

// ✅ VELOCE (solo se necessario)
if (zone.NeedsUpdate)
{
    UpdateRectangle(zone);
}
```

---

## 🤖 Pattern Appresi — After The Wick Bot

Questa sezione documenta situazioni specifiche di cTrader/cAlgo scoperte durante lo sviluppo del bot, non documentate altrove.

### Indicizzazione Candele — Due Convenzioni Diverse

**CRITICO:** cTrader usa **due** convenzioni di indicizzazione diverse a seconda del tipo di serie. Confonderle causa bug silenziosi difficili da diagnosticare.

| Contesto | `[0]` | `[Count-1]` | Note |
|---|---|---|---|
| `Bars.OpenPrices[i]` (Robot) | barra **più vecchia** in memoria | barra **forming** (più recente) | Indici **sequenziali** |
| `IndicatorDataSeries[i]` (es. ATR, EMA) | barra **più vecchia** | barra **più recente** | Indici **sequenziali** |
| Bar index per Chart draw (DrawTrendLine, ecc.) | barra più vecchia | barra più recente | Indici **sequenziali** |

Tutte e tre le forme usano la stessa convenzione sequenziale. `LastValue` = `[Count-1]` su entrambi i tipi.

**Accesso a barre recenti con `Bars.OpenPrices`:**

```csharp
// [Count-1] = forming bar (corrente, ancora aperta)
// [Count-2] = ultima barra completata
// [Count-3] = penultima, ...
// [0]       = barra più vecchia in memoria

double lastClose = _bars.ClosePrices[_bars.Count - 2];   // ✅ ultima barra completata
double prevClose = _bars.ClosePrices[_bars.Count - 3];   // ✅ penultima

// Durata di una barra (positiva):
TimeSpan barDur = _bars.OpenTimes[_bars.Count - 1] - _bars.OpenTimes[_bars.Count - 2];  // ✅ > 0

// Ultimi 5 completed bars allineati con ema.Result:
for (int i = 1; i <= 5; i++)
{
    double hi     = _bars.HighPrices[_bars.Count - 1 - i];   // ✅ i barre fa
    int    emaIdx = _ema.Result.Count - 1 - i;                // ✅ stessa barra
}
```

**`Last(k)`:** `_bars.Last(0)` = ultima barra formata (= `_bars.ClosePrices[_bars.Count - 2]`, non `[Count-1]`). È un'API di accesso alternativa, non equivalente all'indicizzazione diretta.

**`IndicatorDataSeries` (es. ATR, EMA) — stessa convenzione sequenziale:**

```csharp
// ❌ SBAGLIATO — legge le 5 barre più VECCHIE (tutte NaN se ATR non ancora warmato)
for (int i = 0; i < 5; i++)
    sum += atr.Result[i];

// ✅ CORRETTO — legge le 5 barre più RECENTI
int from = Math.Max(0, atr.Result.Count - 5);
for (int i = from; i < atr.Result.Count; i++)
    sum += atr.Result[i];
```

`LastValue` è equivalente a `[Count-1]` ed è sicuro su entrambi i tipi di serie.

---

### Bot-Managed SL/TP — Pattern per SL/TP senza Broker

Quando si vuole gestire SL/TP lato bot (tick-by-tick) senza impostarli sul broker:

```csharp
// SELL: SL sopra entry, triggerato quando Ask >= SL
//       TP sotto entry, triggerato quando Bid <= TP
// BUY:  SL sotto entry, triggerato quando Bid <= SL
//       TP sopra entry, triggerato quando Bid >= TP
```

**Perché Ask per SELL-SL e Bid per BUY-SL:** il trader paga Ask per chiudere un SELL (acquisto), paga Bid per chiudere un BUY (vendita).

---

### Trailing Stop — Richiede SL Broker Prima dell'Attivazione

**PROBLEMA:** `ModifyPosition(pos, null, null, null, true)` non attiva il TSL se non c'è uno SL broker impostato — il TSL non ha un prezzo da cui partire e rimane inattivo silenziosamente.

**SOLUZIONE:** Passare sempre `slPrice` (prezzo SL bot-managed) quando si attiva il TSL:

```csharp
// ✅ CORRETTO: imposta SL broker E attiva TSL in una sola chiamata
_robot.ModifyPosition(position, slPrice, null, ProtectionType.Absolute, true);
```

Il TSL parte dal `slPrice` indicato e segue il prezzo da lì in poi.

---

### Breakeven Aggiustato per Costi — Formula Completa

Quando si sposta lo SL a "breakeven", `EntryPrice` da solo non basta: la posizione chiusa esattamente a `EntryPrice` risulterà in una perdita netta a causa di commissioni, swap e spread.

**Formula corretta:**

```csharp
private double ComputeBreakevenPrice(Position position)
{
    // PipValue è già per unità (non per lotto): moltiplicare per le unità della posizione
    double pipValueForPos = symbol.PipValue * position.VolumeInUnits;

    // Costi monetari da recuperare:
    //   Commissioni: position.Commissions è negativo → abs × 2 = apertura + stima chiusura
    //   Swap: solo se negativo (costo pagato), non se positivo (ricevuto)
    double commissionCost = Math.Abs(position.Commissions) * 2.0;
    double swapCost       = Math.Max(0.0, -position.Swap);
    double totalMonetary  = commissionCost + swapCost;

    // Converti in distanza di prezzo, poi aggiungi spread come buffer
    double costPrice = (pipValueForPos > 0 ? totalMonetary / pipValueForPos * symbol.PipSize : 0.0)
                       + symbol.Spread;

    return position.TradeType == TradeType.Buy
        ? position.EntryPrice + costPrice    // BUY: BE sopra entry (lo spread fu pagato in apertura)
        : position.EntryPrice - costPrice;   // SELL: BE sotto entry (lo spread è pagato in chiusura)
}
```

**Note:**
- `position.Commissions` è già negativo in cTrader (è un costo) — usare `Math.Abs`
- `position.Swap` può essere positivo (ricevuto) o negativo (pagato) — contare solo la parte negativa
- `symbol.PipValue` è **per unità** (non per lotto): non dividere per `LotSize`; moltiplicare direttamente per `VolumeInUnits`
- Aggiungere `symbol.Spread` come buffer per l'esecuzione alla chiusura

---

### Etichetta Posizione con Timeframe

Includere il timeframe nel label della posizione aperta rende immediatamente identificabile quale monitor ha aperto il trade in cTrader:

```csharp
// ✅ Esempio: "ATW_H1", "ATW_M15"
_label = "ATW_" + timeframeName;
_executor.ExecuteMarketOrder(tradeType, symbolName, volumeInUnits, _label);
```

Il `timeframeName` viene passato al costruttore di `WickTradeManager` da `OnStart` del Robot.

---

### Disegno Grafico — Blocco in Modalità Ottimizzazione

`Chart.DrawHorizontalLine()` e altri metodi grafici bloccano l'esecuzione durante l'ottimizzazione (crash silenzioso o freeze). Proteggere SEMPRE con:

```csharp
private bool CanDrawThings()
    => EnableVisuals && RunningMode != RunningMode.Optimization;

// In ogni metodo di disegno:
if (!_canDraw) return;
```

Passare il flag `canDraw` al costruttore dei renderer (es. `WickChartRenderer`) così i metodi `Draw()` diventano no-op in ottimizzazione.

---

### Event-Driven Per-Bar Cooldown — Pattern per Evitare Rientri Immediati

Dopo la chiusura di una posizione, impedire nuove aperture sulla stessa candela via evento:

```csharp
// Su IPositionTracker
event Action? PositionCleared;

// In ClearPosition():
PositionCleared?.Invoke();

// In TimeframeMonitor:
_positionTracker.PositionCleared += () => _blockedThisBar = true;
// In OnBarOpened: _blockedThisBar = false;
// In OnTick: if (_blockedThisBar) return;
```

Questo approccio è superiore a polling o flag condivisi perché ogni monitor gestisce il suo stato indipendentemente.

---

## 🐙 GitHub — Creazione e Aggiornamento del Repo

### Politica

- Ogni nuovo progetto cAlgo **deve avere un repo GitHub** creato all'inizio del ciclo di sviluppo.
- Il repo va aggiornato (commit + push) **ad ogni variazione della minor release** (es. `1.2.x → 1.3.0`) e comunque ogni volta che si apportano modifiche significative al sorgente.
- **Claude esegue automaticamente commit e push** al momento opportuno. Le istruzioni manuali qui sotto servono come riferimento o per operazioni straordinarie.

---

### Creazione del repo (nuovo progetto)

**Prerequisiti:** `git` e `gh` (GitHub CLI) installati e autenticati (`gh auth login`).

**1. Inizializza git nella cartella del progetto**

```powershell
cd "C:\Users\Maurizio\Documents\cAlgo\Sources\Robots\<NomeProgetto>"
git init
git add .
git commit -m "Initial commit"
```

**2. Crea il repo su GitHub e collega il remote**

```powershell
# Crea repo pubblico (ometti --public per privato)
gh repo create <nome-repo> --public --source . --remote origin --push
```

Oppure, se il repo esiste già su GitHub:

```powershell
git remote add origin https://github.com/mauriziolobello/<nome-repo>.git
git push -u origin main
```

**3. Verifica**

```powershell
git remote -v          # deve mostrare origin → github.com/mauriziolobello/...
gh repo view --web     # apre il browser sul repo
```

---

### Aggiornamento del repo (rilascio minor)

**Procedura standard ad ogni `x.Y.0`:**

```powershell
# 1. Verifica stato
git status
git diff --stat

# 2. Aggiungi tutti i file modificati del progetto
git add "Price Action/"          # adatta il path al progetto corrente

# 3. Commit con messaggio convenzionale
git commit -m "feat: vX.Y.0 <descrizione sintetica delle novità>"

# 4. Push
git push origin main
```

**Convenzioni per il messaggio di commit:**

| Prefisso | Quando usarlo |
|---|---|
| `feat:` | Nuove funzionalità (minor bump) |
| `fix:` | Bug fix (patch bump) |
| `refactor:` | Refactoring senza cambio di comportamento |
| `docs:` | Solo documentazione |

---

### Comandi utili di riferimento

```powershell
# Stato e log
git status
git log --oneline -10
git diff --stat HEAD~1

# Verificare i commit non ancora pushati
git log origin/main..HEAD --oneline

# Confrontare con il remote
git fetch origin
git diff origin/main..main --stat

# Annullare l'ultimo commit (mantiene le modifiche)
git reset --soft HEAD~1

# Vedere il repo nel browser
gh repo view --web

# Creare una release GitHub dalla versione corrente
gh release create vX.Y.Z --title "vX.Y.Z — <titolo>" --notes "<note>"
```

---

### Note operative

- Non committare `.claude/settings.local.json` (è un file locale dell'IDE Claude).
- Il file `CHANGELOG.md` deve essere aggiornato **prima** del commit di rilascio.
- I warning CS8632 (nullable annotation) sono pre-esistenti e non bloccanti — non costituiscono un problema da risolvere prima del push.

---

## 📖 Riferimenti

- [cAlgo API Reference](https://help.ctrader.com/ctrader-algo/api-reference/)
- [cTrader API Documentation](https://help.ctrader.com/ctrader-algo/)
- [SOLID Principles](https://en.wikipedia.org/wiki/SOLID)
- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)

---

**Nota:** Questo documento deve essere aggiornato ad ogni cambiamento significativo nell'architettura o nelle convenzioni del progetto.