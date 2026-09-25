using System;

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.JsonSchema.ToCSharp;
using Soenneker.JsonSchema.ToCSharp.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Utils;

public sealed class FileOperationsUtil(
    IJsonSchemaToCSharp generator,
    IAdaptiveCardSchemaUtil schemaUtil,
    IDtosRepositoryUtil dtosRepositoryUtil,
    ILogger<FileOperationsUtil> logger) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
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
            Namespace = Constants.Library,
            RootTypeName = "AdaptiveCard",
            GenerateAllDefinitions = true,
            FailOnUntypedSchemas = false
        }, cancellationToken);

        foreach (string diagnostic in result.Diagnostics)
            logger.LogWarning("Schema generation: {Diagnostic}", diagnostic);

        await dtosRepositoryUtil.Update(result, cancellationToken);
    }
}
