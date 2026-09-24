using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.GitHub.Client.Http.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Utils;

public sealed class AdaptiveCardSchemaUtil(IGitHubHttpClient gitHubHttpClient, ILogger<AdaptiveCardSchemaUtil> logger)
    : IAdaptiveCardSchemaUtil
{
    private const string _repository = "repos/microsoft/AdaptiveCards";

    public async ValueTask<string> GetLatest(CancellationToken cancellationToken = default)
    {
        HttpClient client = await gitHubHttpClient.Get(cancellationToken);
        // Resolve HEAD first so the version listing and file contents always come from the same revision.
        using JsonDocument commits = JsonDocument.Parse(await client.GetStringAsync($"{_repository}/commits?per_page=1", cancellationToken));
        if (commits.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException("The AdaptiveCards repository has no commits.");
        string commit = commits.RootElement[0].GetProperty("sha").GetString()
            ?? throw new InvalidDataException("GitHub did not return a commit SHA.");
        string reference = Uri.EscapeDataString(commit);

        using JsonDocument folders = JsonDocument.Parse(await client.GetStringAsync($"{_repository}/contents/schemas?ref={reference}", cancellationToken));
        Version? latest = null;
        string? latestFolder = null;
        foreach (JsonElement entry in folders.RootElement.EnumerateArray())
        {
            string? name = entry.GetProperty("name").GetString();
            if (entry.GetProperty("type").GetString() != "dir" || !Version.TryParse(name, out Version? version))
                continue;
            if (latest is null || version > latest)
            {
                latest = version;
                latestFolder = name;
            }
        }
        if (latestFolder is null)
            throw new InvalidDataException("No versioned Adaptive Cards schema directories were found.");

        string path = $"schemas/{latestFolder}/adaptive-card.json";
        using JsonDocument file = JsonDocument.Parse(await client.GetStringAsync(
            $"{_repository}/contents/schemas/{Uri.EscapeDataString(latestFolder)}/adaptive-card.json?ref={reference}", cancellationToken));
        if (file.RootElement.GetProperty("encoding").GetString() != "base64")
            throw new InvalidDataException($"GitHub returned an unsupported encoding for {path}.");
        string content = file.RootElement.GetProperty("content").GetString()
            ?? throw new InvalidDataException($"GitHub returned no content for {path}.");
        string schema = Encoding.UTF8.GetString(Convert.FromBase64String(content));
        logger.LogInformation("Using Adaptive Cards schema {Version} from {Path} at commit {Commit}", latest, path, commit);
        return schema;
    }
}
