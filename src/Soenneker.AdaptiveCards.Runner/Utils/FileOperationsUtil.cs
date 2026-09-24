using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.JsonSchema.ToCSharp;
using Soenneker.JsonSchema.ToCSharp.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Utils;

public sealed class FileOperationsUtil(
    IJsonSchemaToCSharp generator,
    IAdaptiveCardSchemaUtil schemaUtil,
    IConfiguration configuration,
    ILogger<FileOperationsUtil> logger) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string output = configuration["output"] ??
                        throw new ArgumentException("Specify --output with the generated source directory.");
        string json = await schemaUtil.GetLatest(cancellationToken);

        JsonObject document = JsonNode.Parse(json)?.AsObject() ??
                              throw new ArgumentException("Expected an Adaptive Cards schema object.");
        if (document["definitions"]?["AdaptiveCard"] is null)
            throw new ArgumentException("The schema must define AdaptiveCard.");
        // The upstream document uses HTTPS for the draft-06 dialect; the generator expects its canonical HTTP identifier.
        if (document["$schema"]?.GetValue<string>() == "https://json-schema.org/draft-06/schema#")
            document["$schema"] = "http://json-schema.org/draft-06/schema#";

        JsonSchemaToCSharpResult result = generator.Generate(document.ToJsonString(), new JsonSchemaToCSharpOptions
        {
            Namespace = "Soenneker.AdaptiveCards.Dtos",
            RootTypeName = "AdaptiveCard",
            GenerateAllDefinitions = true,
            FailOnUntypedSchemas = false
        }, cancellationToken);

        foreach (string diagnostic in result.Diagnostics)
            logger.LogWarning("Schema generation: {Diagnostic}", diagnostic);

        string directory = Path.GetFullPath(output);
        foreach ((string relativePath, string source) in result.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated path escaped the output directory.");
            if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == source)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, source, new UTF8Encoding(false), cancellationToken);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        logger.LogInformation("Generated {Count} files in {Directory}; root type: {RootType}", result.Files.Count,
            directory, result.RootType);
    }
}
