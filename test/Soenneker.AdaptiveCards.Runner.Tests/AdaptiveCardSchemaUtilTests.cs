using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.AdaptiveCards.Runner.Utils;
using Soenneker.GitHub.Client.Http.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Tests;

public sealed class AdaptiveCardSchemaUtilTests
{
    [Test]
    public async Task SelectsHighestVersionAndPinsRequestsToLatestCommit()
    {
        const string folders = """[{"name":"1.9.0","type":"dir"},{"name":"src","type":"dir"},{"name":"9.0.0","type":"file"},{"name":"1.10.0","type":"dir"},{"name":"1.2.0","type":"dir"}]""";
        using var provider = new FakeGitHubClient(folders);
        var util = new AdaptiveCardSchemaUtil(provider, NullLogger<AdaptiveCardSchemaUtil>.Instance);
        string schema = await util.GetLatest();
        if (schema != "schema-content" || provider.Handler.Paths.Count != 3
            || provider.Handler.Paths[1] != "/repos/microsoft/AdaptiveCards/contents/schemas?ref=commit1"
            || provider.Handler.Paths[2] != "/repos/microsoft/AdaptiveCards/contents/schemas/1.10.0/adaptive-card.json?ref=commit1")
            throw new Exception("Did not select the latest numeric directory at a consistent commit.");
        await util.GetLatest();
        if (provider.Handler.Paths.Count != 6 || !provider.Handler.Paths[5].EndsWith("?ref=commit2", StringComparison.Ordinal))
            throw new Exception("A subsequent run did not fetch the latest commit.");
    }

    [Test]
    public async Task MissingVersionDirectoriesFail()
    {
        using var provider = new FakeGitHubClient("""[{"name":"src","type":"dir"}]""");
        var util = new AdaptiveCardSchemaUtil(provider, NullLogger<AdaptiveCardSchemaUtil>.Instance);
        try { await util.GetLatest(); }
        catch (InvalidDataException) { return; }
        throw new Exception("A missing versioned schema must fail.");
    }

    [Test]
    public async Task MissingLatestSchemaDoesNotFallBackToAnOlderVersion()
    {
        using var provider = new FakeGitHubClient("""[{"name":"1.5.0","type":"dir"},{"name":"1.6.0","type":"dir"}]""", HttpStatusCode.NotFound);
        var util = new AdaptiveCardSchemaUtil(provider, NullLogger<AdaptiveCardSchemaUtil>.Instance);
        try { await util.GetLatest(); }
        catch (HttpRequestException)
        {
            if (provider.Handler.Paths.Count == 3) return;
        }
        throw new Exception("A missing latest schema must fail without a stale fallback.");
    }

    private sealed class FakeGitHubClient : IGitHubHttpClient
    {
        public FakeHandler Handler { get; }
        private readonly HttpClient _client;
        public FakeGitHubClient(string folders, HttpStatusCode fileStatus = HttpStatusCode.OK)
        {
            Handler = new FakeHandler(folders, fileStatus);
            _client = new HttpClient(Handler) { BaseAddress = new Uri("https://api.github.com/") };
        }
        public ValueTask<HttpClient> Get(CancellationToken cancellationToken = default) => ValueTask.FromResult(_client);
        public ValueTask<HttpClient> GetForUpload(CancellationToken cancellationToken = default) => Get(cancellationToken);
        public void Dispose() => _client.Dispose();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeHandler(string folders, HttpStatusCode fileStatus) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        private int _commit;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            string content;
            HttpStatusCode status = HttpStatusCode.OK;
            if (path.Contains("/commits?", StringComparison.Ordinal))
                content = JsonSerializer.Serialize(new[] { new { sha = "commit" + ++_commit } });
            else if (path.Contains("/contents/schemas?", StringComparison.Ordinal)) content = folders;
            else
            {
                status = fileStatus;
                content = JsonSerializer.Serialize(new { encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes("schema-content")) });
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }
}
