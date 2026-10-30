using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Features.Accounts;

namespace TheYard.Tests;

/// <summary>
/// The accounts port over Identity, held against a real UserManager over a real engine rather than a
/// fake manager, because everything this adapter is for lives in the manager: the password hashing,
/// the normalised lookup and the lockout counter. A fake would assert that the adapter calls the
/// methods it calls, which is not a fact about the site.
///
/// The lockout behaviour is the part worth holding. Identity's <c>CheckPasswordAsync</c> does not
/// touch the failed count by itself, so an adapter that only forwarded it would let a guesser try
/// forever; this one asks whether the account is locked first, counts a wrong password and clears the
/// count on a right one, which is why five wrong guesses spread over a week never lock an owner out.
/// </summary>
public sealed class IdentityAccountStoreTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), $"theyard-accounts-{Guid.NewGuid():N}.db");

    private readonly ServiceProvider _provider;

    public IdentityAccountStoreTests()
    {
        var connection = new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False");
        using (var db = new YardDbContext(connection.Options()))
        {
            db.Database.Migrate();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<YardDbContext>(options => options.UseSqlite($"Data Source={_file};Pooling=False"));
        services.AddIdentityCore<YardUser>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireDigit = false;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.AllowedForNewUsers = true;
        }).AddEntityFrameworkStores<YardDbContext>();
        _provider = services.BuildServiceProvider();
    }

    /// <summary>A store over a scope of its own, the way the host builds one per request (ADR-107, rule 7).</summary>
    private async Task<T> InAStore<T>(Func<IdentityAccountStore, UserManager<YardUser>, Task<T>> ask)
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<YardUser>>();
        return await ask(new IdentityAccountStore(users), users);
    }

    private static string AnAddress() => $"buyer-{Guid.NewGuid():N}@example.com";

    // #region creating
    /// <summary>
    /// A new account comes back with the address it was made under, the moment it was made and the
    /// security stamp Identity gave it, and it is found again by id and by address. The address is
    /// looked up normalised, so the capitals a person types are not a second account.
    /// </summary>
    [Fact]
    public async Task An_account_is_made_under_its_address_and_found_again_by_id_and_by_address()
    {
        string email = AnAddress();

        var (created, byId, byAddress, byCapitals) = await InAStore(async (store, _) =>
        {
            var made = await store.CreateAsync(email, "correct horse", 1_700_000_000_000, CancellationToken.None);
            return (
                made,
                await store.FindByIdAsync(made.Account!.Id, CancellationToken.None),
                await store.FindByEmailAsync(email, CancellationToken.None),
                await store.FindByEmailAsync(email.ToUpperInvariant(), CancellationToken.None));
        });

        Assert.Equal(AccountProblem.None, created.Problem);
        Assert.NotNull(created.Account);
        Assert.Equal((email, 1_700_000_000_000L), (created.Account.Email, created.Account.CreatedAtMs));
        Assert.NotEmpty(created.Account.SecurityStamp);
        Assert.Equal(created.Account.Id, byId!.Id);
        Assert.Equal(created.Account.Id, byAddress!.Id);
        Assert.Equal(created.Account.Id, byCapitals!.Id);
        Assert.Null(await InAStore(async (store, _) => await store.FindByEmailAsync(AnAddress(), CancellationToken.None)));
    }

    /// <summary>
    /// Identity refuses in a dozen ways with a code each, and the site shows a form with one message.
    /// So the adapter answers one problem per way of being refused, and the codes stay in the log: a
    /// password too short is one, a second account on one address is another, and anything else is the
    /// address being wrong, which is the only one a form can ask the person to fix.
    /// </summary>
    [Fact]
    public async Task Every_way_identity_refuses_becomes_one_problem_a_form_can_show()
    {
        string email = AnAddress();

        var (first, duplicate, tooShort, notAnAddress) = await InAStore(async (store, _) => (
            await store.CreateAsync(email, "correct horse", 1, CancellationToken.None),
            await store.CreateAsync(email, "correct horse", 2, CancellationToken.None),
            await store.CreateAsync(AnAddress(), "short", 3, CancellationToken.None),
            await store.CreateAsync("not an address", "correct horse", 4, CancellationToken.None)));

        Assert.Equal(AccountProblem.None, first.Problem);
        Assert.Equal(AccountProblem.DuplicateEmail, duplicate.Problem);
        Assert.Equal(AccountProblem.PasswordTooShort, tooShort.Problem);
        Assert.Equal(AccountProblem.BadEmail, notAnAddress.Problem);
        // A refused creation hands back no account at all, so a caller cannot read half of one.
        Assert.All([duplicate, tooShort, notAnAddress], refused => Assert.Null(refused.Account));
    }
    // #endregion creating

    // #region lockout
    /// <summary>
    /// The lockout, which is the whole reason this adapter does not simply forward
    /// <c>CheckPasswordAsync</c>: a wrong password is counted, a right one clears the count, and once
    /// the count reaches the configured five the account is locked and the right password is refused
    /// too. Checking alone never moves the count, so an adapter that forwarded would never lock.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_is_counted_a_right_one_clears_the_count_and_five_wrong_ones_lock_the_account()
    {
        string email = AnAddress();

        var counts = await InAStore(async (store, users) =>
        {
            var made = await store.CreateAsync(email, "correct horse", 1, CancellationToken.None);
            var account = made.Account!;

            bool wrong = await store.CheckPasswordAsync(account, "wrong", CancellationToken.None);
            int afterOneWrong = (await users.FindByIdAsync(account.Id))!.AccessFailedCount;

            bool right = await store.CheckPasswordAsync(account, "correct horse", CancellationToken.None);
            int afterRight = (await users.FindByIdAsync(account.Id))!.AccessFailedCount;

            for (int i = 0; i < 5; i++)
            {
                await store.CheckPasswordAsync(account, "wrong", CancellationToken.None);
            }
            bool rightWhileLocked = await store.CheckPasswordAsync(account, "correct horse", CancellationToken.None);
            bool locked = await users.IsLockedOutAsync((await users.FindByIdAsync(account.Id))!);

            return (wrong, afterOneWrong, right, afterRight, rightWhileLocked, locked);
        });

        Assert.False(counts.wrong);
        Assert.Equal(1, counts.afterOneWrong);
        Assert.True(counts.right);
        Assert.Equal(0, counts.afterRight);
        Assert.True(counts.locked, "five wrong passwords in a row lock the account");
        Assert.False(counts.rightWhileLocked, "a locked account refuses the right password too");
    }

    /// <summary>An account that is not there cannot be checked, and nothing throws: the caller gets a no.</summary>
    [Fact]
    public async Task An_account_that_is_not_there_is_refused_rather_than_thrown_at()
    {
        var gone = new Account(Guid.NewGuid().ToString(), AnAddress(), 1, "stamp", null);

        Assert.False(await InAStore(async (store, _) => await store.CheckPasswordAsync(gone, "correct horse", CancellationToken.None)));
        Assert.Equal(AccountProblem.BadEmail, await InAStore(async (store, _) => await store.CheckNewPasswordAsync(gone, "correct horse", CancellationToken.None)));
        Assert.Equal(AccountProblem.BadEmail, (await InAStore(async (store, _) => await store.SetPasswordAsync(gone, "correct horse", CancellationToken.None))).Problem);
        // Ending the sessions of an account that is gone is a no-op, not an exception: a reset link
        // used twice arrives here the second time.
        await InAStore(async (store, _) =>
        {
            await store.EndSessionsAsync(gone, CancellationToken.None);
            return true;
        });
    }
    // #endregion lockout

    // #region passwords
    /// <summary>
    /// A new password is run past the store's own validators before it is set, so the form says what
    /// is wrong with it rather than the reset failing; then setting it takes the old one off, puts the
    /// new one on and clears the failed count, which is what makes a reset a way out of a lockout.
    /// </summary>
    [Fact]
    public async Task A_new_password_is_validated_first_and_setting_it_clears_the_failed_count()
    {
        string email = AnAddress();

        var (tooShort, allowed, set, oldOne, newOne, failedAfter) = await InAStore(async (store, users) =>
        {
            var account = (await store.CreateAsync(email, "correct horse", 1, CancellationToken.None)).Account!;
            await store.CheckPasswordAsync(account, "wrong", CancellationToken.None);

            var refused = await store.CheckNewPasswordAsync(account, "short", CancellationToken.None);
            var fine = await store.CheckNewPasswordAsync(account, "a longer one", CancellationToken.None);
            var changed = await store.SetPasswordAsync(account, "a longer one", CancellationToken.None);
            var after = (await users.FindByIdAsync(account.Id))!;

            return (
                refused,
                fine,
                changed,
                await store.CheckPasswordAsync(account, "correct horse", CancellationToken.None),
                await store.CheckPasswordAsync(account, "a longer one", CancellationToken.None),
                after.AccessFailedCount);
        });

        Assert.Equal(AccountProblem.PasswordTooShort, tooShort);
        Assert.Equal(AccountProblem.None, allowed);
        Assert.Equal(AccountProblem.None, set.Problem);
        Assert.False(oldOne, "the password that was replaced no longer opens the account");
        Assert.True(newOne);
        Assert.Equal(0, failedAfter);
    }

    /// <summary>
    /// Ending the sessions moves the security stamp, which is the one fact every live token is checked
    /// against: a signed-out account's old tokens stop being accepted because the stamp they carry is
    /// no longer the account's.
    /// </summary>
    [Fact]
    public async Task Ending_the_sessions_moves_the_security_stamp_every_live_token_is_checked_against()
    {
        string email = AnAddress();

        var (before, after) = await InAStore(async (store, _) =>
        {
            var account = (await store.CreateAsync(email, "correct horse", 1, CancellationToken.None)).Account!;
            await store.EndSessionsAsync(account, CancellationToken.None);
            return (account.SecurityStamp, (await store.FindByEmailAsync(email, CancellationToken.None))!.SecurityStamp);
        });

        Assert.NotEmpty(before);
        Assert.NotEqual(before, after);
    }
    // #endregion passwords

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _provider.Dispose();
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (IOException)
            {
                // A handle the engine has not let go of yet; the temp folder keeps it.
            }
        }
    }
}
