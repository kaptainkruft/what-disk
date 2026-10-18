using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhatDisk.Platform;

namespace WhatDisk.Settings;

/// <summary>
/// The platforms a setting applies to. A setting for one platform is stored in that platform's subsection of its
/// section in the settings file (for example <c>scanning.macos.showHiddenVolumes</c>), so one file can serve every
/// platform.
/// </summary>
public enum SettingPlatform
{
    /// <summary>Every platform; stored directly in its section.</summary>
    All,

    /// <summary>macOS only; stored in the section's <c>macos</c> subsection.</summary>
    MacOS,

    /// <summary>Windows only; stored in the section's <c>windows</c> subsection.</summary>
    Windows,
}

/// <summary>
/// Describes one setting: where it is stored in the settings file, what it is called and means, and which values it
/// accepts. The settings window is built from these descriptions.
/// </summary>
public abstract class SettingDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingDefinition"/> class.
    /// </summary>
    /// <param name="section">The section of the settings file, such as <c>duplicates</c>.</param>
    /// <param name="key">The key within the section (or its platform subsection), such as <c>sampleCount</c>.</param>
    /// <param name="platform">The platforms the setting applies to.</param>
    /// <param name="label">The label in the settings window.</param>
    /// <param name="description">What the setting does, shown under its label.</param>
    protected SettingDefinition(string section, string key, SettingPlatform platform, string label, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(description);
        Section = section;
        Key = key;
        Platform = platform;
        Label = label;
        Description = description;
    }

    /// <summary>
    /// Gets the section of the settings file the setting is stored in.
    /// </summary>
    public string Section { get; }

    /// <summary>
    /// Gets the setting's key within its section, or within the section's platform subsection.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the platforms the setting applies to.
    /// </summary>
    public SettingPlatform Platform { get; }

    /// <summary>
    /// Gets the label shown in the settings window.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets what the setting does, shown under its label.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the key of the platform subsection the setting is stored in (<c>macos</c> or <c>windows</c>), or
    /// <see langword="null"/> for a setting of every platform.
    /// </summary>
    public string? PlatformKey => KeyOf(Platform);

    /// <summary>
    /// Gets the name of the platform the setting is limited to, such as "macOS", or <see langword="null"/>.
    /// </summary>
    public string? PlatformName => Platform switch
    {
        SettingPlatform.MacOS => "macOS",
        SettingPlatform.Windows => "Windows",
        _ => null,
    };

    /// <summary>
    /// Gets the setting's dotted path in the settings file, such as <c>scanning.macos.showHiddenVolumes</c>.
    /// </summary>
    public string Path => PlatformKey is { } platform ? $"{Section}.{platform}.{Key}" : $"{Section}.{Key}";

    /// <summary>
    /// Gets the key of a platform's subsection.
    /// </summary>
    /// <param name="platform">The platform.</param>
    /// <returns><c>macos</c>, <c>windows</c>, or <see langword="null"/> for every platform.</returns>
    public static string? KeyOf(SettingPlatform platform) => platform switch
    {
        SettingPlatform.MacOS => "macos",
        SettingPlatform.Windows => "windows",
        _ => null,
    };

    /// <summary>
    /// Determines whether the setting applies to a platform.
    /// </summary>
    /// <param name="platform">The platform the app runs on.</param>
    /// <returns><see langword="true"/> for a setting of every platform or of that platform.</returns>
    public bool AppliesTo(SettingPlatform platform) => Platform == SettingPlatform.All || Platform == platform;

    /// <summary>
    /// Gets the default value on a platform, boxed.
    /// </summary>
    /// <param name="platform">The platform's conventions.</param>
    /// <returns>The default.</returns>
    internal abstract object DefaultValueFor(PlatformCapabilities platform);

    /// <summary>
    /// Reads a value from the settings file, boxed.
    /// </summary>
    /// <param name="node">The value in the file.</param>
    /// <returns>The value, or <see langword="null"/> when it is not a valid value for the setting.</returns>
    internal abstract object? ReadValue(JsonNode node);
}

/// <summary>
/// Describes a setting of a particular type.
/// </summary>
/// <typeparam name="T">The type of value.</typeparam>
public abstract class SettingDefinition<T> : SettingDefinition
    where T : notnull
{
    /// <summary>Gets the default value on a platform.</summary>
    private readonly Func<PlatformCapabilities, T> _default;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingDefinition{T}"/> class.
    /// </summary>
    /// <param name="section">The section of the settings file.</param>
    /// <param name="key">The key within the section (or its platform subsection).</param>
    /// <param name="platform">The platforms the setting applies to.</param>
    /// <param name="label">The label in the settings window.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="defaultValue">Gets the default value, which may follow the platform's conventions.</param>
    protected SettingDefinition(string section, string key, SettingPlatform platform, string label, string description, Func<PlatformCapabilities, T> defaultValue)
        : base(section, key, platform, label, description)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        _default = defaultValue;
    }

    /// <summary>
    /// Gets the default value on a platform.
    /// </summary>
    /// <param name="platform">The platform's conventions.</param>
    /// <returns>The default.</returns>
    public T DefaultFor(PlatformCapabilities platform) => _default(platform);

    /// <summary>
    /// Reads a value from the settings file.
    /// </summary>
    /// <param name="node">The value in the file.</param>
    /// <param name="value">The value, when valid; out-of-range numbers are brought into range.</param>
    /// <returns><see langword="true"/> when the value is valid for the setting.</returns>
    public abstract bool TryRead(JsonNode node, out T value);

    /// <summary>
    /// Converts a value for the settings file.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The JSON value.</returns>
    public abstract JsonNode ToJson(T value);

    /// <summary>
    /// Brings a value into the setting's range.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or the nearest valid value.</returns>
    public virtual T Coerce(T value) => value;

    /// <inheritdoc/>
    internal sealed override object DefaultValueFor(PlatformCapabilities platform) => DefaultFor(platform);

    /// <inheritdoc/>
    internal sealed override object? ReadValue(JsonNode node) => TryRead(node, out var value) ? value : null;
}

