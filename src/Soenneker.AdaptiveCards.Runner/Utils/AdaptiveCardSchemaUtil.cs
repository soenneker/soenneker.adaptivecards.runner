using System;
using System.IO;
using System.Net;
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
    public string? Version { get; private set; }

    private const string _repository = "repos/microsoft/AdaptiveCards";

    public async ValueTask<string> GetLatest(CancellationToken cancellationToken = default)
    {
        HttpClient client = await gitHubHttpClient.Get(cancellationToken);
        // Resolve HEAD first so the version listing and file contents always come from the same revision.
        using JsonDocument commits = JsonDocument.Parse(await GetString(client, $"{_repository}/commits?per_page=1", cancellationToken));
        if (commits.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException("The AdaptiveCards repository has no commits.");
        string commit = commits.RootElement[0].GetProperty("sha").GetString()
            ?? throw new InvalidDataException("GitHub did not return a commit SHA.");
        string reference = Uri.EscapeDataString(commit);

        using JsonDocument folders = JsonDocument.Parse(await GetString(client, $"{_repository}/contents/schemas?ref={reference}", cancellationToken));
        System.Version? latest = null;
        string? latestFolder = null;
        foreach (JsonElement entry in folders.RootElement.EnumerateArray())
        {
            string? name = entry.GetProperty("name").GetString();
            if (entry.GetProperty("type").GetString() != "dir" || !System.Version.TryParse(name, out System.Version? version))
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
        using JsonDocument file = JsonDocument.Parse(await GetString(client,
            $"{_repository}/contents/schemas/{Uri.EscapeDataString(latestFolder)}/adaptive-card.json?ref={reference}", cancellationToken));
        if (file.RootElement.GetProperty("encoding").GetString() != "base64")
            throw new InvalidDataException($"GitHub returned an unsupported encoding for {path}.");
        string content = file.RootElement.GetProperty("content").GetString()
            ?? throw new InvalidDataException($"GitHub returned no content for {path}.");
        string schema = Encoding.UTF8.GetString(Convert.FromBase64String(content));
        logger.LogInformation("Using Adaptive Cards schema {Version} from {Path} at commit {Commit}", latest, path, commit);
        Version = latest!.ToString();
        return schema;
    }

    private async Task<string> GetString(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("GitHub rejected authenticated schema access ({StatusCode}); retrying the public repository anonymously", response.StatusCode);
            using var anonymousRequest = new HttpRequestMessage(HttpMethod.Get, path);
            // An explicit empty header prevents HttpClient from copying its default bearer token.
            anonymousRequest.Headers.TryAddWithoutValidation("Authorization", string.Empty);
            using HttpResponseMessage anonymousResponse = await client.SendAsync(anonymousRequest, cancellationToken);
            return await ReadResponse(anonymousResponse, path, cancellationToken);
        }

        return await ReadResponse(response, path, cancellationToken);
    }

    private static async Task<string> ReadResponse(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub schema request '{path}' failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {content}",
                null, response.StatusCode);
        return content;
    }

}
