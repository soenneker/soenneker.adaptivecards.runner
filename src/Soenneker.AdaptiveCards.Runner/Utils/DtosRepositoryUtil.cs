using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.Git.Util.Abstract;
using Soenneker.JsonSchema.ToCSharp;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Dotnet.Abstract;

namespace Soenneker.AdaptiveCards.Runner.Utils;

public sealed class DtosRepositoryUtil(IGitUtil gitUtil, IDotnetUtil dotnetUtil, IDirectoryUtil directoryUtil,
    IConfiguration configuration, ILogger<DtosRepositoryUtil> logger) : IDtosRepositoryUtil
{
    public async ValueTask Update(JsonSchemaToCSharpResult result, CancellationToken cancellationToken = default)
    {
        string? configuredDirectory = configuration["Dtos:Directory"];
        bool temporary = string.IsNullOrWhiteSpace(configuredDirectory);
        string? repository = temporary ? null : Path.GetFullPath(configuredDirectory!);
        string? token = configuration["GH:Token"];
        if (temporary && string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GH:Token is required to update the DTO repository.");

        try
        {
            if (temporary)
                repository = await gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.TargetRepository}.git", token,
                    cancellationToken: cancellationToken);

            string projectDirectory = Path.Combine(repository!, "src", Constants.Library);
            string project = Path.Combine(projectDirectory, Constants.Library + ".csproj");
            if (!File.Exists(project))
                throw new FileNotFoundException("The DTO checkout must contain the Soenneker.AdaptiveCards.Dtos project.", project);

            string generatedDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "Generated"));
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string relative, string source) in result.Files)
            {
                string path = Path.GetFullPath(Path.Combine(generatedDirectory, relative));
                if (!path.StartsWith(generatedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Generated path escaped the DTO Generated directory.");
                if (File.Exists(path) && !IsGenerated(await File.ReadAllTextAsync(path, cancellationToken)))
                    throw new IOException("Refusing to overwrite a handwritten file: " + path);
                files.Add(path, source);
            }
            if (files.Count == 0)
                throw new InvalidOperationException("The schema generator returned no DTO files.");

            bool changed = false;
            foreach ((string path, string source) in files)
            {
                if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == source)
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, source, new UTF8Encoding(false), cancellationToken);
                changed = true;
            }
            foreach (string path in Directory.EnumerateFiles(generatedDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (files.ContainsKey(path) || !IsGenerated(await File.ReadAllTextAsync(path, cancellationToken)))
                    continue;
                File.Delete(path);
                changed = true;
            }

            if (!changed)
            {
                logger.LogInformation("{Library} is already up to date", Constants.Library);
                return;
            }
            await dotnetUtil.Restore(project, verbosity: "minimal", cancellationToken: cancellationToken);
            if (!await dotnetUtil.Build(project, configuration: "Release", restore: false, verbosity: "minimal", cancellationToken: cancellationToken))
                throw new InvalidOperationException("The generated DTO library failed to build; changes were not committed or pushed.");

            if (temporary)
            {
                if (!await gitUtil.HasWorkingTreeChanges(repository!, cancellationToken))
                    return;
                string name = configuration["Git:Name"] ?? throw new InvalidOperationException("GIT__NAME is not set.");
                string email = configuration["Git:Email"] ?? throw new InvalidOperationException("GIT__EMAIL is not set.");
                await gitUtil.CommitAndPush(repository!, "Update Adaptive Cards DTOs from the latest schema", token!, name, email, cancellationToken);
                logger.LogInformation("Updated and pushed {Library}", Constants.Library);
            }
            else
                logger.LogInformation("Updated {Count} generated DTO files in {Repository}", files.Count, repository);
        }
        finally
        {
            if (temporary && Directory.Exists(repository))
            {
                string fullPath = Path.GetFullPath(repository);
                if (!fullPath.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Refusing to clean up a path outside the runner's temporary checkout.");
                await directoryUtil.Delete(fullPath, CancellationToken.None);
            }
        }
    }

    private static bool IsGenerated(string source) => source.StartsWith("// <auto-generated", StringComparison.Ordinal);
}
