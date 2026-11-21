using Microsoft.AspNetCore.Http;

namespace Wolfden.Api;

/// <summary>
/// Notices when the installation's login setting changes while the server
/// runs (#315), and fails safe: the server keeps the setting it started
/// with for everything it allows, but a login turned on after it started
/// stops it serving until it's restarted. It reads the setting at most once
/// every <see cref="WolfdenApiOptions.LoginSettingEvery"/>.
/// </summary>
internal sealed class LoginWatch(bool startedOn, Func<bool> read, TimeProvider time, TimeSpan every, TextWriter? log)
{
    /// <summary>What a request gets once login was turned on after the server started.</summary>
    internal static readonly ApiError RestartNeeded = new(
        "restart_needed", "Login was turned on. Restart wolfden serve: until then it answers nothing, so nobody can use the page without a login.");

    private readonly Lock _gate = new();
    private long _nextRead = long.MinValue;
    private bool _turnedOn;
    private bool _toldOff;

    /// <summary>True once login has been turned on since a server that started without it.</summary>
    public bool RestartNeededNow()
    {
        lock (_gate)
        {
            if (_turnedOn)
            {
                return true;
            }

            var now = time.GetTimestamp();
            if (now < _nextRead)
            {
                return false;
            }

            _nextRead = now + (long)(every.TotalSeconds * time.TimestampFrequency);
            bool on;
            try
            {
                on = read();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // A setting that can't be read now might say login is on: fail safe.
                on = true;
            }

            if (!startedOn && on)
            {
                _turnedOn = true;
                log?.WriteLine($"wolfden serve: {RestartNeeded.Message}");
                return true;
            }

            if (startedOn && !on && !_toldOff)
            {
                _toldOff = true;
                log?.WriteLine("wolfden serve: login was turned off, but this server keeps asking for it until it's restarted.");
            }

            return false;
        }
    }

    /// <summary>
    /// Answers 503 <c>restart_needed</c> for every request but
    /// <c>/api/health</c> once login was turned on after the server started.
    /// </summary>
    public Task Guard(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) && RestartNeededNow())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return context.Response.WriteAsJsonAsync(RestartNeeded);
        }

        return next(context);
    }
}
