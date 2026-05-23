namespace MarkovChainBot;

/// <summary>
/// Nine-position anchor grid for the analytics panel (3 columns × 3 rows).
///
/// Offset semantics per anchor group:
///   Corner anchors  (TopLeft / TopRight / BottomLeft / BottomRight)
///       PanelOffsetX  = pixels from the nearest horizontal edge toward chart center (≥ 0)
///       PanelOffsetY  = pixels from the nearest vertical   edge toward chart center (≥ 0)
///
///   Edge-center anchors  (TopCenter / BottomCenter / MiddleLeft / MiddleRight)
///       PanelOffsetX  = signed horizontal displacement from the centered position
///                       positive → right,  negative → left
///       PanelOffsetY  = signed vertical   displacement from the centered position
///                       positive → down,   negative → up
///
///   MiddleCenter
///       PanelOffsetX  = signed horizontal displacement from screen center
///       PanelOffsetY  = signed vertical   displacement from screen center
/// </summary>
public enum PanelCorner
{
    // ── Original corners ──────────────────────────────────────────────────
    TopLeft      = 0,
    TopRight     = 1,
    BottomLeft   = 2,
    BottomRight  = 3,

    // ── Edge centres ──────────────────────────────────────────────────────
    TopCenter    = 4,   // top edge, horizontally centred
    BottomCenter = 5,   // bottom edge, horizontally centred
    MiddleLeft   = 6,   // left edge, vertically centred
    MiddleRight  = 7,   // right edge, vertically centred

    // ── Screen centre ─────────────────────────────────────────────────────
    MiddleCenter = 8,   // centre of the chart area
}
