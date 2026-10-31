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
}
