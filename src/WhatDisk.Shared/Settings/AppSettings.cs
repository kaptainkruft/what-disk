using System.Text.Json;
using System.Text.Json.Nodes;
using WhatDisk.Platform;

namespace WhatDisk.Settings;

/// <summary>
/// The app's settings, kept in a JSON file that holds only what differs from the defaults (as Visual Studio Code's
/// settings do), so that changing a default in a later version changes it for everyone who has not chosen otherwise:
/// <code>
/// {
///   "duplicates": { "sampleCount": 128, "readers": 1 },
///   "scanning": { "threads": 8, "macos": { "showHiddenVolumes": true } }
/// }
/// </code>
/// The file has the same format on every platform. Settings of one platform are stored in their section's
/// <c>macos</c> or <c>windows</c> subsection, and settings of other platforms, and keys this version does not know,
/// are kept as they are. Comments and trailing commas are accepted when reading. A value that is not valid for its
/// setting counts as the default; a number out of range is brought into range.
/// </summary>
/// <remarks>
/// Reading is safe from any thread. Changes are saved at once, by writing a temporary file and renaming it over the
/// settings file, so the file is never left half-written. <see cref="Changed"/> is raised on the thread that made the
/// change.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// The settings file's name.
    /// </summary>
    public const string FileName = "settings.json";

    /// <summary>How the file is read: comments and trailing commas are allowed, as people edit it by hand.</summary>
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Guards <see cref="_document"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The settings file's contents: only the values that differ from the defaults, plus unknown keys.</summary>
    private JsonObject _document = [];

    /// <summary>Whether the file could not be read, so it must be kept aside before it is first overwritten.</summary>
    private bool _keepInvalidFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettings"/> class and reads the settings file, if there is one.
    /// </summary>
    /// <param name="platform">The platform's conventions, which choose some defaults and the platform subsection.</param>
    /// <param name="filePath">The settings file, or <see langword="null"/> to keep settings in memory only.</param>
    public AppSettings(PlatformCapabilities platform, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(platform);
        Platform = platform;
        CurrentPlatform = platform.PlatformName switch
        {
            "macOS" => SettingPlatform.MacOS,
            "Windows" => SettingPlatform.Windows,
            _ => SettingPlatform.All,
        };
        FilePath = filePath;
        Load();
    }

    /// <summary>
    /// Raised after a setting's value changed, with the setting.
    /// </summary>
    public event EventHandler<SettingDefinition>? Changed;

    /// <summary>
    /// Gets the platform's conventions.
    /// </summary>
    public PlatformCapabilities Platform { get; }

    /// <summary>
    /// Gets the platform the app runs on, which chooses the platform subsection; <see cref="SettingPlatform.All"/>
    /// on a platform without its own settings.
    /// </summary>
    public SettingPlatform CurrentPlatform { get; }

    /// <summary>
    /// Gets the settings file, or <see langword="null"/> when settings are kept in memory only.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets why the settings file could not be read, or <see langword="null"/> when it was read or does not exist.
    /// </summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// Gets why the settings could not be saved, or <see langword="null"/> after a successful save.
    /// </summary>
    public string? SaveError { get; private set; }

    /// <summary>
    /// Gets the default location of the settings file: <c>~/Library/Application Support/WhatDisk</c> on macOS,
    /// <c>%APPDATA%\WhatDisk</c> on Windows, and <c>~/.config/WhatDisk</c> elsewhere.
    /// </summary>
    /// <returns>The path.</returns>
    public static string DefaultFilePath()
    {
        // .NET maps the local application data folder to ~/Library/Application Support on macOS.
        var folder = Environment.GetFolderPath(OperatingSystem.IsMacOS() ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Join(folder, "WhatDisk", FileName);
    }

    /// <summary>
    /// Gets a setting's value: the value in the file when it is valid, otherwise the default.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <returns>The value.</returns>
    public T Get<T>(SettingDefinition<T> setting)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(setting);
        lock (_gate)
        {
            return Find(setting) is { } node && setting.TryRead(node, out var value) ? value : setting.DefaultFor(Platform);
        }
    }

    /// <summary>
    /// Determines whether a setting differs from its default.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <returns><see langword="true"/> when the file holds a valid value other than the default.</returns>
    public bool IsModified(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        lock (_gate)
        {
            return Find(setting) is { } node && setting.ReadValue(node) is { } value && !value.Equals(setting.DefaultValueFor(Platform));
        }
    }

    /// <summary>
    /// Changes a setting and saves the file. A value equal to the default is removed from the file instead of written.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <param name="value">The value; a number out of range is brought into range.</param>
    public void Set<T>(SettingDefinition<T> setting, T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(setting);
        value = setting.Coerce(value);
        bool changed;
        lock (_gate)
        {
            var before = Find(setting) is { } node && setting.TryRead(node, out var current) ? current : setting.DefaultFor(Platform);
            var isDefault = EqualityComparer<T>.Default.Equals(value, setting.DefaultFor(Platform));
            changed = !EqualityComparer<T>.Default.Equals(before, value);
            if (isDefault)
            {
                Remove(setting);
            }
            else
            {
                Container(setting, create: true)![setting.Key] = setting.ToJson(value);
            }

            Save();
        }

        if (changed)
        {
            Changed?.Invoke(this, setting);
        }
    }

    /// <summary>
    /// Returns a setting to its default by removing it from the file.
    /// </summary>
    /// <param name="setting">The setting.</param>
    public void Reset(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        bool changed;
        lock (_gate)
        {
            changed = Find(setting) is { } node && setting.ReadValue(node) is { } value && !value.Equals(setting.DefaultValueFor(Platform));
            if (Find(setting) is not null)
            {
                Remove(setting);
                Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, setting);
        }
    }

    /// <summary>
    /// Returns every setting of this platform to its default. Settings of other platforms, and keys this version does
    /// not know, are kept.
    /// </summary>
    public void ResetAll()
    {
        foreach (var setting in SettingsCatalog.All.Where(setting => setting.AppliesTo(CurrentPlatform)))
        {
            Reset(setting);
        }
    }

    /// <summary>
    /// Reads the settings file again, for example after it was edited by hand, and raises <see cref="Changed"/> for
    /// each setting whose value changed.
    /// </summary>
    public void Reload()
    {
        var settings = SettingsCatalog.All.Where(setting => setting.AppliesTo(CurrentPlatform)).ToArray();
        var before = settings.Select(Value).ToArray();
        Load();
        for (var index = 0; index < settings.Length; index++)
        {
            if (!Equals(before[index], Value(settings[index])))
            {
                Changed?.Invoke(this, settings[index]);
            }
        }

        object Value(SettingDefinition setting)
        {
            lock (_gate)
            {
                return Find(setting) is { } node && setting.ReadValue(node) is { } value ? value : setting.DefaultValueFor(Platform);
            }
        }
    }

    /// <summary>
    /// Creates the settings file when it does not exist yet (with no settings, or those changed in memory), so that it
    /// can be opened for editing.
    /// </summary>
    public void EnsureFile()
    {
        lock (_gate)
        {
            if (FilePath is not null && !File.Exists(FilePath))
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Gets the settings file's contents as they would be saved.
    /// </summary>
    /// <returns>The JSON text, indented.</returns>
    public string ToJson()
    {
        lock (_gate)
        {
            using var stream = new MemoryStream();
            Write(stream);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>
    /// Reads the settings file. A missing file means every setting has its default; an unreadable or invalid one is
    /// reported in <see cref="LoadError"/>, and every setting has its default until it is fixed or overwritten.
    /// </summary>
    private void Load()
    {
        JsonObject document = [];
        string? error = null;
        if (FilePath is not null && File.Exists(FilePath))
        {
            try
            {
                var text = File.ReadAllText(FilePath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    document = JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                        ?? throw new JsonException("The settings file must hold a JSON object.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                error = $"The settings file could not be read, so the defaults are used: {exception.Message}";
            }
        }

        lock (_gate)
        {
            _document = document;
            _keepInvalidFile = error is not null;
            LoadError = error;
        }
    }

    /// <summary>
    /// Writes the settings file, if there is one: to a temporary file first, which is then renamed over it. Before an
    /// invalid file is first overwritten, it is kept as <c>settings.invalid.json</c>. Must be called holding the lock.
    /// </summary>
    private void Save()
    {
        if (FilePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            if (_keepInvalidFile && File.Exists(FilePath))
            {
                File.Copy(FilePath, System.IO.Path.ChangeExtension(FilePath, ".invalid.json"), overwrite: true);
            }

            var temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary))
            {
                Write(stream);
            }

            File.Move(temporary, FilePath, overwrite: true);
            _keepInvalidFile = false;
            LoadError = null;
            SaveError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SaveError = $"The settings could not be saved: {exception.Message}";
        }
    }

    /// <summary>
    /// Writes the document as indented JSON. Must be called holding the lock.
    /// </summary>
    /// <param name="stream">The stream.</param>
    private void Write(Stream stream)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        _document.WriteTo(writer);
    }

    /// <summary>
    /// Finds a setting's value in the document. Must be called holding the lock.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The value, or <see langword="null"/> when the file does not set it.</returns>
    private JsonNode? Find(SettingDefinition setting) => Container(setting, create: false)?[setting.Key];

    /// <summary>
    /// Gets the object that holds a setting: its section, or its section's platform subsection. Must be called
    /// holding the lock.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="create">Whether to create the section and subsection when they are missing (or not objects).</param>
    /// <returns>The object, or <see langword="null"/> when it does not exist and was not created.</returns>
    private JsonObject? Container(SettingDefinition setting, bool create)
    {
        var section = Child(_document, setting.Section, create);
        return setting.PlatformKey is { } platform && section is not null ? Child(section, platform, create) : section;

        static JsonObject? Child(JsonObject parent, string key, bool create)
        {
            if (parent[key] is JsonObject child)
            {
                return child;
            }

            if (!create)
            {
                return null;
            }

            child = [];
            parent[key] = child;
            return child;
        }
    }

    /// <summary>
    /// Removes a setting from the document, and then its platform subsection and section if they are left empty.
    /// Must be called holding the lock.
    /// </summary>
    /// <param name="setting">The setting.</param>
    private void Remove(SettingDefinition setting)
    {
        if (Container(setting, create: false) is not { } container)
        {
            return;
        }

        container.Remove(setting.Key);
        if (setting.PlatformKey is { } platform && container.Count == 0 && _document[setting.Section] is JsonObject parent)
        {
            parent.Remove(platform);
        }

        if (_document[setting.Section] is JsonObject { Count: 0 })
        {
            _document.Remove(setting.Section);
        }
    }
}
