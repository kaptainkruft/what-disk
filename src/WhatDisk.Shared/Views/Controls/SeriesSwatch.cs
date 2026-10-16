using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhatDisk.Views.Controls;

/// <summary>
/// A small square in a chart colour, used by the legend to tie each entry to its slice.
/// </summary>
public sealed class SeriesSwatch : Control
{
    /// <summary>
    /// Identifies the <see cref="Slot"/> property.
    /// </summary>
    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<SeriesSwatch, int>(nameof(Slot));

    /// <summary>The side length of the swatch.</summary>
    private const double Side = 10;

    /// <summary>
    /// Initializes static members of the <see cref="SeriesSwatch"/> class.
    /// </summary>
    static SeriesSwatch() => AffectsRender<SeriesSwatch>(SlotProperty);

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesSwatch"/> class.
    /// </summary>
    public SeriesSwatch() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    /// <summary>
    /// Gets or sets the colour slot.
    /// </summary>
    public int Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var top = (Bounds.Height - Side) / 2;
        context.DrawRectangle(ChartBrushes.ForSlot(this, Slot), null, new Rect(0, top, Side, Side), 2, 2);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(Side, Side);
}
