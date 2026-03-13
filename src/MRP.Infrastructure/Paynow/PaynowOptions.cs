namespace MRP.Infrastructure.Paynow;

public class PaynowOptions
{
    public string DefaultResultUrl { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 900;
    public int MaxConcurrentPolls { get; set; } = 10;
    public int MaxRetryAttempts { get; set; } = 3;
}
