using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wolfden.Accounts;

/// <summary>
/// The password rules (#315 §7): 12 to 256 characters, counting each
/// Unicode code point once after NFC normalization, with no composition
/// rules (no "a digit and a symbol"), as NIST SP 800-63B rev. 4 §3.1.1.2
/// asks. It refuses two passwords anyone could guess from the account
/// itself: the username, and one character repeated.
/// </summary>
public sealed class PasswordRules : IPasswordValidator<WolfdenUser>
{
    /// <summary>The fewest characters a password may have.</summary>
    public const int MinimumLength = 12;

    /// <summary>The most characters a password may have.</summary>
    public const int MaximumLength = 256;

    /// <summary>The code for every refusal, as Identity reports it.</summary>
    public const string ErrorCode = "bad_password";

    /// <summary>What's wrong with <paramref name="password"/> for <paramref name="userName"/>, or null if nothing is.</summary>
    public static string? Problem(string? userName, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!TryNormalize(password, out var normalized))
        {
            return "a password can't hold a broken character (half of a character outside the basic set, such as an emoji).";
        }

        var length = normalized.EnumerateRunes().Count();
        if (length < MinimumLength)
        {
            return $"a password needs at least {MinimumLength} characters, and this one has {length}. A few words in a row make a long one that's easy to remember.";
        }

        if (length > MaximumLength)
        {
            return $"a password can have at most {MaximumLength} characters, and this one has {length}.";
        }

        if (userName is not null && string.Equals(normalized, TryNormalize(userName, out var name) ? name : userName, StringComparison.OrdinalIgnoreCase))
        {
            return "a password can't be the username.";
        }

        if (normalized.EnumerateRunes().Distinct().Count() == 1)
        {
            return "a password can't be one character repeated.";
        }

        return null;
    }

    /// <summary>
    /// The form a password is hashed and compared in: Unicode NFC, so the
    /// same characters typed on two keyboards match. False for text that
    /// isn't valid Unicode (a lone surrogate), which has no NFC form.
    /// </summary>
    public static bool TryNormalize(string password, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(password);
        try
        {
            normalized = password.IsNormalized(NormalizationForm.FormC) ? password : password.Normalize(NormalizationForm.FormC);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = password;
            return false;
        }
    }

    /// <summary>The NFC form of <paramref name="password"/>; throws <see cref="ArgumentException"/> for text that isn't valid Unicode.</summary>
    public static string Normalize(string password) =>
        TryNormalize(password, out var normalized)
            ? normalized
            : throw new ArgumentException("The password isn't valid Unicode.", nameof(password));

    /// <inheritdoc/>
    public Task<IdentityResult> ValidateAsync(UserManager<WolfdenUser> manager, WolfdenUser user, string? password) =>
        Task.FromResult(Problem(user?.UserName, password ?? string.Empty) is { } problem
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = problem })
            : IdentityResult.Success);
}

/// <summary>
/// Identity's password hasher, applied to the NFC form of the password
/// (NIST SP 800-63B rev. 4 §3.1.1.2), so a password typed with a composed
/// "é" matches one typed with "e" and a combining accent.
/// </summary>
public sealed class NormalizingPasswordHasher(IOptions<PasswordHasherOptions>? options = null) : PasswordHasher<WolfdenUser>(options)
{
    /// <inheritdoc/>
    public override string HashPassword(WolfdenUser user, string password) =>
        base.HashPassword(user, PasswordRules.Normalize(password));

    /// <inheritdoc/>
    /// <remarks>Text that isn't valid Unicode can't be a password the rules allowed, so it fails.</remarks>
    public override PasswordVerificationResult VerifyHashedPassword(WolfdenUser user, string hashedPassword, string providedPassword) =>
        PasswordRules.TryNormalize(providedPassword ?? string.Empty, out var normalized)
            ? base.VerifyHashedPassword(user, hashedPassword, normalized)
            : PasswordVerificationResult.Failed;
}

/// <summary>The username rules: 1 to 64 characters, letters, digits, '.', '-' and '_'.</summary>
public static class UserNameRules
{
    /// <summary>The most characters a username may have.</summary>
    public const int MaximumLength = 64;

    /// <summary>The characters a username may use. Identity compares usernames without regard to case.</summary>
    public const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_";

    /// <summary>What's wrong with <paramref name="userName"/>, or null if nothing is.</summary>
    public static string? Problem(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        if (userName.Length == 0)
        {
            return "a username can't be empty.";
        }

        if (userName.Length > MaximumLength)
        {
            return $"a username can have at most {MaximumLength} characters.";
        }

        if (userName.Any(c => !AllowedCharacters.Contains(c, StringComparison.Ordinal)))
        {
            return string.Create(CultureInfo.InvariantCulture, $"'{userName}' isn't a username: use letters, digits, '.', '-' and '_', with no spaces.");
        }

        return null;
    }
}
