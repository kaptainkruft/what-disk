using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WhatDisk.Views;

/// <summary>
/// One button of a <see cref="QuestionWindow"/>.
/// </summary>
/// <param name="Label">The button's text.</param>
/// <param name="Result">The value <see cref="QuestionWindow.ShowAsync"/> returns when the button is pressed.</param>
/// <param name="IsDefault">Whether Return presses the button.</param>
/// <param name="IsCancel">Whether Escape presses the button.</param>
/// <param name="IsDestructive">Whether the button destroys data, so it is styled as dangerous.</param>
public sealed record QuestionButton(string Label, string Result, bool IsDefault = false, bool IsCancel = false, bool IsDestructive = false);

/// <summary>
/// A small modal window asking a question: a heading, an explanation, an optional check box and a row of buttons.
/// </summary>
public sealed class QuestionWindow : Window
{
    /// <summary>The optional check box.</summary>
    private readonly CheckBox? _checkBox;

    /// <summary>The result of the pressed button, or <see langword="null"/> when the window was closed.</summary>
    private string? _result;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuestionWindow"/> class.
    /// </summary>
    /// <param name="heading">The question.</param>
    /// <param name="message">The explanation.</param>
    /// <param name="buttons">The buttons, left to right.</param>
    /// <param name="checkBoxLabel">The label of a check box shown above the buttons, or <see langword="null"/> for none.</param>
    public QuestionWindow(string heading, string message, IReadOnlyList<QuestionButton> buttons, string? checkBoxLabel = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        Title = heading;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
                IsCancel = definition.IsCancel,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (definition.IsDefault)
            {
                button.Classes.Add("accent");
            }

            if (definition.IsDestructive)
            {
                button.Classes.Add("danger");
            }

            button.Click += (_, _) =>
            {
                _result = definition.Result;
                Close();
            };
            row.Children.Add(button);
        }

        content.Children.Add(row);
        Content = content;
    }

    /// <summary>
    /// Gets a value indicating whether the check box was ticked.
    /// </summary>
    public bool IsChecked => _checkBox?.IsChecked == true;

    /// <summary>
    /// Shows the window modally over an owner and waits for an answer.
    /// </summary>
    /// <param name="owner">The window that owns the question.</param>
    /// <returns>The result of the pressed button, or <see langword="null"/> when the window was closed.</returns>
    public async Task<string?> ShowAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }
}
