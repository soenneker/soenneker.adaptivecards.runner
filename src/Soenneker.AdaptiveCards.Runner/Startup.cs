using Microsoft.Extensions.DependencyInjection;
using Soenneker.GitHub.Client.Http.Registrars;
using Soenneker.JsonSchema.ToCSharp.Registrars;
using Soenneker.AdaptiveCards.Runner.Utils;
using Soenneker.AdaptiveCards.Runner.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;
using Soenneker.Utils.File.Download.Registrars;

namespace Soenneker.AdaptiveCards.Runner;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.SetupIoC();
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddSingleton<IAdaptiveCardSchemaUtil, AdaptiveCardSchemaUtil>()
                .AddGitHubHttpClientAsSingleton()
                .AddJsonSchemaToCSharpAsSingleton()
                .AddFileDownloadUtilAsSingleton()
                .AddRunnersManagerAsSingleton();

        return services;
    }
}

