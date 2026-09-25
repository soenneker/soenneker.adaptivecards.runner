using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.AdaptiveCards.Runner.Utils.Abstract;

/// <summary>Generates Adaptive Cards DTOs with Soenneker.JsonSchema.ToCSharp.</summary>
public interface IFileOperationsUtil
{
    /// <summary>Fetches the latest upstream schema and updates the Soenneker.AdaptiveCards.Dtos repository.</summary>
    ValueTask Process(CancellationToken cancellationToken = default);
}
