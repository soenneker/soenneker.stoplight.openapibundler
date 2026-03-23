using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Stoplight.OpenApiBundler.Abstract;

/// <summary>
/// A utility library to download and bundle OpenApi specs from Stoplight
/// </summary>
public interface IStoplightOpenApiBundler
{
    /// <summary>
    /// Bundles the root spec from Stoplight into a single YAML file.
    /// </summary>
    /// <param name="projectId">Stoplight project id, for example "calendly".</param>
    /// <param name="rootNodePath">Node path inside Stoplight, for example "reference/calendly-api/openapi.yaml".</param>
    /// <param name="outputFilePath">Local file path where the bundled YAML will be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full local path to the bundled file.</returns>
    ValueTask<string> Bundle(string projectId, string rootNodePath, string outputFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bundles the root spec from a Stoplight node URL into a single YAML file.
    /// </summary>
    /// <param name="stoplightNodeUrl">A Stoplight node URL, for example "https://stoplight.io/api/v1/projects/calendly/api-docs/nodes/reference/calendly-api/openapi.yaml".</param>
    /// <param name="outputFilePath">Local file path where the bundled YAML will be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full local path to the bundled file.</returns>
    ValueTask<string> Bundle(string stoplightNodeUrl, string outputFilePath, CancellationToken cancellationToken = default);
}
