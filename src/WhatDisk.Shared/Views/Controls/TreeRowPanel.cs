using Avalonia;
using Avalonia.Controls;

namespace WhatDisk.Views.Controls;

/// <summary>
/// Lays out the cells of a tree list row (or the header) side by side, using the widths of the inherited
/// <see cref="TreeColumnLayout"/>. Children marked with <see cref="ResizeColumnProperty"/> are column resize grips,
/// placed over the left edge of their column.
/// </summary>
public sealed class TreeRowPanel : Panel
{
    /// <summary>
    /// Identifies the inherited column layout shared by the header and the rows.
    /// </summary>
    public static readonly AttachedProperty<TreeColumnLayout?> ColumnLayoutProperty =
        AvaloniaProperty.RegisterAttached<TreeRowPanel, Control, TreeColumnLayout?>("ColumnLayout", inherits: true);

    /// <summary>
    /// Identifies the column whose left edge a resize grip controls; -1 for ordinary cells.
    /// </summary>
    public static readonly AttachedProperty<int> ResizeColumnProperty =
        AvaloniaProperty.RegisterAttached<TreeRowPanel, Control, int>("ResizeColumn", -1);

    /// <summary>
    /// Identifies the horizontal padding inside each cell.
    /// </summary>
    public static readonly StyledProperty<double> CellPaddingProperty =
        AvaloniaProperty.Register<TreeRowPanel, double>(nameof(CellPadding), 6);

    /// <summary>The width of a resize grip.</summary>
    private const double GripWidth = 8;

    /// <summary>The layout whose changes this panel is subscribed to.</summary>
    private TreeColumnLayout? _subscribedLayout;

    /// <summary>Whether the panel is attached to a visual tree.</summary>
    private bool _isAttached;

    /// <summary>
    /// Initializes static members of the <see cref="TreeRowPanel"/> class.
    /// </summary>
    static TreeRowPanel() => AffectsMeasure<TreeRowPanel>(ColumnLayoutProperty, CellPaddingProperty);

    /// <summary>
    /// Gets or sets the horizontal padding inside each cell.
    /// </summary>
    public double CellPadding
    {
        get => GetValue(CellPaddingProperty);
        set => SetValue(CellPaddingProperty, value);
    }

    /// <summary>
    /// Gets the column layout of a control.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The layout, or <see langword="null"/>.</returns>
    public static TreeColumnLayout? GetColumnLayout(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ColumnLayoutProperty);
    }

    /// <summary>
    /// Sets the column layout of a control and its descendants.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The layout.</param>
    public static void SetColumnLayout(Control control, TreeColumnLayout? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ColumnLayoutProperty, value);
    }

    /// <summary>
    /// Gets the column whose left edge a grip resizes.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The column index, or -1.</returns>
    public static int GetResizeColumn(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ResizeColumnProperty);
    }

    /// <summary>
    /// Marks a control as the resize grip for the left edge of a column.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The column index, or -1.</param>
    public static void SetResizeColumn(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ResizeColumnProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Subscribe(GetValue(ColumnLayoutProperty));
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        Subscribe(null);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnLayoutProperty && _isAttached)
        {
            Subscribe(change.GetNewValue<TreeColumnLayout?>());
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (GetValue(ColumnLayoutProperty) is not { } layout)
        {
            return base.MeasureOverride(availableSize);
        }

        var rowWidth = double.IsInfinity(availableSize.Width) ? layout.MinimumTotalWidth : availableSize.Width;
        Span<double> widths = stackalloc double[layout.Columns.Count];
        layout.ComputeWidths(rowWidth, widths);
        var padding = CellPadding;
        var height = 0d;
        var cell = 0;
        foreach (var child in Children)
        {
            if (GetResizeColumn(child) >= 0)
            {
                child.Measure(new Size(GripWidth, availableSize.Height));
                continue;
            }

            var width = cell < widths.Length ? widths[cell] : 0;
            cell++;
            child.Measure(new Size(Math.Max(0, width - (2 * padding)), availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(rowWidth, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (GetValue(ColumnLayoutProperty) is not { } layout)
        {
            return base.ArrangeOverride(finalSize);
        }

        Span<double> widths = stackalloc double[layout.Columns.Count];
        layout.ComputeWidths(finalSize.Width, widths);
        Span<double> lefts = stackalloc double[layout.Columns.Count];
        var x = 0d;
        for (var index = 0; index < widths.Length; index++)
        {
            lefts[index] = x;
            x += widths[index];
        }

        var padding = CellPadding;
        var cell = 0;
        foreach (var child in Children)
        {
            var gripColumn = GetResizeColumn(child);
            if (gripColumn >= 0)
            {
                var edge = gripColumn < lefts.Length ? lefts[gripColumn] : x;
                child.Arrange(new Rect(edge - (GripWidth / 2), 0, GripWidth, finalSize.Height));
                continue;
            }

            if (cell >= widths.Length)
            {
                child.Arrange(default);
                continue;
            }

            var width = Math.Max(0, widths[cell] - (2 * padding));
            child.Arrange(new Rect(lefts[cell] + padding, 0, width, finalSize.Height));
            cell++;
        }

        return finalSize;
    }

    /// <summary>
    /// Re-subscribes to width changes of a layout.
    /// </summary>
    /// <param name="layout">The layout to observe, or <see langword="null"/>.</param>
    private void Subscribe(TreeColumnLayout? layout)
    {
        if (ReferenceEquals(layout, _subscribedLayout))
        {
            return;
        }

        if (_subscribedLayout is not null)
        {
            _subscribedLayout.Changed -= OnLayoutChanged;
        }

        _subscribedLayout = layout;
        if (layout is not null)
        {
            layout.Changed += OnLayoutChanged;
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Re-measures the row when a column width changes.
    /// </summary>
    /// <param name="sender">The layout.</param>
    /// <param name="e">Unused.</param>
    private void OnLayoutChanged(object? sender, EventArgs e) => InvalidateMeasure();
}
