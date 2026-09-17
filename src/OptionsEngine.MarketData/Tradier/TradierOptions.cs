namespace OptionsEngine.MarketData.Tradier;

/// <summary>Production-only Tradier configuration. AccessToken belongs in user secrets or environment configuration.</summary>
public sealed class TradierOptions
{
    public const string SectionName = "Tradier";
    public string BaseUrl { get; set; } = "https://api.tradier.com/v1/";
    public string? AccessToken { get; set; }
    public int TransientRetryCount { get; set; } = 1;
}
