using Godot;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private Rect2 VisibleViewportLocal()
    {
        // A native screen can retain a larger layout rectangle in a resized
        // window. Use the visible canvas rather than its layout minimum.
        var visible = GetCanvasTransform().AffineInverse() * GetViewportRect();
        return GetGlobalTransform().AffineInverse() * _viewportGlobal.Intersection(visible);
    }

    private Vector2? _hoverOffset;
    private bool _cardsBelow, _informationAbove;
    private const float SideChangeGain = 48;

    private Vector2 StableMainOrigin(Rect2 viewport, Rect2 anchor, float width, float height)
    {
        float left = anchor.Position.X - viewport.Position.X - Margin - Gap;
        float right = viewport.End.X - Margin - anchor.End.X - Gap;
        _hoverLeft ??= left > right;
        _hoverOffset ??= new Vector2(_hoverLeft == true ? -Gap - width : anchor.Size.X + Gap, 0);
        // Keep the source-relative horizontal side. At vertical screen edges,
        // slide continuously into the visible reading area rather than change
        // anchors or keep the title/actions below the screen.
        var origin = anchor.Position + _hoverOffset.Value;
        origin.Y = Math.Clamp(origin.Y, viewport.Position.Y + Margin,
            Math.Max(viewport.Position.Y + Margin, viewport.End.Y - Margin - height));
        return origin;
    }

    private static bool KeepOrChangeSide(bool current, float required, float first, float second)
    {
        float available = current ? second : first, other = current ? first : second;
        return available + 1 < required && other >= available + SideChangeGain ? !current : current;
    }

    private void LayoutAuxiliarySurfaces(Rect2 viewport, Rect2 main)
    {
        if (_optionPreview.Visible && (_previewAnchor == null || !IsInstanceValid(_previewAnchor) || !_previewAnchor.IsVisibleInTree()))
            CloseOptionPreview();
        float width = main.Size.X;
        float combatNatural = 0, combatMinimum = 0;
        if (HasCombatSurface && _combatBody != null)
        {
            _combatBody.CustomMinimumSize = new(Math.Max(1, width - 32), 0);
            combatMinimum = CombatSurfaceChrome + CombatBodyMinimum;
            combatNatural = Math.Max(combatMinimum, _combatBody.GetCombinedMinimumSize().Y + CombatSurfaceChrome);
        }
        float summaryNatural = _optionPreview.Visible && _optionPreview.SummaryDesiredSize.Y > 0
            ? _optionPreview.SummaryDesiredSize.Y + 19 : 0;
        float summaryMinimum = Math.Min(96, summaryNatural);
        float informationGap = combatNatural > 0 && summaryNatural > 0 ? Gap : 0;
        float informationNatural = combatNatural + summaryNatural + informationGap;
        float informationMinimum = combatMinimum + summaryMinimum + informationGap;
        var cards = _optionPreview.Visible ? _optionCardPreview.CardsDesiredSize :
            HasRewardCardDock ? RewardCardDockDesiredSize : Vector2.Zero;
        if (_optionPreview.Visible && cards.X > 0 && cards.Y > 0) cards += new Vector2(16, 16);
        bool hasCards = cards.X > 0 && cards.Y > 0;
        bool hasInformation = informationNatural > 0;
        float upperEnd = Math.Min(main.Position.Y - Gap, viewport.End.Y - Margin);
        float lowerStart = Math.Max(main.End.Y + Gap, viewport.Position.Y + Margin);
        float above = Math.Max(0, upperEnd - viewport.Position.Y - Margin);
        float below = Math.Max(0, viewport.End.Y - Margin - lowerStart);

        if (hasCards)
            _cardsBelow = KeepOrChangeSide(_cardsBelow, cards.Y,
                Math.Max(0, above - (hasInformation && _informationAbove ? informationMinimum + Gap : 0)),
                Math.Max(0, below - (hasInformation && !_informationAbove ? informationMinimum + Gap : 0)));
        if (hasInformation)
            _informationAbove = KeepOrChangeSide(_informationAbove, informationNatural,
                Math.Max(0, below - (hasCards && _cardsBelow ? cards.Y + Gap : 0)),
                Math.Max(0, above - (hasCards && !_cardsBelow ? cards.Y + Gap : 0)));

        float informationSpace = _informationAbove ? above : below;
        bool sameSide = hasCards && hasInformation && _cardsBelow != _informationAbove;
        float informationHeight = hasInformation ? Math.Max(informationMinimum,
            Math.Min(informationNatural, informationSpace - (sameSide ? cards.Y + Gap : 0))) : 0;
        float cardSpace = _cardsBelow ? below : above;
        float cardHeight = hasCards ? Math.Max(1, Math.Min(cards.Y,
            cardSpace - (sameSide ? informationHeight + Gap : 0))) : 0;
        float combatHeight = combatNatural > 0 ? Math.Max(combatMinimum,
            Math.Min(combatNatural, informationHeight - summaryMinimum - informationGap)) : 0;
        float summaryHeight = summaryNatural > 0 ? Math.Max(1, informationHeight - combatHeight - informationGap) : 0;
        float upperCursor = upperEnd, lowerCursor = lowerStart;
        Rect2 Place(float height, bool aboveMain, float surfaceWidth)
        {
            float y;
            if (aboveMain) { y = upperCursor - height; upperCursor = y - Gap; }
            else { y = lowerCursor; lowerCursor += height + Gap; }
            float x = Math.Clamp(main.GetCenter().X - surfaceWidth * .5f, viewport.Position.X + Margin,
                Math.Max(viewport.Position.X + Margin, viewport.End.X - Margin - surfaceWidth));
            return new(x, y, surfaceWidth, height);
        }
        // Information stays nearest the main panel on either side. A single
        // placement cursor per side keeps battle, summary and cards disjoint.
        if (combatNatural > 0) ApplyCombatSurfaceLayout(Place(combatHeight, _informationAbove, width));
        Rect2 summaryArea = summaryNatural > 0 ? Place(summaryHeight, _informationAbove, width) : default;
        Rect2 cardArea = default;
        if (hasCards)
        {
            float scrollWidth = _optionPreview.Visible ? _previewCardsViewport.GetVScrollBar().GetCombinedMinimumSize().X :
                _rewardCardDock?.GetVScrollBar().GetCombinedMinimumSize().X ?? 0;
            float cardWidth = Math.Min(cards.X + (cardHeight < cards.Y ? scrollWidth : 0),
                Math.Max(1, viewport.Size.X - Margin * 2));
            cardArea = Place(cardHeight, !_cardsBelow, cardWidth);
        }
        if (_optionPreview.Visible) ApplyOptionPreviewLayout(summaryArea, cardArea);
        else LayoutRewardCardDock(cardArea);
    }

    private bool AuxiliaryConnectionContains(Vector2 point)
    {
        var regions = new List<Rect2> { _surface.GetGlobalRect() };
        if (HasCombatSurface) regions.Add(_combatSurface!.GetGlobalRect());
        if (_previewViewport.IsVisibleInTree()) regions.Add(_previewViewport.GetGlobalRect());
        if (_previewCardsViewport.IsVisibleInTree()) regions.Add(_previewCardsViewport.GetGlobalRect());
        if (_rewardCardDock?.IsVisibleInTree() == true) regions.Add(_rewardCardDock.GetGlobalRect());
        for (int i = 0; i < regions.Count; i++)
            for (int j = i + 1; j < regions.Count; j++)
                if (ShortConnectionContains(regions[i], regions[j], point)) return true;
        return false;
    }
}
