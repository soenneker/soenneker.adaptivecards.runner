using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.AdaptiveCards.Runner.Utils.Abstract;

/// <summary>Generates Adaptive Cards DTOs with Soenneker.JsonSchema.ToCSharp.</summary>
public interface IFileOperationsUtil
{
    /// <summary>Fetches the latest upstream schema and writes generated sources to the configured output directory.</summary>
    ValueTask Process(CancellationToken cancellationToken = default);
}
