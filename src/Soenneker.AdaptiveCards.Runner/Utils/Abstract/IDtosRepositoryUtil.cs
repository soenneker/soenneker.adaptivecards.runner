using System.Threading;
using System.Threading.Tasks;
using Soenneker.JsonSchema.ToCSharp;

namespace Soenneker.AdaptiveCards.Runner.Utils.Abstract;

/// <summary>Updates the Soenneker.AdaptiveCards.Dtos repository with generated schema models.</summary>
public interface IDtosRepositoryUtil
{
    /// <summary>Reconciles generated files and builds the DTO library. A temporary clone is committed and pushed;
    /// a checkout supplied through Dtos:Directory is updated locally without committing unrelated work.</summary>
    ValueTask Update(JsonSchemaToCSharpResult result, CancellationToken cancellationToken = default);
}
