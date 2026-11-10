using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wolfden.Accounts;

/// <summary>
/// <c>accounts.db</c>: Identity's user tables, without roles tables (an
/// account has exactly one role, in <see cref="WolfdenUser.Role"/>) and
/// without email or phone columns.
/// </summary>
/// <remarks>
/// <para>
/// Unlike a den's index, this database isn't rebuildable: it's the only
/// copy of the accounts. So its schema changes go through EF Core
/// migrations (the <c>Migrations</c> folder), applied when it's opened,
/// never through drop-and-recreate. To add one, from the repository root:
/// <c>dotnet ef migrations add &lt;Name&gt; --project src/Wolfden.Accounts</c>.
/// </para>
/// </remarks>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : IdentityUserContext<WolfdenUser, Guid>(options)
{
    /// <summary>The connection string for the database at <paramref name="path"/>. No pooling, so the file is let go when the command ends.</summary>
    public static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<WolfdenUser>(user =>
        {
            // Username and password only: no address is ever stored (#315 §4.3).
            user.Ignore(u => u.Email);
            user.Ignore(u => u.NormalizedEmail);
            user.Ignore(u => u.EmailConfirmed);
            user.Ignore(u => u.PhoneNumber);
            user.Ignore(u => u.PhoneNumberConfirmed);
            user.Property(u => u.Role).HasMaxLength(16).IsRequired();

            // Ids are version 7 Guids that AccountStore sets (maintainer
            // decision, 2026-11-10). EF mustn't make up a random one in
            // place of a missing id, and the database refuses an empty one.
            // SQLite keeps a Guid as TEXT, in upper case.
            user.Property(u => u.Id).ValueGeneratedNever();
            user.ToTable(t => t.HasCheckConstraint("CK_AspNetUsers_Id_NotEmpty", $"\"Id\" <> '{Guid.Empty}'"));
        });
    }

    /// <inheritdoc/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefuseEmptyIds();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefuseEmptyIds();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>A new account must have its id set (<see cref="Guid.CreateVersion7()"/>); an empty one is a bug, so it fails loudly.</summary>
    private void RefuseEmptyIds()
    {
        if (ChangeTracker.Entries<WolfdenUser>().Any(e => e.State == EntityState.Added && e.Entity.Id == Guid.Empty))
        {
            throw new AccountsException("an account can't be saved without an id.");
        }
    }
}

/// <summary>For <c>dotnet ef</c> only: a context over a throwaway file, so the tools can read the model.</summary>
internal sealed class AccountsDbContextDesignFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(AccountsDbContext.ConnectionString(Path.Combine(Path.GetTempPath(), "wolfden-accounts-design.db")))
            .Options);
}
