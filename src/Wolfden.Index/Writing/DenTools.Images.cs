using Wolfden.Core.Editing;
using Wolfden.Core.Structure;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Adding an image to a note from the web UI's editor (#187): the image is
/// saved in an <c>images/</c> folder beside the note's file, under a name
/// the den picks, and the result gives the path to write in the note
/// (format.md F-LAY-9).
/// </summary>
public sealed partial class DenTools
{
    /// <summary>The largest image <see cref="AddImage"/> saves: 10 MiB.</summary>
    public const int MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>The folder beside a note that its added images go in.</summary>
    public const string ImageFolder = "images";

    /// <summary>The most names <see cref="AddImage"/> tries (<c>map.png</c>, <c>map-2.png</c>, ...) before it gives up.</summary>
    private const int MostImageNames = 1000;

    /// <summary>
    /// Saves <paramref name="bytes"/> as an image for <paramref name="entity"/>:
    /// in the <c>images/</c> folder beside its file, named from
    /// <paramref name="fileName"/> as a slug (<c>Castle Map.PNG</c> as
    /// <c>castle-map.png</c>). A file already there is never replaced: the
    /// name gets <c>-2</c>, <c>-3</c>, and so on. Only the image types the
    /// web UI shows are taken (<see cref="ImageFiles"/>), and the contents
    /// must be the type the extension says. It's refused in a final book.
    /// The note itself isn't changed: the page inserts the Markdown, and the
    /// writer saves the note.
    /// </summary>
    public AddImageResult AddImage(string entity, string? fileName, byte[] bytes, string? universe = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var given = Path.GetFileName((fileName ?? "").Replace('\\', '/').Trim());
        if (given.Length == 0)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "Give the image's file name, such as 'castle-map.png', so Wolfden can tell what kind of image it is.");
        }

        var extension = Path.GetExtension(given);
        if (bytes.Length > MaxImageBytes)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"'{given}' is {Megabytes(bytes.Length)}, and Wolfden adds images of up to {Megabytes(MaxImageBytes)}. Make it smaller in an image editor, then add it again.");
        }

        if (ImageFiles.Problem(extension, bytes) is { } problem)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"'{given}' wasn't added: {problem}.");
        }

        var stem = given[..^extension.Length];
        var slug = NewEntity.Slugify(stem);
        if (slug.Length == 0)
        {
            slug = "image";
        }

        var ext = extension.ToLowerInvariant();
        if (DenPaths.Problem($"{slug}{ext}") is not null)
        {
            // A name Windows keeps for a device, such as CON.png.
            slug += "-image";
        }

        var (path, _) = Locate(entity, universe);
        var noteFolder = DenPaths.Parent(path);
        using (EnterWrite())
        {
            var den = Snapshot();
            if (!den.FileExists(path))
            {
                throw Gone(path);
            }

            // An images folder that's there already, in any case, is used as it's spelled
            // (Images/ made by hand on Windows); a file or folder link by that name is in the way.
            var folderName = ImageFolder;
            static bool IsImages(string name) => string.Equals(name, ImageFolder, StringComparison.OrdinalIgnoreCase);
            var existing = Store.GetDirectories(noteFolder).FirstOrDefault(IsImages);
            if (existing is null && Store.GetFolderLinks(noteFolder).FirstOrDefault(IsImages) is { } folderLink)
            {
                throw new DenToolException(
                    ToolErrorCodes.AlreadyExists,
                    $"{Join(noteFolder, folderLink)} is a folder link, which Wolfden doesn't follow, so it can't save the image there. Make it an ordinary folder, or rename it, then add the image again.");
            }

            if (existing is not null)
            {
                folderName = existing;
            }
            else if (Store.GetFiles(noteFolder).FirstOrDefault(IsImages) is { } blocking)
            {
                throw new DenToolException(
                    ToolErrorCodes.AlreadyExists,
                    $"There's a file named '{blocking}' beside the note ({Join(noteFolder, blocking)}), where Wolfden keeps the note's images in a folder of that name. Rename or move the file, then add the image again.");
            }

            var folder = Join(noteFolder, folderName);
            var structure = DenStructure.Load(den);
            var frozen = structure.Universes.SelectMany(u => u.Books)
                .FirstOrDefault(b => b.Status == BookStatus.Final && IsAtOrUnder(folder, b.Path));
            if (frozen is not null)
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen,
                    $"{path} is in book '{frozen.Key}', which is final (frozen), so nothing can be added to it until the book is unfrozen (F-BOOK-1). {Unfreezing(frozen)}");
            }

            // Every name already in the folder, files and folders, in any case: on
            // Windows and macOS a name that differs only in case is the same file.
            var taken = Store.GetFiles(folder).Concat(Store.GetDirectories(folder)).Concat(Store.GetFolderLinks(folder))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var n = 1; n <= MostImageNames; n++)
            {
                var name = n == 1 ? $"{slug}{ext}" : $"{slug}-{n}{ext}";
                if (taken.Contains(name))
                {
                    continue;
                }

                var target = $"{folder}/{name}";
                try
                {
                    Save(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [target] = bytes }, originals: null, created: target);
                }
                catch (DenToolException e) when (e.Code == ToolErrorCodes.AlreadyExists)
                {
                    // Made by another program since the folder was listed: try the next name.
                    taken.Add(name);
                    continue;
                }

                var written = $"{folderName}/{name}";
                var effects = new List<string>();
                if (n > 1)
                {
                    effects.Add($"The note's {folderName} folder already has {slug}{ext}, so the image was saved as {name}.");
                }

                return new AddImageResult(DenReader.ResultVersion, target, written, ImageFiles.AltText(stem), bytes.Length, effects);
            }

            throw new DenToolException(
                ToolErrorCodes.AlreadyExists,
                $"{folder} already has {slug}{ext} and {MostImageNames - 1} numbered copies of it, so nothing was added. Rename the image, then add it again.");
        }
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private static string Megabytes(long bytes) =>
        $"{(bytes / (1024.0 * 1024.0)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} MB";
}
