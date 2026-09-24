using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.AdaptiveCards.Runner.Utils.Abstract;

/// <summary>Retrieves versioned schemas from Microsoft's AdaptiveCards repository.</summary>
public interface IAdaptiveCardSchemaUtil
{
    /// <summary>Fetches the highest numeric version under schemas at the latest default-branch commit.</summary>
    /// <remarks>Fails if there are no version folders or the newest version has no readable schema.</remarks>
    ValueTask<string> GetLatest(CancellationToken cancellationToken = default);
}
