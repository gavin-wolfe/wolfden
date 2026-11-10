namespace Wolfden.Accounts;

/// <summary>
/// The installation or its accounts can't be used as asked: a folder in a
/// den, a settings file that doesn't read, or a change the accounts
/// database refused. The message is a sentence a command can print.
/// </summary>
public sealed class AccountsException : Exception
{
    public AccountsException()
    {
    }

    public AccountsException(string message)
        : base(message)
    {
    }

    public AccountsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
