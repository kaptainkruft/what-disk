using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WhatDisk.Views;

/// <summary>
/// One button of a <see cref="QuestionView"/>.
/// </summary>
/// <param name="Label">The button's text.</param>
/// <param name="Result">The result the question gives when the button is pressed.</param>
/// <param name="IsDefault">Whether Return presses the button.</param>
/// <param name="IsCancel">Whether Escape presses the button.</param>
/// <param name="IsDestructive">Whether the button destroys data, so it is styled as dangerous.</param>
public sealed record QuestionButton(string Label, string Result, bool IsDefault = false, bool IsCancel = false, bool IsDestructive = false);

/// <summary>
/// A question asked inside the main window: a heading, an explanation, an optional check box and a row of buttons.
/// </summary>
public sealed class QuestionView : InWindowDialog
{
    /// <summary>The optional check box.</summary>
    private readonly CheckBox? _checkBox;

    /// <summary>The button Return presses, if any.</summary>
    private readonly Button? _defaultButton;

    /// <summary>The result Escape gives.</summary>
    private readonly string? _cancelResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuestionView"/> class.
    /// </summary>
    /// <param name="heading">The question.</param>
    /// <param name="message">The explanation.</param>
    /// <param name="buttons">The buttons, left to right.</param>
    /// <param name="checkBoxLabel">The label of a check box shown above the buttons, or <see langword="null"/> for none.</param>
    public QuestionView(string heading, string message, IReadOnlyList<QuestionButton> buttons, string? checkBoxLabel = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        Heading = heading;
        Width = 440;
        _cancelResult = buttons.FirstOrDefault(static button => button.IsCancel)?.Result;

        var content = new StackPanel { Margin = new Thickness(24, 20), Spacing = 10 };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } });
        if (checkBoxLabel is not null)
        {
            _checkBox = new CheckBox { Content = checkBoxLabel };
            content.Children.Add(_checkBox);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var definition in buttons)
        {
            var button = new Button
            {
                Content = definition.Label,
                IsDefault = definition.IsDefault,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (definition.IsDefault)
            {
                button.Classes.Add("accent");
                _defaultButton = button;
            }

            if (definition.IsDestructive)
            {
                button.Classes.Add("danger");
            }

            button.Click += (_, _) => Complete(definition.Result);
            row.Children.Add(button);
        }

        content.Children.Add(row);
        Content = content;
    }

    /// <summary>
    /// Gets the question.
    /// </summary>
    public string Heading { get; }

    /// <summary>
    /// Gets a value indicating whether the check box was ticked.
    /// </summary>
    public bool IsChecked => _checkBox?.IsChecked == true;

    /// <inheritdoc/>
    protected override string? CancelResult => _cancelResult;

    /// <inheritdoc/>
    public override void FocusDefault()
    {
        if (_defaultButton is not null)
        {
            _defaultButton.Focus();
        }
        else
        {
            (_checkBox as Control ?? this).Focus();
        }
    }
}
