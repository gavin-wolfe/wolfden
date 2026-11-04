using Wolfden.Core.Structure;

namespace Wolfden.Cli;

/// <summary>
/// What a write command adds when its own look at the den, which reads as a view,
/// didn't find what it was given: a file open in another program may be why (#298).
/// </summary>
internal static class HeldFiles
{
    /// <summary><paramref name="problem"/>, and the den's container files that a write can't read now, if any.</summary>
    public static string Added(string root, string problem)
    {
        var den = new TrackingDenSource(new FileSystemDenSource(root));
        _ = DenStructure.Load(den);
        var held = den.Unreadable;
        return held.Count == 0
            ? problem
            : $"{problem} Couldn't read {string.Join(", ", held.Select(u => u.Path))} right now, which may be why. {UnreadablePath.WhatToDo(held)}";
    }
}
