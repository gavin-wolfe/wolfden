using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wolfden.Accounts;

/// <summary>
/// An installation's <c>settings.json</c>: whether login is on, and later
/// the public origin (#315). Keys this version doesn't know are kept as
/// they are when it saves.
/// </summary>
public sealed class InstallationSettings
{
    private const string LoginKey = "login";

    private readonly JsonObject _json;

    private InstallationSettings(JsonObject json)
    {
        _json = json;
    }

    /// <summary>Whether login is on. Off when the file or the key is missing.</summary>
    public bool Login
    {
        get => _json[LoginKey] is JsonValue value && value.TryGetValue<bool>(out var on) && on;
        set => _json[LoginKey] = value;
    }

    /// <summary>The defaults, login off, for replacing a file that doesn't read.</summary>
    public static InstallationSettings Defaults() => new([]);

    /// <summary>
    /// Reads the installation's settings. A missing file is the defaults
    /// (login off). A file that isn't a JSON object, or whose <c>login</c>
    /// isn't true or false, throws <see cref="InvalidDataException"/> with a
    /// message that names the file.
    /// </summary>
    public static InstallationSettings Load(InstallationFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!File.Exists(folder.SettingsFile))
        {
            return new InstallationSettings([]);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(
                File.ReadAllText(folder.SettingsFile),
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, AllowDuplicateProperties = false });

            // JsonObject reads its properties lazily; read them now, so a
            // repeated key fails here and not in whoever asks later.
            _ = (node as JsonObject)?.Count;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new InvalidDataException($"'{folder.SettingsFile}' isn't valid JSON, or names a setting twice: {e.Message}", e);
        }

        if (node is not JsonObject json)
        {
            throw new InvalidDataException($"'{folder.SettingsFile}' should hold one JSON object, such as {{ \"login\": false }}.");
        }

        if (json[LoginKey] is { } login && !(login is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"'{folder.SettingsFile}': \"login\" should be true or false.");
        }

        return new InstallationSettings(json);
    }

    /// <summary>
    /// Writes the settings, creating the folder if needed. It writes a
    /// temporary file and moves it into place, so a crash leaves the old
    /// file or the new one, never half of one.
    /// </summary>
    public void Save(InstallationFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        folder.Create();
        var temp = folder.SettingsFile + ".tmp";
        var text = _json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(temp, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        File.Move(temp, folder.SettingsFile, overwrite: true);
    }
}
