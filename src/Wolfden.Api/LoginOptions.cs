using Wolfden.Accounts;

namespace Wolfden.Api;

/// <summary>
/// How the server asks for a login (#315), when login is on. With no
/// <see cref="LoginOptions"/> (<see cref="WolfdenApiOptions.Login"/> null),
/// login is off and every request acts as the owner, as before accounts.
/// </summary>
public sealed class LoginOptions
{
    /// <summary>Login for the accounts of <paramref name="installation"/>, whose id is <paramref name="installationId"/>.</summary>
    public LoginOptions(InstallationFolder installation, string installationId)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!InstallationSettings.IsInstallationId(installationId))
        {
            throw new ArgumentException($"'{installationId}' isn't an installation id.", nameof(installationId));
        }

        Installation = installation;
        InstallationId = installationId;
    }

    /// <summary>The installation: its <c>accounts.db</c>, and its <c>keys/</c> for the cookie.</summary>
    public InstallationFolder Installation { get; }

    /// <summary>The installation's id, which the cookie's name includes (<see cref="InstallationSettings.InstallationId"/>).</summary>
    public string InstallationId { get; }

    /// <summary>
    /// True when the den is reached through a public HTTPS origin (#315 D8,
    /// <c>serve --public-origin</c>, a later change). The cookie is then
    /// <c>Secure</c>, its name takes the <c>__Host-</c> prefix, and a session
    /// lasts 12 hours rather than 14 days. Off for 127.0.0.1, which is plain HTTP.
    /// </summary>
    public bool SecureOrigin { get; init; }

    /// <summary>How long a session lasts without use; each use slides it on.</summary>
    public TimeSpan Session => SecureOrigin ? TimeSpan.FromHours(12) : TimeSpan.FromDays(14);

    /// <summary>
    /// The login cookie's name: <c>wolfden-&lt;id&gt;</c>, or
    /// <c>__Host-wolfden-&lt;id&gt;</c> under a secure origin. Browsers
    /// share cookies across a host's ports, so each installation's own name
    /// keeps two Wolfdens on <c>localhost</c> from logging each other out.
    /// </summary>
    public string CookieName => (SecureOrigin ? "__Host-" : "") + "wolfden-" + InstallationId;

    /// <summary>
    /// True if login attempts are also limited per client address
    /// (<see cref="AttemptsPerAddress"/>). Only under a secure origin, where
    /// requests come through a proxy that says who sent them (#315 D8): on
    /// 127.0.0.1 every request has the same address, so one bucket for all
    /// would let anyone on this computer keep the owner out.
    /// </summary>
    public bool LimitsAddresses => LimitAddressesOverride ?? SecureOrigin;

    /// <summary>For tests: limit per address whatever the origin.</summary>
    internal bool? LimitAddressesOverride { get; init; }

    /// <summary>Login attempts allowed from one address in <see cref="AttemptWindow"/>, when <see cref="LimitsAddresses"/> (#315 §7: 10 a minute).</summary>
    internal int AttemptsPerAddress { get; init; } = 10;

    /// <summary>Login attempts allowed for one username in <see cref="AttemptWindow"/>, from anywhere (#315 §7: 10 a minute).</summary>
    internal int AttemptsPerUser { get; init; } = 10;

    /// <summary>The window the attempt limits count in.</summary>
    internal TimeSpan AttemptWindow { get; init; } = TimeSpan.FromMinutes(1);
}
