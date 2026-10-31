using System.Text;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// Telling an image's type from its contents (#187), so the editor adds
/// only files that are the image type their name says.
/// </summary>
public class ImageFileTests
{
    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var brands = new[] { major, "\0\0\0\0" }.Concat(compatible).ToArray();
        var size = 8 + (brands.Length * 4);
        var box = new List<byte> { 0, 0, 0, (byte)size };
        box.AddRange("ftyp"u8.ToArray());
        foreach (var brand in brands)
        {
            box.AddRange(Encoding.ASCII.GetBytes(brand));
        }

        box.AddRange([0, 0, 0, 8, (byte)'m', (byte)'e', (byte)'t', (byte)'a']);
        return [.. box];
    }

    public static TheoryData<byte[], string?> Samples => new()
    {
        { Bytes(0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0), "image/png" },
        { Bytes(0x89, 'P', 'N', 'G', 0x0D, 0x0A), null },
        { Bytes(0xFF, 0xD8, 0xFF, 0xDB, 0), "image/jpeg" },
        { "GIF89a\x01\0"u8.ToArray(), "image/gif" },
        { "GIF87a\x01\0"u8.ToArray(), "image/gif" },
        { "RIFF\x10\0\0\0WEBPVP8 "u8.ToArray(), "image/webp" },
        { "RIFF\x10\0\0\0WAVEfmt "u8.ToArray(), null },
        { Ftyp("avif", "mif1", "miaf"), "image/avif" },
        { Ftyp("mif1", "avif"), "image/avif" },
        { Ftyp("isom", "mp41"), null },
        { "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "image/svg+xml" },
        {
            Encoding.UTF8.GetBytes("﻿<?xml version=\"1.0\"?>\n<!-- made by hand -->\n<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n<svg><rect/></svg>"),
            "image/svg+xml"
        },
        { "<html><svg/></html>"u8.ToArray(), null },
        { "<svg><unclosed></svg>"u8.ToArray(), null },
        { "just words"u8.ToArray(), null },
        { Bytes(0xC3, 0x28, '<', 's', 'v', 'g', '/', '>'), null },
        { [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-16\"?><svg/>")], "image/svg+xml" },
        { [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("<svg/>")], "image/svg+xml" },
        { Array.Empty<byte>(), null },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void ItsTypeComesFromItsFirstBytes(byte[] bytes, string? type) =>
        Assert.Equal(type, ImageFiles.Sniff(bytes));

    [Fact]
    public void AnEntityInTheDoctypeIsNeverRead()
    {
        // A DOCTYPE is skipped, not processed, so an entity can't pull in a file or blow up.
        var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg>&x;</svg>"u8.ToArray();
        Assert.Null(ImageFiles.Sniff(svg));
    }

    [Fact]
    public void AnSvgThatDeclaresEntitiesIsRefusedWithWhatToDo()
    {
        // As Adobe Illustrator's SVG 1.1 Save As writes it.
        var svg = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?>\n<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\" [\n" +
            "  <!ENTITY ns_svg \"http://www.w3.org/2000/svg\">\n]>\n<svg xmlns=\"&ns_svg;\"><rect/></svg>");

        Assert.Null(ImageFiles.Sniff(svg));
        var problem = ImageFiles.Problem(".svg", svg);
        Assert.Contains("declares entities in a DOCTYPE, which Wolfden doesn't accept", problem, StringComparison.Ordinal);
        Assert.Contains("Export As", problem, StringComparison.Ordinal);
        Assert.Contains("PNG", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("damaged", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongFileNameGivesAltTextCutAtAWord()
    {
        var alt = ImageFiles.AltText(string.Join("-", Enumerable.Repeat("castle", 40)));
        Assert.True(alt.Length <= ImageFiles.MaxAltLength);
        Assert.EndsWith("castle", alt, StringComparison.Ordinal);
        Assert.Equal(new string('x', ImageFiles.MaxAltLength), ImageFiles.AltText(new string('x', 300)));
    }

    [Theory]
    [InlineData(".png", null)]
    [InlineData(".PNG", null)]
    [InlineData(".jpg", "it's a PNG. Rename it to end in .png")]
    [InlineData(".bmp", "not .bmp")]
    [InlineData("", "no extension")]
    public void AProblemSaysWhatsWrongAndWhatToDo(string extension, string? said)
    {
        var png = Bytes(0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A, 0);
        var problem = ImageFiles.Problem(extension, png);
        if (said is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.Contains(said, problem, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("castle-map", "castle map")]
    [InlineData("Castle_Map  [draft]", "Castle Map draft")]
    [InlineData("--", "")]
    [InlineData("a\\b", "ab")]
    public void AltTextComesFromTheFileName(string stem, string alt) =>
        Assert.Equal(alt, ImageFiles.AltText(stem));
}