/// <summary>
/// An on/off setting, stored as <c>true</c> or <c>false</c>.
/// </summary>
/// <param name="section">The section of the settings file.</param>
/// <param name="key">The key within the section (or its platform subsection).</param>
/// <param name="label">The label in the settings window.</param>
/// <param name="description">What the setting does.</param>
/// <param name="defaultValue">The default.</param>
/// <param name="platform">The platforms the setting applies to.</param>
public sealed class BoolSetting(string section, string key, string label, string description, bool defaultValue, SettingPlatform platform = SettingPlatform.All)
    : SettingDefinition<bool>(section, key, platform, label, description, _ => defaultValue)
{
    /// <inheritdoc/>
    public override bool TryRead(JsonNode node, out bool value)
    {
        ArgumentNullException.ThrowIfNull(node);
        value = false;
        return node.GetValueKind() is JsonValueKind.True or JsonValueKind.False && node.AsValue().TryGetValue(out value);
    }

    /// <inheritdoc/>
    public override JsonNode ToJson(bool value) => JsonValue.Create(value);
}

/// <summary>
/// A whole-number setting with a range, stored as a JSON number. Numbers outside the range are brought into it.
/// </summary>
/// <param name="section">The section of the settings file.</param>
/// <param name="key">The key within the section (or its platform subsection).</param>
/// <param name="label">The label in the settings window.</param>
/// <param name="description">What the setting does.</param>
/// <param name="defaultValue">The default.</param>
/// <param name="minimum">The smallest value allowed.</param>
/// <param name="maximum">The largest value allowed.</param>
/// <param name="platform">The platforms the setting applies to.</param>
public sealed class IntSetting(string section, string key, string label, string description, int defaultValue, int minimum, int maximum, SettingPlatform platform = SettingPlatform.All)
    : SettingDefinition<int>(section, key, platform, label, description, _ => defaultValue)
{
    /// <summary>
    /// Gets the smallest value allowed.
    /// </summary>
    public int Minimum { get; } = minimum;

    /// <summary>
    /// Gets the largest value allowed.
    /// </summary>
    public int Maximum { get; } = maximum;

    /// <summary>
    /// Gets the unit shown after the value, such as "MiB" or "files", or <see langword="null"/>.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets what zero means, such as "Automatic", when zero is a special value; otherwise <see langword="null"/>.
    /// </summary>
    public string? ZeroMeans { get; init; }

    /// <inheritdoc/>
    public override bool TryRead(JsonNode node, out int value)
    {
        ArgumentNullException.ThrowIfNull(node);
        value = 0;
        if (node.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        // A value read from the file converts to any number type; one set in this session keeps the type it was set as.
        var json = node.AsValue();
        if (json.TryGetValue(out int whole))
        {
            value = Coerce(whole);
            return true;
        }

        if (json.TryGetValue(out double number) && !double.IsNaN(number))
        {
            value = (int)Math.Clamp(Math.Round(number), Minimum, Maximum);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override JsonNode ToJson(int value) => JsonValue.Create(value);

    /// <inheritdoc/>
    public override int Coerce(int value) => Math.Clamp(value, Minimum, Maximum);

    /// <summary>
    /// Describes the range for the settings window, such as "1–64 threads".
    /// </summary>
    /// <returns>The description.</returns>
    public string DescribeRange() =>
        string.Create(CultureInfo.CurrentCulture, $"{Minimum:N0}–{Maximum:N0}{(Unit is null ? string.Empty : $" {Unit}")}");
}

/// <summary>
/// One of the values a <see cref="ChoiceSetting"/> accepts.
/// </summary>
/// <param name="Value">The value stored in the settings file.</param>
/// <param name="Label">The value's name in the settings window.</param>
public sealed record SettingChoice(string Value, string Label);

/// <summary>
/// A setting that takes one of a fixed list of values, stored as a JSON string.
/// </summary>
/// <param name="section">The section of the settings file.</param>
/// <param name="key">The key within the section (or its platform subsection).</param>
/// <param name="label">The label in the settings window.</param>
/// <param name="description">What the setting does.</param>
/// <param name="choices">The values it accepts.</param>
/// <param name="defaultValue">Gets the default, which may follow the platform's conventions.</param>
/// <param name="platform">The platforms the setting applies to.</param>
public sealed class ChoiceSetting(
    string section,
    string key,
    string label,
    string description,
    IReadOnlyList<SettingChoice> choices,
    Func<PlatformCapabilities, string> defaultValue,
    SettingPlatform platform = SettingPlatform.All)
    : SettingDefinition<string>(section, key, platform, label, description, defaultValue)
{
    /// <summary>
    /// Gets the values the setting accepts.
    /// </summary>
    public IReadOnlyList<SettingChoice> Choices { get; } = choices ?? throw new ArgumentNullException(nameof(choices));

    /// <inheritdoc/>
    public override bool TryRead(JsonNode node, out string value)
    {
        ArgumentNullException.ThrowIfNull(node);
        value = string.Empty;
        if (node.GetValueKind() != JsonValueKind.String || !node.AsValue().TryGetValue(out string? text)
            || Choices.FirstOrDefault(choice => string.Equals(choice.Value, text, StringComparison.OrdinalIgnoreCase)) is not { } match)
        {
            return false;
        }

        value = match.Value;
        return true;
    }

    /// <inheritdoc/>
    public override JsonNode ToJson(string value) => JsonValue.Create(value)!;
}
