using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RateLimiter.Domain;

namespace RateLimiter.Infrastructure.Configuration;

/// <summary>
/// Options for the <see cref="JsonFileConfigurationStore"/>.
/// </summary>
public sealed class JsonFileConfigurationStoreOptions
{
    /// <summary>
    /// Path to the JSON configuration file containing rate limit rules.
    /// </summary>
    public string FilePath { get; set; } = "ratelimit-rules.json";
}

/// <summary>
/// Loads rate limit rules from a JSON file on disk.
/// </summary>
public sealed class JsonFileConfigurationStore : IConfigurationStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonFileConfigurationStore(IOptions<JsonFileConfigurationStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _filePath = options.Value.FilePath;
    }

    public async Task<IReadOnlyList<RateLimitRule>> LoadRulesAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(_filePath);
        var document = await JsonSerializer.DeserializeAsync<RateLimitConfigDocument>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Failed to deserialize configuration from '{_filePath}'.");

        return document.RateLimitRules;
    }
}

/// <summary>
/// Represents the JSON document structure for rate limit configuration.
/// </summary>
internal sealed class RateLimitConfigDocument
{
    public List<RateLimitRule> RateLimitRules { get; set; } = [];
}
