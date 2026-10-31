namespace Wolfden.Mcp.Tests;

/// <summary>
/// <see cref="DenTools.AddImage"/> (#187) when another program makes a file
/// of the same name between listing the folder and writing: the image takes
/// the next name, and the other file is left as it is.
/// </summary>
public sealed class AddImageTests : IDisposable
{
    private const string Castle = "universes/dracula/entities/locations/castle-dracula.md";
    private const string Images = "universes/dracula/entities/locations/images";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];

    private readonly TempDen _den = TempDen.Dracula();

    public void Dispose() => _den.Dispose();

    private string Full(string path) => Path.Combine(_den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void AFileMadeMeanwhileByAnotherProgramIsKeptAndTheImageTakesTheNextName()
    {
        var raced = 0;
        var tools = new DenTools(_den.Root)
        {
            BeforeWrite = () =>
            {
                // Only the first try: another program saves map.png just before Wolfden does.
                if (raced++ == 0)
                {
                    Directory.CreateDirectory(Full(Images));
                    File.WriteAllBytes(Full($"{Images}/map.png"), [7, 7, 7]);
                }
            },
        };

        var result = tools.AddImage(Castle, "map.png", Png);

        Assert.Equal($"{Images}/map-2.png", result.Path);
        Assert.Equal("images/map-2.png", result.Link);
        Assert.Equal([7, 7, 7], File.ReadAllBytes(Full($"{Images}/map.png")));
        Assert.Equal(Png, File.ReadAllBytes(Full($"{Images}/map-2.png")));
        Assert.Equal(2, raced);
    }

    [Fact]
    public void AFolderLinkNamedImagesIsRefusedAndNothingIsWrittenThroughIt()
    {
        var target = Directory.CreateTempSubdirectory("wolfden-outside-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
            using (LinkFolder(Full("universes/dracula/entities/locations/Images"), target))
            {
                var e = Assert.Throws<DenToolException>(() => new DenTools(_den.Root).AddImage(Castle, "map.png", Png));

                Assert.Equal(ToolErrorCodes.AlreadyExists, e.Code);
                Assert.Contains("universes/dracula/entities/locations/Images is a folder link, which Wolfden doesn't follow", e.Message, StringComparison.Ordinal);
                Assert.Equal(["keep.txt"], Directory.GetFiles(target).Select(Path.GetFileName));
            }
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void WhereCaseMattersAnExactImagesFolderWinsOverImagesInAnotherCase()
    {
        // Only a case-sensitive file system (Linux) can hold images/ and Images/ side by side.
        Directory.CreateDirectory(Full("universes/dracula/entities/locations/Images"));
        Directory.CreateDirectory(Full(Images));
        if (Directory.GetDirectories(Full("universes/dracula/entities/locations")).Length < 2)
        {
            Assert.Skip("This file system takes names that differ only in case as one name.");
        }

        var result = new DenTools(_den.Root).AddImage(Castle, "map.png", Png);

        Assert.Equal($"{Images}/map.png", result.Path);
        Assert.Equal("images/map.png", result.Link);
        Assert.Empty(Directory.GetFiles(Full("universes/dracula/entities/locations/Images")));
    }

    /// <summary>
    /// Makes <paramref name="link"/> a link to the folder <paramref name="target"/>:
    /// a symbolic link where the OS allows it, else a junction on Windows, else
    /// the test is skipped. Disposing it removes the link, not what it points to.
    /// </summary>
    private static Removal LinkFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return new Removal(link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Windows without developer mode or admin rights; try a junction, which needs neither.
        }

        if (OperatingSystem.IsWindows())
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            mklink.WaitForExit();
            if (mklink.ExitCode == 0 && Directory.Exists(link))
            {
                return new Removal(link);
            }
        }

        Assert.Skip("This system doesn't allow folder links to be created.");
        return null!;
    }

    private sealed class Removal(string link) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
    }
}
