namespace Wolfden.Core.Documents;

/// <summary>How serious a <see cref="Diagnostic"/> is (format.md §1.3).</summary>
public enum DiagnosticSeverity
{
    /// <summary>The file is valid and indexed, but something deserves a look.</summary>
    Warning,

    /// <summary>The file is invalid. Writers must refuse to write it.</summary>
    Error,
}

/// <summary>
/// The section an error in its comment leaves out, with its subsections
/// (format.md F-GEN-2). <paramref name="Attribute"/> and
/// <paramref name="Value"/> say what's wrong without a line number, so a
/// save can tell an error it found from one it adds, wherever the text moved
/// (<see cref="Editing.DenWriteCheck"/>).
/// </summary>
/// <param name="Name">The section's name.</param>
/// <param name="Line">The 1-based line of the section's heading.</param>
/// <param name="Attribute">The attribute with the bad value, as written.</param>
/// <param name="Value">The value, as written.</param>
public sealed record LeftOutSection(string Name, int Line, string Attribute, string Value)
{
    /// <summary>
    /// True if another error leaves out the whole file or container too, so the
    /// comment's error reaches no further than that error does. It's still
    /// reported, and still known across a save by its attribute and value.
    /// </summary>
    public bool FileLeftOutToo { get; init; }
}

/// <summary>
/// A problem found while parsing a file, cited by its format.md rule id
/// (for example <c>F-YAML-3</c>).
/// </summary>
/// <param name="RuleId">The format.md rule, such as <c>F-ENC-3</c>.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Line">The 1-based line in the file, counting frontmatter lines, or null when the problem has no line.</param>
/// <param name="Message">A message for the writer.</param>
public sealed record Diagnostic(string RuleId, DiagnosticSeverity Severity, int? Line, string Message)
{
    /// <summary>
    /// For a bad value in a well-formed section comment (format.md F-GEN-2,
    /// F-SEC-10), the section it leaves out. Null for every other diagnostic,
    /// whose reach is the whole file or container.
    /// </summary>
    public LeftOutSection? LeftOut { get; init; }

    /// <summary>The section this error alone leaves out: <see cref="LeftOut"/>, unless another error leaves out the whole file too. Null otherwise.</summary>
    public LeftOutSection? LeavesOutOnly => LeftOut is { FileLeftOutToo: false } section ? section : null;

    /// <summary>The sentence a section's error ends with, naming what it leaves out.</summary>
    internal static string LeftOutSentence(string sectionName) =>
        $" {(sectionName.Length > 0 ? $"'{sectionName}'" : "This section")} is left out until this is fixed.";

    /// <summary>
    /// This error as a pack's template has it: a template isn't a note, so
    /// a bad section comment there is an error in the template, and no
    /// section of a den is left out (types.md F-PACK-2).
    /// </summary>
    internal Diagnostic ForTemplate() => LeftOut is { } section
        ? this with { LeftOut = null, Message = Message[..^LeftOutSentence(section.Name).Length] }
        : this;

    /// <summary>
    /// This error in a file another error leaves out whole (format.md F-GEN-2):
    /// it no longer says a section is left out, since the whole file is.
    /// </summary>
    internal Diagnostic InExcludedFile() => LeftOut is { FileLeftOutToo: false } section
        ? this with { LeftOut = section with { FileLeftOutToo = true }, Message = Message[..^LeftOutSentence(section.Name).Length] }
        : this;

    /// <inheritdoc />
    public override string ToString() =>
        Line is int line ? $"{line}: {Severity} {RuleId}: {Message}" : $"{Severity} {RuleId}: {Message}";
}
