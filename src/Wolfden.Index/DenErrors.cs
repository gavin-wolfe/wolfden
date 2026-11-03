using Wolfden.Core.Structure;

namespace Wolfden.Index;

/// <summary>What an error leaves out (format.md F-GEN-2).</summary>
public enum DenErrorReach
{
    /// <summary>One section and its subsections: a bad value in its comment.</summary>
    Section,

    /// <summary>The whole file: an entity, an outline, or a pack's file.</summary>
    File,

    /// <summary>A universe, series, or book and everything under it.</summary>
    Container,

    /// <summary>The whole den: <c>den.yaml</c> can't be loaded.</summary>
    Den,
}

/// <summary>An error in the den, with what it leaves out (<see cref="DenIndex.Errors"/>).</summary>
/// <param name="Path">The file or folder, relative to the den root.</param>
/// <param name="Line">The 1-based line, or null.</param>
/// <param name="Rule">The rule, such as <c>F-STAT-1</c>.</param>
/// <param name="Message">The message.</param>
/// <param name="Reach">What it leaves out.</param>
/// <param name="Section">For <see cref="DenErrorReach.Section"/>, the section's name. A heading can give away a reveal, so a view at a story position doesn't say it.</param>
/// <param name="SectionLine">For <see cref="DenErrorReach.Section"/>, the line of the section's heading.</param>
public sealed record DenError(string Path, int? Line, string Rule, string Message, DenErrorReach Reach, string? Section, int? SectionLine);

/// <summary>Works out what an error reaches.</summary>
public static class DenErrors
{
    /// <summary>What an error that isn't about one section reaches, by where it is (format.md F-GEN-2).</summary>
    /// <param name="path">The file or folder, relative to the den root.</param>
    public static DenErrorReach ReachOfFileError(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path == "den.yaml")
        {
            return DenErrorReach.Den;
        }

        return DenPaths.Name(path) is "universe.md" or "series.md" or "book.md"
            || !path.Contains('.', StringComparison.Ordinal) // a container folder without its file (F-LAY-4)
            ? DenErrorReach.Container
            : DenErrorReach.File;
    }
}
