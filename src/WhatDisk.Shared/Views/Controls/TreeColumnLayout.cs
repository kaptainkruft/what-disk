namespace WhatDisk.Views.Controls;

/// <summary>
/// The widths of the tree list's columns, shared by the header and every row so they stay aligned. The first column
/// fills whatever space the fixed-width columns leave; the others can be resized.
/// </summary>
public sealed class TreeColumnLayout
{
    /// <summary>The current widths of the fixed columns; the first entry is unused.</summary>
    private readonly double[] _widths;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeColumnLayout"/> class.
    /// </summary>
    /// <param name="columns">The columns; the first one fills the remaining width.</param>
    public TreeColumnLayout(IReadOnlyList<TreeColumnDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Count);
        Columns = columns;
        _widths = [.. columns.Select(static column => Math.Max(column.Width, column.MinWidth))];
    }

    /// <summary>
    /// Raised when a column width changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the columns.
    /// </summary>
    public IReadOnlyList<TreeColumnDefinition> Columns { get; }

    /// <summary>
    /// Gets the width needed by every column at its minimum (first column) or current (others) width.
    /// </summary>
    public double MinimumTotalWidth => Columns[0].MinWidth + _widths.Skip(1).Sum();

    /// <summary>
    /// Gets the current width of a fixed column.
    /// </summary>
    /// <param name="index">The column index (1 or more).</param>
    /// <returns>The width.</returns>
    public double GetWidth(int index) => _widths[index];

    /// <summary>
    /// Sets the width of a fixed column, honouring its minimum.
    /// </summary>
    /// <param name="index">The column index (1 or more).</param>
    /// <param name="width">The requested width.</param>
    public void SetWidth(int index, double width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        var clamped = Math.Clamp(width, Columns[index].MinWidth, 1000);
        if (Math.Abs(clamped - _widths[index]) < 0.1)
        {
            return;
        }

        _widths[index] = clamped;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Computes the actual column widths for an available width.
    /// </summary>
    /// <param name="available">The width available to the row.</param>
    /// <param name="widths">Receives one width per column.</param>
    public void ComputeWidths(double available, Span<double> widths)
    {
        var fixedTotal = 0d;
        for (var index = 1; index < _widths.Length; index++)
        {
            widths[index] = _widths[index];
            fixedTotal += _widths[index];
        }

        widths[0] = Math.Max(Columns[0].MinWidth, available - fixedTotal);
    }
}
