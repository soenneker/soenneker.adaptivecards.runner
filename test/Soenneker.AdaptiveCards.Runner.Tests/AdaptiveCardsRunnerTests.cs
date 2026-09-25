using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.AdaptiveCards.Runner.Utils;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.JsonSchema.ToCSharp.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Tests;

public sealed class AdaptiveCardsRunnerTests
{
    [Test]
    public async Task HostedServicesResolveAndGenerateDeterministically()
    {
        string output = Path.Combine(Path.GetTempPath(), "adaptivecards-" + Guid.NewGuid().ToString("N"));
        try
        {
            using IHost host = Program.CreateHostBuilder(["--output", output])
                .ConfigureServices(services => services.AddSingleton<IAdaptiveCardSchemaUtil>(new StubSchemaUtil("""{"$ref":"#/definitions/AdaptiveCard","definitions":{"AdaptiveCard":{"type":"object","properties":{"type":{"enum":["AdaptiveCard"]}}}}}""")))
                .Build();
            bool registered = false;
            foreach (IHostedService service in host.Services.GetServices<IHostedService>())
                registered |= service is ConsoleHostedService;
            if (!registered) throw new Exception("Runner hosted service is not registered.");
            IFileOperationsUtil operations = host.Services.GetRequiredService<IFileOperationsUtil>();
            await operations.Process();
            var generated = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories))
                generated.Add(file, await File.ReadAllTextAsync(file));
            if (!File.Exists(Path.Combine(output, "Models", "AdaptiveCard.cs")) || !File.Exists(Path.Combine(output, "SchemaJsonContext.cs")))
                throw new Exception("Soenneker.JsonSchema.ToCSharp output is missing.");
            await operations.Process();
            foreach ((string file, string content) in generated)
                if (await File.ReadAllTextAsync(file) != content) throw new Exception("Generation is not deterministic.");
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Test]
    public async Task InvalidSchemaDoesNotOverwriteOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adaptivecards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        
        string output = Path.Combine(directory, "Models.cs");
        try
        {
            
            await File.WriteAllTextAsync(output, "keep");
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["output"] = directory }).Build();
            using IHost host = Program.CreateHostBuilder([]).Build();
            var operations = new FileOperationsUtil(host.Services.GetRequiredService<IJsonSchemaToCSharp>(), new StubSchemaUtil("{}"), configuration, NullLogger<FileOperationsUtil>.Instance);
            bool rejected = false;
            try { await operations.Process(); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected || await File.ReadAllTextAsync(output) != "keep")
                throw new Exception("Invalid input changed the output or returned success.");
        }
        finally
        {

            File.Delete(output);
            Directory.Delete(directory);
        }
    }
    private sealed class StubSchemaUtil(string json) : IAdaptiveCardSchemaUtil
    {
        public ValueTask<string> GetLatest(CancellationToken cancellationToken = default) => ValueTask.FromResult(json);
    }}


