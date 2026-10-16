using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace WhatDisk.Views.Controls;

/// <summary>
/// A horizontal bar that fills from the left in proportion to <see cref="Value"/>, followed by a right-aligned
/// percentage. Drawn directly rather than templated, because every row of the tree contains two of them.
/// </summary>
public sealed class PercentBar : Control
{
    /// <summary>
    /// Identifies the <see cref="Value"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PercentBar, double>(nameof(Value));

    /// <summary>
    /// Identifies the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PercentBar, string?>(nameof(Text));

    /// <summary>
    /// Identifies the <see cref="BarBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<PercentBar, IBrush?>(nameof(BarBrush));

    /// <summary>
    /// Identifies the <see cref="TrackBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<PercentBar, IBrush?>(nameof(TrackBrush));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> property, inherited like text colour.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<PercentBar>();

    /// <summary>
    /// Identifies the <see cref="BarHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<PercentBar, double>(nameof(BarHeight), 8);

    /// <summary>
    /// Identifies the <see cref="TextWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TextWidthProperty =
        AvaloniaProperty.Register<PercentBar, double>(nameof(TextWidth), 46);

    /// <summary>The space between the bar and the text.</summary>
    private const double Gap = 6;

    /// <summary>The text layout cached for the current text and font.</summary>
    private FormattedText? _formattedText;

    /// <summary>
    /// Initializes static members of the <see cref="PercentBar"/> class.
    /// </summary>
    static PercentBar()
    {
        AffectsRender<PercentBar>(ValueProperty, TextProperty, BarBrushProperty, TrackBrushProperty, ForegroundProperty, BarHeightProperty, TextWidthProperty);
        AffectsMeasure<PercentBar>(BarHeightProperty, TextWidthProperty);
    }

    /// <summary>
    /// Gets or sets the filled fraction, from 0 to 1.
    /// </summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the text shown to the right of the bar; no text area is reserved when it is empty.
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the filled part.
    /// </summary>
    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the unfilled track.
    /// </summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the text brush.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the bar.
    /// </summary>
    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the width reserved for the text.
    /// </summary>
    public double TextWidth
    {
        get => GetValue(TextWidthProperty);
        set => SetValue(TextWidthProperty, value);
    }

    /// <summary>
    /// Builds the outline of a bar that is square at its left (baseline) end and rounded at its data end.
    /// </summary>
    /// <param name="rect">The bar's bounds.</param>
    /// <returns>The geometry.</returns>
    internal static StreamGeometry CreateBarGeometry(Rect rect)
    {
        var radius = Math.Min(rect.Height / 2, rect.Width);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(rect.TopLeft, isFilled: true);
        context.LineTo(new Point(rect.Right - radius, rect.Top));
        context.ArcTo(new Point(rect.Right, rect.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.LineTo(new Point(rect.Right, rect.Bottom - radius));
        context.ArcTo(new Point(rect.Right - radius, rect.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.LineTo(rect.BottomLeft);
        context.EndFigure(isClosed: true);
        return geometry;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var text = Text;
        var textWidth = string.IsNullOrEmpty(text) ? 0 : TextWidth;
        var barWidth = Math.Max(0, size.Width - textWidth - (textWidth > 0 ? Gap : 0));
        var barHeight = Math.Min(BarHeight, size.Height);
        var top = Math.Round((size.Height - barHeight) / 2);
        if (barWidth >= 2)
        {
            if (TrackBrush is { } track)
            {
                context.DrawGeometry(track, null, CreateBarGeometry(new Rect(0, top, barWidth, barHeight)));
            }

            var filled = barWidth * Math.Clamp(Value, 0, 1);
            if (filled >= 0.5 && BarBrush is { } bar)
            {
                context.DrawGeometry(bar, null, CreateBarGeometry(new Rect(0, top, filled, barHeight)));
            }
        }

        if (textWidth > 0 && GetFormattedText(text!) is { } formatted)
        {
            context.DrawText(formatted, new Point(size.Width - formatted.Width, (size.Height - formatted.Height) / 2));
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Math.Max(BarHeight, string.IsNullOrEmpty(Text) ? 0 : GetFormattedText(Text)?.Height ?? 0);
        return new Size(Math.Min(availableSize.Width, TextWidth + 24), height);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty
            || change.Property == ForegroundProperty
            || change.Property == TextElement.FontFamilyProperty
            || change.Property == TextElement.FontSizeProperty
            || change.Property == TextElement.FontWeightProperty)
        {
            _formattedText = null;
        }
    }

    /// <summary>
    /// Gets the laid-out text, creating it when the text or font changed.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The formatted text.</returns>
    private FormattedText? GetFormattedText(string text)
    {
        if (_formattedText is null)
        {
            var typeface = new Typeface(
                GetValue(TextElement.FontFamilyProperty),
                GetValue(TextElement.FontStyleProperty),
                GetValue(TextElement.FontWeightProperty));
            _formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                GetValue(TextElement.FontSizeProperty),
                Foreground ?? Brushes.Black);
        }

        return _formattedText;
    }
}
