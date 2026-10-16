using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhatDisk.Icons;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Views.Controls;

/// <summary>
/// Shows the platform icon of a node or volume. A built-in vector icon is shown immediately and replaced when the
/// platform icon has been loaded in the background.
/// </summary>
public sealed class PlatformIcon : Control
{
    /// <summary>
    /// Identifies the <see cref="Subject"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> SubjectProperty =
        AvaloniaProperty.Register<PlatformIcon, object?>(nameof(Subject));

    /// <summary>
    /// Identifies the <see cref="IconSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<PlatformIcon, double>(nameof(IconSize), 16);

    /// <summary>Maps subjects to the categories of their built-in icons.</summary>
    private static readonly DefaultIconProvider FallbackKeys = new();

    /// <summary>The image currently drawn.</summary>
    private IImage? _image;

    /// <summary>The key of the icon requested most recently.</summary>
    private IconKey _key;

    /// <summary>
    /// Initializes static members of the <see cref="PlatformIcon"/> class.
    /// </summary>
    static PlatformIcon() => AffectsMeasure<PlatformIcon>(IconSizeProperty);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformIcon"/> class.
    /// </summary>
    public PlatformIcon() => ActualThemeVariantChanged += (_, _) => UpdateIcon();

    /// <summary>
    /// Gets or sets the <see cref="FileSystemNode"/> or <see cref="VolumeInfo"/> whose icon is shown.
    /// </summary>
    public object? Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon's width and height in device-independent pixels.
    /// </summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (_image is null)
        {
            return;
        }

        var size = IconSize;
        var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        context.DrawImage(_image, rect);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateIcon();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SubjectProperty || change.Property == IconSizeProperty)
        {
            UpdateIcon();
        }
    }

    /// <summary>
    /// Gets the resource key of the built-in icon for a subject.
    /// </summary>
    /// <param name="subject">The node or volume.</param>
    /// <returns>The resource key.</returns>
    private static string GetFallbackResourceKey(object subject)
    {
        var key = subject switch
        {
            FileSystemNode node => FallbackKeys.GetKey(node),
            VolumeInfo volume => FallbackKeys.GetKey(volume),
            _ => new IconKey(IconKey.FileCategory),
        };
        return $"WhatDisk.Icon.{key.Category}";
    }

    /// <summary>
    /// Shows the built-in icon for the subject and starts loading its platform icon.
    /// </summary>
    private void UpdateIcon()
    {
        if (Subject is not { } subject)
        {
            _image = null;
            InvalidateVisual();
            return;
        }

        var fallback = this.TryFindResource(GetFallbackResourceKey(subject), ActualThemeVariant, out var resource) ? resource as IImage : null;
        if (!this.TryFindResource(IconCache.ResourceKey, out var cacheResource) || cacheResource is not IconCache cache)
        {
            _image = fallback;
            InvalidateVisual();
            return;
        }

        var key = cache.GetKey(subject);
        _key = key;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var task = cache.GetIconAsync(key, (int)Math.Ceiling(IconSize * scaling));
        _image = task.IsCompletedSuccessfully ? task.Result ?? fallback : fallback;
        InvalidateVisual();
        if (!task.IsCompleted)
        {
            _ = ApplyWhenLoadedAsync(task, key);
        }
    }

    /// <summary>
    /// Replaces the built-in icon with the platform icon once it has loaded, if the subject has not changed.
    /// </summary>
    /// <param name="task">The loading task.</param>
    /// <param name="key">The key that was requested.</param>
    /// <returns>A task that completes when the icon has been applied.</returns>
    private async Task ApplyWhenLoadedAsync(Task<IImage?> task, IconKey key)
    {
        var image = await task;
        if (image is not null && _key == key)
        {
            _image = image;
            InvalidateVisual();
        }
    }
}
