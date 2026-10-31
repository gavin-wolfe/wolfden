using System.Text;
using System.Xml;

namespace Wolfden.Core.Structure;

/// <summary>
/// The image files Wolfden shows and adds (format.md F-LAY-9, #186, #187):
/// their types by extension, and a check that a file's contents are the
/// type its extension says.
/// </summary>
public static class ImageFiles
{
    /// <summary>The image types, by extension (lowercase, with the dot), in any case.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>The extensions, as a writer reads them: <c>.png, .jpg, ...</c>.</summary>
    public static string Listed => string.Join(", ", ContentTypes.Keys);

    /// <summary>
    /// The content type <paramref name="bytes"/> are, from their first bytes
    /// (their "magic number"), or null if they aren't any image type
    /// Wolfden shows. An SVG is text: it's one if it's well-formed XML
    /// whose first element is <c>svg</c>.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (IsAvif(bytes))
        {
            return "image/avif";
        }

        return IsSvg(bytes) ? "image/svg+xml" : null;
    }

    /// <summary>
    /// Why <paramref name="bytes"/> can't be saved as an image named with
    /// <paramref name="extension"/>, or null if they can: the extension must
    /// be one Wolfden shows, and the contents must be that type.
    /// </summary>
    public static string? Problem(string extension, ReadOnlySpan<byte> bytes)
    {
        if (!ContentTypes.TryGetValue(extension, out var expected))
        {
            return extension.Length == 0
                ? $"the file's name has no extension, so Wolfden can't tell what kind of image it is. It adds {Listed} files"
                : $"Wolfden adds {Listed} files, not {extension.ToLowerInvariant()}";
        }

        if (bytes.Length == 0)
        {
            return "the file is empty";
        }

        var found = Sniff(bytes);
        if (found == expected)
        {
            return null;
        }

        var kind = Name(expected);
        if (found is null)
        {
            return $"its name says it's a {kind} image, but it isn't one (or the file is damaged)";
        }

        var actual = ContentTypes.First(t => t.Value == found).Key;
        return $"its name says it's a {kind} image, but it's a {Name(found)}. Rename it to end in {actual} and add it again";
    }

    /// <summary>
    /// The alt text for an image named <paramref name="stem"/> (its file
    /// name without the extension): <c>-</c>, <c>_</c>, and runs of spaces
    /// become one space, and the brackets and backslashes Markdown would
    /// read are dropped. Empty if nothing's left, for the writer to fill in.
    /// </summary>
    public static string AltText(string stem)
    {
        var text = new StringBuilder();
        foreach (var c in stem)
        {
            if (c is '[' or ']' or '\\' || char.IsControl(c))
            {
                continue;
            }

            var space = c is '-' or '_' || char.IsWhiteSpace(c);
            if (space)
            {
                if (text.Length > 0 && text[^1] != ' ')
                {
                    text.Append(' ');
                }
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString().Trim();
    }

    private static string Name(string contentType) => contentType switch
    {
        "image/jpeg" => "JPEG",
        "image/svg+xml" => "SVG",
        _ => contentType["image/".Length..].ToUpperInvariant(),
    };

    /// <summary>An ISO base media file whose first box is <c>ftyp</c>, naming <c>avif</c> or <c>avis</c> among its brands.</summary>
    private static bool IsAvif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || !bytes[4..8].SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var size = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        if (size < 16 || size > bytes.Length || size % 4 != 0)
        {
            return false;
        }

        // The major brand at 8, the minor version at 12, then the compatible brands.
        for (var at = 8; at + 4 <= size; at += at == 8 ? 8 : 4)
        {
            var brand = bytes[at..(at + 4)];
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Well-formed XML (a DOCTYPE is skipped, never read) whose first element is <c>svg</c>.</summary>
    private static bool IsSvg(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text.TrimStart('﻿')), settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.LocalName != "svg")
                    {
                        return false;
                    }

                    // The rest must be well-formed too, or it isn't an image a browser shows.
                    while (reader.Read())
                    {
                    }

                    return true;
                }
            }

            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
