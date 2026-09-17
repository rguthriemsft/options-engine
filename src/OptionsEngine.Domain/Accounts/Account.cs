namespace OptionsEngine.Domain.Accounts;

/// <summary>Represents a brokerage account that owns one or more holdings.</summary>
public sealed class Account
{
    public Guid AccountId { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public Broker Broker { get; private set; }
    public AccountType AccountType { get; private set; }
    public bool IsTaxDeferred { get; private set; }
    public bool IsEnabled { get; private set; }
    public ICollection<Holding> Holdings { get; } = new List<Holding>();

    private Account()
    {
    }

    public Account(string name, Broker broker, AccountType accountType, bool isTaxDeferred, bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Broker = broker;
        AccountType = accountType;
        IsTaxDeferred = isTaxDeferred;
        IsEnabled = isEnabled;
    }
}
