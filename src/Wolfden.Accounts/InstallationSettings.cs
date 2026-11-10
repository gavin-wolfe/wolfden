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

    /// <summary>
    /// A short random id for this installation (#315 §3.6), or null if it
    /// has none yet or its value isn't one Wolfden would write. It goes in
    /// the login cookie's name, so two installations served on
    /// <c>localhost</c> don't overwrite each other's cookie. It's random,
    /// not derived from the folder's path, so the cookie's name says
    /// nothing about the computer account it belongs to.
    /// </summary>
    public string? InstallationId
    {
        get => _json[InstallationIdKey] is JsonValue value && value.TryGetValue<string>(out var id) && IsInstallationId(id) ? id : null;
        set => _json[InstallationIdKey] = value;
    }

    private const string InstallationIdKey = "installation_id";

    /// <summary>True for an id Wolfden writes: 12 lower-case letters and digits.</summary>
    public static bool IsInstallationId(string? id) =>
        id is { Length: 12 } && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    /// <summary>
    /// The installation's id: the one in <c>settings.json</c>, or a new
    /// random one, which is saved there first. A value that isn't an id
    /// (a hand edit) is replaced.
    /// </summary>
    public static string EnsureInstallationId(InstallationFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var settings = Load(folder);
        if (settings.InstallationId is { } id)
        {
            return id;
        }

        id = NewInstallationId();
        settings.InstallationId = id;
        settings.Save(folder);
        return id;
    }

    /// <summary>A new random id: 12 characters from a to z and 0 to 9, about 62 bits.</summary>
    internal static string NewInstallationId() =>
        System.Security.Cryptography.RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 12);

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

        var json = ParseObject(folder);
        if (json[LoginKey] is { } login && !(login is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"'{folder.SettingsFile}': \"login\" should be true or false.");
        }

        return new InstallationSettings(json);
    }

    /// <summary>
    /// Turns login off in a settings file <see cref="Load"/> refused, so
    /// <c>login off</c> always works. The old file is first copied to
    /// <c>settings.json.broken</c> (replacing an older one). If it's still
    /// one JSON object, only <c>login</c> changes and every other key is
    /// kept; otherwise it's replaced by the defaults.
    /// </summary>
    /// <returns>A sentence saying what was done, naming the copy.</returns>
    public static string TurnLoginOffRepairing(InstallationFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var backup = folder.SettingsFile + ".broken";
        File.Copy(folder.SettingsFile, backup, overwrite: true);
        JsonObject? json;
        try
        {
            json = ParseObject(folder);
        }
        catch (InvalidDataException)
        {
            json = null;
        }

        var settings = new InstallationSettings(json ?? []) { Login = false };
        settings.Save(folder);
        return json is null
            ? $"It couldn't be read, so it's replaced with one that turns login off; the old file is kept as '{backup}'."
            : $"Its \"login\" is now false, and its other settings are kept; the old file is kept as '{backup}'.";
    }

    /// <summary>Reads the settings file as one JSON object, or throws <see cref="InvalidDataException"/>.</summary>
    private static JsonObject ParseObject(InstallationFolder folder)
    {
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

        return node as JsonObject
            ?? throw new InvalidDataException($"'{folder.SettingsFile}' should hold one JSON object, such as {{ \"login\": false }}.");
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
