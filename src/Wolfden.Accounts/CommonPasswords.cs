using System.Collections.Frozen;

namespace Wolfden.Accounts;

/// <summary>
/// The common-password blocklist (#354): <c>CommonPasswords/common-passwords.txt</c>,
/// a public-domain list of nearly 10,000 (see the README beside it), built into
/// this assembly. It's read once, the first time a password is checked,
/// and never fetched from anywhere.
/// </summary>
internal sealed class CommonPasswords
{
    /// <summary>The name the list is built into the assembly under (see the .csproj).</summary>
    internal const string ResourceName = "Wolfden.Accounts.common-passwords.txt";

    /// <summary>
    /// The fewest characters an entry keeps after <see cref="PasswordRules.Skeleton"/>.
    /// Shorter ones are left over from entries made mostly of symbols
    /// (<c>&amp;amp;</c> is <c>amp</c>), not passwords people use.
    /// </summary>
    internal const int ShortestEntry = 4;

    private readonly Lazy<FrozenSet<string>> entries;
    private int loads;

    internal CommonPasswords(Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        entries = new Lazy<FrozenSet<string>>(() => Read(open), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The list built into Wolfden.</summary>
    internal static CommonPasswords Default { get; } = new(OpenResource);

    /// <summary>How many times the list has been read: 0 until a password is checked, then 1.</summary>
    internal int Loads => Volatile.Read(ref loads);

    /// <summary>How many distinct entries the list has, compared as skeletons.</summary>
    internal int Count => entries.Value.Count;

    /// <summary>Whether <paramref name="skeleton"/> (see <see cref="PasswordRules.Skeleton"/>) is on the list.</summary>
    internal bool Contains(string skeleton) => entries.Value.Contains(skeleton);

    private static Stream OpenResource() =>
        typeof(CommonPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource '{ResourceName}' isn't in {typeof(CommonPasswords).Assembly.GetName().Name}.");

    private FrozenSet<string> Read(Func<Stream> open)
    {
        Interlocked.Increment(ref loads);
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StreamReader(open(), System.Text.Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (PasswordRules.TryNormalize(line, out var normalized) && PasswordRules.Skeleton(normalized) is { Length: >= ShortestEntry } skeleton)
            {
                set.Add(skeleton);
            }
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }
}
