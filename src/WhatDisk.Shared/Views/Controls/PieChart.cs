using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhatDisk.Scanning;
using WhatDisk.ViewModels;

namespace WhatDisk.Views.Controls;

/// <summary>
/// A donut chart of <see cref="ChartSlice"/> values. Slices are separated by a thin gap in the surface colour; the
/// hovered or selected slice is lifted outwards, shows its value in a tooltip and can be clicked.
/// </summary>
public sealed class PieChart : Control
{
    /// <summary>
    /// Identifies the <see cref="Slices"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<ChartSlice>?> SlicesProperty =
        AvaloniaProperty.Register<PieChart, IReadOnlyList<ChartSlice>?>(nameof(Slices));

    /// <summary>
    /// Identifies the <see cref="HighlightedNode"/> property.
    /// </summary>
    public static readonly StyledProperty<FileSystemNode?> HighlightedNodeProperty =
        AvaloniaProperty.Register<PieChart, FileSystemNode?>(nameof(HighlightedNode));

    /// <summary>
    /// Identifies the <see cref="SliceCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> SliceCommandProperty =
        AvaloniaProperty.Register<PieChart, ICommand?>(nameof(SliceCommand));

    /// <summary>
    /// Identifies the <see cref="GapBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GapBrushProperty =
        AvaloniaProperty.Register<PieChart, IBrush?>(nameof(GapBrush));

    /// <summary>
    /// Identifies the <see cref="InnerRadiusRatio"/> property.
    /// </summary>
    public static readonly StyledProperty<double> InnerRadiusRatioProperty =
        AvaloniaProperty.Register<PieChart, double>(nameof(InnerRadiusRatio), 0.6);

    /// <summary>How far a hovered or selected slice is lifted outwards.</summary>
    private const double Lift = 5;

    /// <summary>The width of the gap drawn between slices.</summary>
    private const double GapWidth = 2;

    /// <summary>The index of the slice under the pointer, or -1.</summary>
    private int _hoveredIndex = -1;

    /// <summary>
    /// Initializes static members of the <see cref="PieChart"/> class.
    /// </summary>
    static PieChart() =>
        AffectsRender<PieChart>(SlicesProperty, HighlightedNodeProperty, GapBrushProperty, InnerRadiusRatioProperty);

    /// <summary>
    /// Initializes a new instance of the <see cref="PieChart"/> class.
    /// </summary>
    public PieChart() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    /// <summary>
    /// Gets or sets the slices, in drawing order.
    /// </summary>
    public IReadOnlyList<ChartSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    /// <summary>
    /// Gets or sets the node whose slice is lifted, typically the selected item.
    /// </summary>
    public FileSystemNode? HighlightedNode
    {
        get => GetValue(HighlightedNodeProperty);
        set => SetValue(HighlightedNodeProperty, value);
    }

