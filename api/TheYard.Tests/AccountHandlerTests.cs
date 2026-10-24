
namespace TheYard.Tests;

// #region account-handler-tests
/// <summary>
/// The Accounts slice's handlers with hand-written fakes at every port and no host (ADR: The onion
/// cut into vertical slices, rule 7): the store of accounts, the allowances, the reset tokens and
/// the mail.
/// </summary>
public sealed class AccountHandlerTests
{
    private const string Store = "sql";

    [Fact]
    public async Task Registration_spends_the_allowance_only_on_an_account_made_and_a_store_with_no_accounts_is_unavailable()
    {
        var accounts = new FakeAccounts();
        var allowance = new FakeAllowance(1);
        var register = new RegisterHandler(accounts, allowance, () => 1_000);

        Assert.Equal(AccountAnswerKind.Unavailable, (await new RegisterHandler(null, allowance, () => 0).HandleAsync(new RegisterCommand("a@b.c", "correct horse"), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Unreadable, (await register.HandleAsync(new RegisterCommand("", "correct horse"), CancellationToken.None)).Kind);
        var refused = await register.HandleAsync(new RegisterCommand("a@b.c", "short"), CancellationToken.None);
        Assert.Equal((AccountAnswerKind.Refused, AccountProblem.PasswordTooShort), (refused.Kind, refused.Problem));
        Assert.Equal(1, allowance.Left);
        var made = await register.HandleAsync(new RegisterCommand("a@b.c", "correct horse"), CancellationToken.None);
        Assert.Equal(AccountAnswerKind.Done, made.Kind);
        Assert.Equal(1_000, made.Account!.CreatedAtMs);
        Assert.Equal(AccountAnswerKind.TooMany, (await register.HandleAsync(new RegisterCommand("d@e.f", "correct horse"), CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task Sign_in_answers_one_refusal_for_every_failure_and_sign_out_ends_the_sessions()
    {
        var accounts = new FakeAccounts();
        var account = (await accounts.CreateAsync("a@b.c", "correct horse", 1, CancellationToken.None)).Account!;
        var signIn = new SignInHandler(accounts);

        Assert.Equal(AccountAnswerKind.Refused, (await signIn.HandleAsync(new SignInCommand("nobody@b.c", "correct horse"), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Refused, (await signIn.HandleAsync(new SignInCommand("a@b.c", "wrong"), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Refused, (await signIn.HandleAsync(new SignInCommand("a@b.c", null), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Done, (await signIn.HandleAsync(new SignInCommand("a@b.c", "correct horse"), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Unavailable, (await new SignInHandler(null).HandleAsync(new SignInCommand("a@b.c", "correct horse"), CancellationToken.None)).Kind);

        Assert.Equal(account.Id, (await new WhoAmIHandler(accounts).HandleAsync(new WhoAmIQuery(account.Id), CancellationToken.None))!.Id);
        Assert.Null(await new WhoAmIHandler(accounts).HandleAsync(new WhoAmIQuery(null), CancellationToken.None));
        await new SignOutHandler(accounts).HandleAsync(new SignOutCommand(account.Id), CancellationToken.None);
        Assert.NotEqual(account.SecurityStamp, (await accounts.FindByIdAsync(account.Id, CancellationToken.None))!.SecurityStamp);
    }

    [Fact]
    public async Task A_reset_link_is_minted_mailed_used_once_and_refused_on_the_other_store()
    {
        var accounts = new FakeAccounts();
        var account = (await accounts.CreateAsync("a@b.c", "correct horse", 1, CancellationToken.None)).Account!;
        var tokens = new FakeTokens();
        var links = new MemoryResetLinks();
        var maker = new ResetLinkMaker(tokens, links);
        var mail = new FakeMail();

        var minted = await new OperatorResetLinkHandler(accounts, maker).HandleAsync(new OperatorResetLinkCommand("a@b.c", Store, "https://yard.example"), CancellationToken.None);
        Assert.Equal(AccountAnswerKind.Done, minted.Kind);
        Assert.Equal(AccountAnswerKind.Refused, (await new OperatorResetLinkHandler(accounts, maker).HandleAsync(new OperatorResetLinkCommand("nobody@b.c", Store, "https://yard.example"), CancellationToken.None)).Kind);

        var forgot = new ForgotHandler(accounts, mail, new FakeSpacing(), maker);
        Assert.Equal(ForgotAnswerKind.NoAddress, (await forgot.HandleAsync(new ForgotCommand(" ", Store, "https://yard.example"), CancellationToken.None)).Kind);
        Assert.Equal(ForgotAnswerKind.Done, (await forgot.HandleAsync(new ForgotCommand("a@b.c", Store, "https://yard.example"), CancellationToken.None)).Kind);
        Assert.Equal(ForgotAnswerKind.Done, (await forgot.HandleAsync(new ForgotCommand("nobody@b.c", Store, "https://yard.example"), CancellationToken.None)).Kind);
        Assert.Single(mail.Sent);
        Assert.Equal(ForgotAnswerKind.NoMail, (await new ForgotHandler(accounts, new FakeMail(configured: false), new FakeSpacing(), maker).HandleAsync(new ForgotCommand("a@b.c", Store, "x"), CancellationToken.None)).Kind);

        string id = minted.Url!.Split("reset=")[1];
        var reset = new ResetPasswordHandler(accounts, tokens, links);
        Assert.Equal(ResetAnswerKind.LinkRefused, (await reset.HandleAsync(new ResetPasswordCommand(id, "another horse", "cosmos"), CancellationToken.None)).Kind);
        Assert.Equal(ResetAnswerKind.PasswordRefused, (await reset.HandleAsync(new ResetPasswordCommand(id, "short", Store), CancellationToken.None)).Kind);
        Assert.Equal(ResetAnswerKind.Changed, (await reset.HandleAsync(new ResetPasswordCommand(id, "another horse", Store), CancellationToken.None)).Kind);
        Assert.Equal(ResetAnswerKind.LinkRefused, (await reset.HandleAsync(new ResetPasswordCommand(id, "a third horse", Store), CancellationToken.None)).Kind);
        Assert.Equal(AccountAnswerKind.Done, (await new SignInHandler(accounts).HandleAsync(new SignInCommand("a@b.c", "another horse"), CancellationToken.None)).Kind);
    }

    /// <summary>Accounts in a list: eight characters or more, one per address, a stamp that moves.</summary>
    private sealed class FakeAccounts : IAccountStore
    {
        private readonly List<(Account Account, string Password)> _rows = [];

        public Task<Account?> FindByIdAsync(string id, CancellationToken cancellation) =>
            Task.FromResult<Account?>(_rows.Find(row => row.Account.Id == id).Account);

        public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellation) =>
            Task.FromResult<Account?>(_rows.Find(row => row.Account.Email == email).Account);

        public Task<AccountCreated> CreateAsync(string email, string password, long createdAtMs, CancellationToken cancellation)
        {
            if (password.Length < 8)
            {
                return Task.FromResult(new AccountCreated(null, AccountProblem.PasswordTooShort));
            }
            if (_rows.Exists(row => row.Account.Email == email))
            {
                return Task.FromResult(new AccountCreated(null, AccountProblem.DuplicateEmail));
            }
            var account = new Account(Guid.NewGuid().ToString("N"), email, createdAtMs, Guid.NewGuid().ToString("N"), "hash:" + password);
            _rows.Add((account, password));
            return Task.FromResult(new AccountCreated(account, AccountProblem.None));
        }

        public Task<bool> CheckPasswordAsync(Account account, string password, CancellationToken cancellation) =>
            Task.FromResult(_rows.Exists(row => row.Account.Id == account.Id && row.Password == password));

        public Task EndSessionsAsync(Account account, CancellationToken cancellation)
        {
            Replace(account.Id, current => current.Account with { SecurityStamp = Guid.NewGuid().ToString("N") }, null);
            return Task.CompletedTask;
        }

        public Task<AccountProblem> CheckNewPasswordAsync(Account account, string password, CancellationToken cancellation) =>
            Task.FromResult(password.Length < 8 ? AccountProblem.PasswordTooShort : AccountProblem.None);

        public Task<AccountCreated> SetPasswordAsync(Account account, string password, CancellationToken cancellation) =>
            Task.FromResult(new AccountCreated(Replace(account.Id, current => current.Account with { PasswordHash = "hash:" + password }, password), AccountProblem.None));

        private Account Replace(string id, Func<(Account Account, string Password), Account> change, string? password)
        {
            int index = _rows.FindIndex(row => row.Account.Id == id);
            var changed = change(_rows[index]);
            _rows[index] = (changed, password ?? _rows[index].Password);
            return changed;
        }
    }

    private sealed class FakeAllowance(int left) : IAllowance
    {
        public int Left { get; private set; } = left;

        public bool TryTake()
        {
            if (Left == 0)
            {
                return false;
            }
            Left--;
            return true;
        }

        public void GiveBack() => Left++;
    }

    private sealed class FakeSpacing : IPerAddressAllowance
    {
        public bool TryTake(string address) => true;
    }

    /// <summary>Tokens that are their claims in plain text, which is enough to hold the order of the checks.</summary>
    private sealed class FakeTokens : IResetTokens
    {
        public TimeSpan Lifetime => TimeSpan.FromHours(1);

        public string Issue(Account account, string store) => $"{account.Id}|{store}|{Fingerprint(account.PasswordHash)}";

        public Task<ResetClaims?> ReadAsync(string? token) =>
            Task.FromResult(token?.Split('|') is [var id, var store, var print] ? new ResetClaims(id, store, print) : null);

        public string Fingerprint(string? passwordHash) => passwordHash ?? "";
    }

    private sealed class FakeMail(bool configured = true) : IEmailSender
    {
        public List<string> Sent { get; } = [];

        public bool Configured => configured;

        public string Reason => configured ? "faked" : "no sender";

        public Task<bool> SendAsync(string to, string subject, string text, CancellationToken cancellation)
        {
            Sent.Add(to);
            return Task.FromResult(true);
        }
    }
}
// #endregion account-handler-tests
