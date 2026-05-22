using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Application;
using RateLimiter.Domain;

namespace RateLimiter.Infrastructure.Configuration;

public sealed class ConfigurationPoller : IHostedService, IDisposable
{
    private readonly IConfigurationStore _configurationStore;
    private readonly RateLimitRuleProvider _ruleProvider;
    private readonly ILogger<ConfigurationPoller> _logger;
    private readonly TimeSpan _pollInterval;

    private IReadOnlyList<RateLimitRule> _currentRules = Array.Empty<RateLimitRule>();
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    public ConfigurationPoller(
        IConfigurationStore configurationStore,
        RateLimitRuleProvider ruleProvider,
        IOptions<ConfigurationPollerOptions> options,
        ILogger<ConfigurationPoller> logger)
    {
        ArgumentNullException.ThrowIfNull(configurationStore);
        ArgumentNullException.ThrowIfNull(ruleProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _configurationStore = configurationStore;
        _ruleProvider = ruleProvider;
        _logger = logger;
        _pollInterval = options.Value.PollInterval;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConfigurationPoller starting. Poll interval: {PollInterval}", _pollInterval);

        var rules = await _configurationStore.LoadRulesAsync(cancellationToken);
        _currentRules = rules;
        _ruleProvider.UpdateRules(rules);

        _logger.LogInformation("Initial configuration loaded with {RuleCount} rule(s)", rules.Count);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = PollLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConfigurationPoller stopping");

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_pollingTask is not null)
        {
            await Task.WhenAny(_pollingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await PollOnceAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown, no action needed
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            var newRules = await _configurationStore.LoadRulesAsync(ct);

            if (!RulesAreEqual(_currentRules, newRules))
            {
                _logger.LogInformation(
                    "Configuration change detected. Previous rules: {PreviousRules}, New rules: {NewRules}",
                    FormatRules(_currentRules),
                    FormatRules(newRules));

                _currentRules = newRules;
                _ruleProvider.UpdateRules(newRules);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load configuration from store. Retaining last-known-good rules ({RuleCount} rule(s))",
                _currentRules.Count);
        }
    }

    private static bool RulesAreEqual(IReadOnlyList<RateLimitRule> current, IReadOnlyList<RateLimitRule> incoming)
    {
        if (current.Count != incoming.Count)
            return false;

        for (int i = 0; i < current.Count; i++)
        {
            if (current[i] != incoming[i])
                return false;
        }

        return true;
    }

    private static string FormatRules(IReadOnlyList<RateLimitRule> rules)
    {
        return string.Join(", ", rules.Select(r =>
            $"[{r.RuleId}: capacity={r.BucketCapacity}, rate={r.RefillRatePerSecond}, appliesTo={r.AppliesTo}]"));
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