    /// <summary>
    /// Gets or sets the command executed with a slice's node when the slice is clicked.
    /// </summary>
    public ICommand? SliceCommand
    {
        get => GetValue(SliceCommandProperty);
        set => SetValue(SliceCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the gaps between slices; it should match the surface behind the chart.
    /// </summary>
    public IBrush? GapBrush
    {
        get => GetValue(GapBrushProperty);
        set => SetValue(GapBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the radius of the hole as a fraction of the outer radius.
    /// </summary>
    public double InnerRadiusRatio
    {
        get => GetValue(InnerRadiusRatioProperty);
        set => SetValue(InnerRadiusRatioProperty, value);
    }

    /// <summary>
    /// Converts an angle (degrees clockwise from twelve o'clock) and radius into a point.
    /// </summary>
    /// <param name="center">The centre of the chart.</param>
    /// <param name="radius">The distance from the centre.</param>
    /// <param name="degrees">The angle.</param>
    /// <returns>The point.</returns>
    internal static Point Polar(Point center, double radius, double degrees)
    {
        var radians = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    /// <summary>
    /// Finds the slice that contains a point, given the chart geometry.
    /// </summary>
    /// <param name="slices">The slices.</param>
    /// <param name="center">The centre of the chart.</param>
    /// <param name="innerRadius">The radius of the hole.</param>
    /// <param name="outerRadius">The outer radius, including the lift.</param>
    /// <param name="point">The point to test.</param>
    /// <returns>The index of the slice, or -1 when the point is outside the ring.</returns>
    internal static int HitTest(IReadOnlyList<ChartSlice> slices, Point center, double innerRadius, double outerRadius, Point point)
    {
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < innerRadius || distance > outerRadius)
        {
            return -1;
        }

        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0)
        {
            angle += 360;
        }

        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index].Slice;
            if (angle >= slice.StartAngle && angle < slice.StartAngle + slice.SweepAngle)
            {
                return index;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (Slices is not { Count: > 0 } slices || !TryGetGeometry(out var center, out var inner, out var outer))
        {
            return;
        }

        var highlighted = HighlightedNode;
        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index].Slice;
            var lifted = index == _hoveredIndex || (highlighted is not null && ReferenceEquals(slice.Node, highlighted));
            var geometry = CreateSector(center, inner, lifted ? outer + Lift : outer, slice.StartAngle, slice.SweepAngle);
            context.DrawGeometry(ChartBrushes.ForSlot(this, slice.ColorSlot), null, geometry);
        }

        if (slices.Count > 1 && GapBrush is { } gapBrush)
        {
            var pen = new Pen(gapBrush, GapWidth, lineCap: PenLineCap.Flat);
            foreach (var item in slices)
            {
                var angle = item.Slice.StartAngle;
                context.DrawLine(pen, Polar(center, inner - 1, angle), Polar(center, outer + Lift + 1, angle));
            }
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        return double.IsInfinity(side) ? new Size(160, 160) : new Size(side, side);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlicesProperty)
        {
            _hoveredIndex = -1;
            ToolTip.SetTip(this, null);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = -1;
        if (Slices is { Count: > 0 } slices && TryGetGeometry(out var center, out var inner, out var outer))
        {
            index = HitTest(slices, center, inner, outer + Lift, e.GetPosition(this));
        }

        if (index == _hoveredIndex)
        {
            return;
        }

        _hoveredIndex = index;
        ToolTip.SetTip(this, index >= 0 ? CreateToolTip(Slices![index]) : null);
        Cursor = index >= 0 && Slices![index].Slice.Node is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredIndex = -1;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || Slices is not { Count: > 0 } slices
            || !TryGetGeometry(out var center, out var inner, out var outer))
        {
            return;
        }

        var index = HitTest(slices, center, inner, outer + Lift, e.GetPosition(this));
        if (index >= 0 && slices[index].Slice.Node is { } node && SliceCommand is { } command && command.CanExecute(node))
        {
            command.Execute(node);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Builds the outline of a ring segment.
    /// </summary>
    /// <param name="center">The centre of the chart.</param>
    /// <param name="inner">The inner radius.</param>
    /// <param name="outer">The outer radius.</param>
    /// <param name="start">The start angle, in degrees clockwise from twelve o'clock.</param>
    /// <param name="sweep">The angular extent in degrees.</param>
    /// <returns>The geometry.</returns>
    private static StreamGeometry CreateSector(Point center, double inner, double outer, double start, double sweep)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (sweep >= 359.99)
        {
            // A full ring: two concentric circles, filled with the even-odd rule so the hole stays empty.
            context.SetFillRule(FillRule.EvenOdd);
            AddCircle(context, center, outer);
            AddCircle(context, center, inner);
            return geometry;
        }

        var end = start + sweep;
        var isLargeArc = sweep > 180;
        context.BeginFigure(Polar(center, outer, start), isFilled: true);
        context.ArcTo(Polar(center, outer, end), new Size(outer, outer), 0, isLargeArc, SweepDirection.Clockwise);
        context.LineTo(Polar(center, inner, end));
        context.ArcTo(Polar(center, inner, start), new Size(inner, inner), 0, isLargeArc, SweepDirection.CounterClockwise);
        context.EndFigure(isClosed: true);
        return geometry;
    }

    /// <summary>
    /// Adds a circle to a geometry as two half arcs.
    /// </summary>
    /// <param name="context">The geometry being built.</param>
    /// <param name="center">The centre.</param>
    /// <param name="radius">The radius.</param>
    private static void AddCircle(StreamGeometryContext context, Point center, double radius)
    {
        context.BeginFigure(Polar(center, radius, 0), isFilled: true);
        context.ArcTo(Polar(center, radius, 180), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.ArcTo(Polar(center, radius, 360), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.EndFigure(isClosed: true);
    }

    /// <summary>
    /// Creates the tooltip for a slice: the value first and prominent, the name second.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <returns>The tooltip content.</returns>
    private static StackPanel CreateToolTip(ChartSlice slice)
    {
        var panel = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = $"{slice.SizeText} · {slice.PercentText}", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = slice.Slice.Label, Opacity = 0.8, MaxWidth = 280, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    /// <summary>
    /// Computes the chart's centre and radii for the current size.
    /// </summary>
    /// <param name="center">Receives the centre.</param>
    /// <param name="inner">Receives the radius of the hole.</param>
    /// <param name="outer">Receives the outer radius of an unlifted slice.</param>
    /// <returns><see langword="false"/> when the control is too small to draw.</returns>
    private bool TryGetGeometry(out Point center, out double inner, out double outer)
    {
        var size = Bounds.Size;
        center = new Point(size.Width / 2, size.Height / 2);
        outer = (Math.Min(size.Width, size.Height) / 2) - Lift - 1;
        inner = outer * Math.Clamp(InnerRadiusRatio, 0, 0.9);
        return outer > 10;
    }
}
