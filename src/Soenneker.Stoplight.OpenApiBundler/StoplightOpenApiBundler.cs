using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Stoplight.OpenApiBundler.Abstract;
using Soenneker.Utils.HttpClientCache.Abstract;
using YamlDotNet.RepresentationModel;

namespace Soenneker.Stoplight.OpenApiBundler;

/// <inheritdoc cref="IStoplightOpenApiBundler"/>
public sealed class StoplightOpenApiBundler : IStoplightOpenApiBundler
{
    private const string _stoplightApiBase = "https://stoplight.io/api/v1/projects/";

    private static readonly Regex _stoplightNodeUrlRegex = new("^/api/v1/projects/(?<projectId>[^/]+)/api-docs/nodes/(?<nodePath>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly ILogger<StoplightOpenApiBundler> _logger;

    private int _fetchedNodeCount;
    private int _cacheHitCount;
    private int _resolvedExternalRefCount;

    public StoplightOpenApiBundler(IHttpClientCache httpClientCache, ILogger<StoplightOpenApiBundler> logger)
    {
        _httpClient = httpClientCache.GetSync("stoplight");
        _logger = logger;
    }

    public async ValueTask<string> Bundle(string projectId, string rootNodePath, string outputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        string normalizedRootNodePath = NormalizeNodePath(rootNodePath);
        string fullOutputFilePath = Path.GetFullPath(outputFilePath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputFilePath);

        _fetchedNodeCount = 0;
        _cacheHitCount = 0;
        _resolvedExternalRefCount = 0;

        _logger.LogInformation("Starting Stoplight OpenAPI bundle for project '{ProjectId}' from '{RootNodePath}' to '{OutputFilePath}'", projectId,
            normalizedRootNodePath, fullOutputFilePath);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            _logger.LogDebug("Ensured bundle output directory exists: {OutputDirectory}", outputDirectory);
        }

        var cache = new Dictionary<string, YamlNode>(StringComparer.OrdinalIgnoreCase);
        var resolutionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        YamlNode rootNode = await GetOrParseYamlRootAsync(projectId, normalizedRootNodePath, cache, cancellationToken)
            .ConfigureAwait(false);
        YamlNode bundledRootNode = await ResolveExternalRefsAsync(rootNode, projectId, normalizedRootNodePath, cache, resolutionStack, cancellationToken)
            .ConfigureAwait(false);

        var stream = new YamlStream(new YamlDocument(bundledRootNode));

        await using var writer = new StreamWriter(fullOutputFilePath, false, new UTF8Encoding(false));
        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);

        _logger.LogInformation(
            "Completed Stoplight OpenAPI bundle to '{OutputFilePath}'. Nodes fetched: {FetchedNodeCount}, cache hits: {CacheHitCount}, external refs resolved: {ResolvedExternalRefCount}",
            fullOutputFilePath, _fetchedNodeCount, _cacheHitCount, _resolvedExternalRefCount);

        return fullOutputFilePath;
    }

    public ValueTask<string> Bundle(string stoplightNodeUrl, string outputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stoplightNodeUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        (string projectId, string nodePath) = ParseStoplightNodeUrl(stoplightNodeUrl);

        _logger.LogInformation("Parsed Stoplight bundle URL '{StoplightNodeUrl}' to project '{ProjectId}' and node '{NodePath}'", stoplightNodeUrl, projectId,
            nodePath);

        return Bundle(projectId, nodePath, outputFilePath, cancellationToken);
    }

    private async ValueTask<string> FetchNodeContentAsync(string projectId, string nodePath, CancellationToken cancellationToken)
    {
        string requestUri = $"{_stoplightApiBase}{Uri.EscapeDataString(projectId)}/api-docs/nodes/{EscapeNodePathForUrl(nodePath)}";

        _logger.LogDebug("Fetching Stoplight node '{NodePath}' from '{RequestUri}'", nodePath, requestUri);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                                                              .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        string payload = await response.Content.ReadAsStringAsync(cancellationToken)
                                       .ConfigureAwait(false);
        _fetchedNodeCount++;

        if (TryExtractEnvelopeContent(payload, out string? content) && !string.IsNullOrWhiteSpace(content))
        {
            _logger.LogDebug("Fetched Stoplight node '{NodePath}' via JSON envelope with {CharacterCount} characters", nodePath, content.Length);
            return content;
        }

        _logger.LogDebug("Fetched Stoplight node '{NodePath}' as raw payload with {CharacterCount} characters", nodePath, payload.Length);

        return payload;
    }

    private static bool TryExtractEnvelopeContent(string payload, out string? content)
    {
        content = null;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty("content", out JsonElement contentElement))
                return false;

            if (contentElement.ValueKind != JsonValueKind.String)
                return false;

            content = contentElement.GetString();
            return !string.IsNullOrWhiteSpace(content);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ExtractExternalRefs(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        string trimmed = content.TrimStart();

        return LooksLikeJson(trimmed) ? ExtractRefsFromJson(content) : ExtractRefsFromYaml(content);
    }

    private static bool LooksLikeJson(string content)
    {
        return content.Length > 0 && (content[0] == '{' || content[0] == '[');
    }

    private static IReadOnlyList<string> ExtractRefsFromJson(string json)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(json);
        VisitJsonElement(doc.RootElement, refs);

        return refs.Count == 0 ? Array.Empty<string>() : refs.ToArray();

        static void VisitJsonElement(JsonElement element, HashSet<string> refs)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                        {
                            string? value = property.Value.GetString();

                            if (!string.IsNullOrWhiteSpace(value))
                                refs.Add(value!);
                        }
                        else
                        {
                            VisitJsonElement(property.Value, refs);
                        }
                    }

                    break;
                }
                case JsonValueKind.Array:
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        VisitJsonElement(item, refs);
                    }

                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> ExtractRefsFromYaml(string yaml)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);

            foreach (YamlDocument document in stream.Documents)
            {
                VisitYamlNode(document.RootNode, refs);
            }

            return refs.Count == 0 ? Array.Empty<string>() : refs.ToArray();
        }
        catch
        {
            return ExtractRefsFromYamlFallback(yaml);
        }

        static void VisitYamlNode(YamlNode? node, HashSet<string> refs)
        {
            if (node is null)
                return;

            switch (node)
            {
                case YamlMappingNode mapping:
                {
                    foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
                    {
                        if (keyNode is YamlScalarNode keyScalar && string.Equals(keyScalar.Value, "$ref", StringComparison.Ordinal) &&
                            valueNode is YamlScalarNode valueScalar && !string.IsNullOrWhiteSpace(valueScalar.Value))
                        {
                            refs.Add(valueScalar.Value!);
                        }
                        else
                        {
                            VisitYamlNode(valueNode, refs);
                        }
                    }

                    break;
                }
                case YamlSequenceNode sequence:
                {
                    foreach (YamlNode child in sequence.Children)
                    {
                        VisitYamlNode(child, refs);
                    }

                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> ExtractRefsFromYamlFallback(string yaml)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(yaml, @"(?m)^\s*\$ref\s*:\s*['""]?(?<ref>[^'""]+)['""]?\s*$"))
        {
            string? value = match.Groups["ref"].Value;

            if (!string.IsNullOrWhiteSpace(value))
                refs.Add(value);
        }

        return refs.Count == 0 ? Array.Empty<string>() : refs.ToArray();
    }

    private static bool IsLocalDocumentRef(string reference)
    {
        return reference.StartsWith("#/", StringComparison.Ordinal) || reference == "#";
    }

    private static bool IsRemoteAbsoluteUrl(string reference)
    {
        return Uri.TryCreate(reference, UriKind.Absolute, out _);
    }

    private static string ResolveRelativeNodePath(string currentDirectoryNodePath, string reference)
    {
        string pathPart = reference;
        int hashIndex = pathPart.IndexOf('#');

        if (hashIndex >= 0)
            pathPart = pathPart[..hashIndex];

        int queryIndex = pathPart.IndexOf('?');

        if (queryIndex >= 0)
            pathPart = pathPart[..queryIndex];

        pathPart = pathPart.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(pathPart))
            throw new InvalidOperationException($"Cannot resolve external ref '{reference}' because it does not contain a path.");

        string combined = CombinePosixPaths(currentDirectoryNodePath, pathPart);
        return NormalizeNodePath(combined);
    }

    private static string CombinePosixPaths(string leftDirectory, string rightRelativePath)
    {
        if (string.IsNullOrWhiteSpace(leftDirectory))
            return rightRelativePath;

        if (string.IsNullOrWhiteSpace(rightRelativePath))
            return leftDirectory;

        return $"{leftDirectory.TrimEnd('/')}/{rightRelativePath.TrimStart('/')}";
    }

    private static string NormalizeNodePath(string nodePath)
    {
        string path = nodePath.Replace('\\', '/')
                              .Trim();

        while (path.StartsWith('/'))
        {
            path = path[1..];
        }

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>(parts.Length);

        foreach (string part in parts)
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (stack.Count == 0)
                    throw new InvalidOperationException($"Cannot normalize node path '{nodePath}' because it escapes above the project root.");

                stack.Pop();
                continue;
            }

            stack.Push(part);
        }

        return string.Join('/', stack.Reverse());
    }

    private static string GetDirectoryNodePath(string nodePath)
    {
        int index = nodePath.LastIndexOf('/');

        return index < 0 ? string.Empty : nodePath[..index];
    }

    private static string EscapeNodePathForUrl(string nodePath)
    {
        return string.Join('/', nodePath.Split('/')
                                        .Select(Uri.EscapeDataString));
    }

    private async ValueTask<YamlNode> ResolveExternalRefsAsync(YamlNode node, string projectId, string currentNodePath, IDictionary<string, YamlNode> cache,
        ISet<string> resolutionStack, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case YamlMappingNode mappingNode:
            {
                if (TryGetRefValue(mappingNode, out string? reference) && !string.IsNullOrWhiteSpace(reference) && !IsLocalDocumentRef(reference) &&
                    !IsRemoteAbsoluteUrl(reference))
                {
                    _resolvedExternalRefCount++;
                    string resolutionKey = $"{projectId}|{currentNodePath}|{reference}";

                    if (!resolutionStack.Add(resolutionKey))
                    {
                        _logger.LogError(
                            "Circular external reference detected while bundling project '{ProjectId}': current '{CurrentNodePath}', ref '{Reference}'",
                            projectId, currentNodePath, reference);
                        throw new InvalidOperationException($"Circular external reference detected while bundling: {reference}");
                    }

                    _logger.LogDebug("Resolving external ref '{Reference}' from node '{CurrentNodePath}'", reference, currentNodePath);

                    try
                    {
                        YamlNode resolvedRefNode =
                            await ResolveReferencedNodeAsync(reference, projectId, currentNodePath, cache, resolutionStack, cancellationToken)
                                .ConfigureAwait(false);

                        if (mappingNode.Children.Count == 1)
                            return resolvedRefNode;

                        if (resolvedRefNode is not YamlMappingNode resolvedMappingNode)
                            return resolvedRefNode;

                        var mergedMapping = new YamlMappingNode();

                        foreach ((YamlNode key, YamlNode value) in resolvedMappingNode.Children)
                        {
                            mergedMapping.Add(CloneYamlNode(key), CloneYamlNode(value));
                        }

                        foreach ((YamlNode key, YamlNode value) in mappingNode.Children)
                        {
                            if (key is YamlScalarNode scalarKey && string.Equals(scalarKey.Value, "$ref", StringComparison.Ordinal))
                                continue;

                            mergedMapping.Children[CloneYamlNode(key)] =
                                await ResolveExternalRefsAsync(value, projectId, currentNodePath, cache, resolutionStack, cancellationToken)
                                    .ConfigureAwait(false);
                        }

                        return mergedMapping;
                    }
                    finally
                    {
                        resolutionStack.Remove(resolutionKey);
                    }
                }

                var resolvedMapping = new YamlMappingNode();

                foreach ((YamlNode key, YamlNode value) in mappingNode.Children)
                {
                    resolvedMapping.Add(CloneYamlNode(key),
                        await ResolveExternalRefsAsync(value, projectId, currentNodePath, cache, resolutionStack, cancellationToken)
                            .ConfigureAwait(false));
                }

                return resolvedMapping;
            }
            case YamlSequenceNode sequenceNode:
            {
                var resolvedSequence = new YamlSequenceNode();

                foreach (YamlNode child in sequenceNode.Children)
                {
                    resolvedSequence.Add(await ResolveExternalRefsAsync(child, projectId, currentNodePath, cache, resolutionStack, cancellationToken)
                        .ConfigureAwait(false));
                }

                return resolvedSequence;
            }
            default:
                return CloneYamlNode(node);
        }
    }

    private async ValueTask<YamlNode> ResolveReferencedNodeAsync(string reference, string projectId, string currentNodePath,
        IDictionary<string, YamlNode> cache, ISet<string> resolutionStack, CancellationToken cancellationToken)
    {
        SplitReference(reference, out string relativePath, out string? fragment);

        string referencedNodePath = ResolveRelativeNodePath(GetDirectoryNodePath(currentNodePath), relativePath);
        _logger.LogDebug("Resolved external ref '{Reference}' from '{CurrentNodePath}' to node '{ReferencedNodePath}' with fragment '{Fragment}'", reference,
            currentNodePath, referencedNodePath, fragment ?? "<none>");
        YamlNode referencedRootNode = await GetOrParseYamlRootAsync(projectId, referencedNodePath, cache, cancellationToken)
            .ConfigureAwait(false);
        YamlNode targetNode = string.IsNullOrWhiteSpace(fragment) ? referencedRootNode : ResolveJsonPointer(referencedRootNode, fragment!, referencedNodePath);

        return await ResolveExternalRefsAsync(targetNode, projectId, referencedNodePath, cache, resolutionStack, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<YamlNode> GetOrParseYamlRootAsync(string projectId, string nodePath, IDictionary<string, YamlNode> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(nodePath, out YamlNode? cachedNode))
        {
            _cacheHitCount++;
            _logger.LogDebug("Using cached Stoplight node '{NodePath}'", nodePath);
            return CloneYamlNode(cachedNode);
        }

        string yaml = await FetchNodeContentAsync(projectId, nodePath, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Parsing Stoplight node '{NodePath}'", nodePath);
        YamlNode parsedNode = ParseYamlRoot(yaml, nodePath);
        cache[nodePath] = CloneYamlNode(parsedNode);
        IReadOnlyList<string> refs = ExtractExternalRefs(yaml);
        _logger.LogDebug("Parsed Stoplight node '{NodePath}' with {ExternalRefCount} external refs discovered", nodePath, refs.Count);

        return parsedNode;
    }

    private static YamlNode ParseYamlRoot(string yaml, string filePath)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);

            if (stream.Documents.Count == 0)
                throw new InvalidOperationException($"No YAML document was found in '{filePath}'.");

            return stream.Documents[0].RootNode;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Failed to parse YAML file '{filePath}'.", e);
        }
    }

    private static void SplitReference(string reference, out string path, out string? fragment)
    {
        int hashIndex = reference.IndexOf('#');

        if (hashIndex < 0)
        {
            path = reference;
            fragment = null;
            return;
        }

        path = reference[..hashIndex];
        fragment = reference[hashIndex..];
    }

    private static YamlNode ResolveJsonPointer(YamlNode rootNode, string fragment, string filePath)
    {
        if (fragment == "#")
            return CloneYamlNode(rootNode);

        if (!fragment.StartsWith("#/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported fragment '{fragment}' in '{filePath}'.");

        string[] segments = fragment[2..]
                            .Split('/', StringSplitOptions.None)
                            .Select(UnescapeJsonPointerSegment)
                            .ToArray();

        YamlNode currentNode = rootNode;

        foreach (string segment in segments)
        {
            currentNode = currentNode switch
            {
                YamlMappingNode mappingNode => ResolveMappingChild(mappingNode, segment, filePath, fragment),
                YamlSequenceNode sequenceNode => ResolveSequenceChild(sequenceNode, segment, filePath, fragment),
                _ => throw new InvalidOperationException($"Unable to resolve fragment '{fragment}' in '{filePath}'.")
            };
        }

        return CloneYamlNode(currentNode);
    }

    private static YamlNode ResolveMappingChild(YamlMappingNode mappingNode, string segment, string filePath, string fragment)
    {
        foreach ((YamlNode keyNode, YamlNode valueNode) in mappingNode.Children)
        {
            if (keyNode is YamlScalarNode scalarNode && string.Equals(scalarNode.Value, segment, StringComparison.Ordinal))
                return valueNode;
        }

        throw new InvalidOperationException($"Fragment '{fragment}' could not be resolved in '{filePath}'.");
    }

    private static YamlNode ResolveSequenceChild(YamlSequenceNode sequenceNode, string segment, string filePath, string fragment)
    {
        if (!int.TryParse(segment, out int index) || index < 0 || index >= sequenceNode.Children.Count)
            throw new InvalidOperationException($"Fragment '{fragment}' could not be resolved in '{filePath}'.");

        return sequenceNode.Children[index];
    }

    private static string UnescapeJsonPointerSegment(string segment)
    {
        return segment.Replace("~1", "/")
                      .Replace("~0", "~");
    }

    private static bool TryGetRefValue(YamlMappingNode mappingNode, out string? reference)
    {
        foreach ((YamlNode keyNode, YamlNode valueNode) in mappingNode.Children)
        {
            if (keyNode is YamlScalarNode keyScalar && string.Equals(keyScalar.Value, "$ref", StringComparison.Ordinal) &&
                valueNode is YamlScalarNode valueScalar)
            {
                reference = valueScalar.Value;
                return true;
            }
        }

        reference = null;
        return false;
    }

    private static YamlNode CloneYamlNode(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalarNode:
                return new YamlScalarNode(scalarNode.Value)
                {
                    Anchor = scalarNode.Anchor,
                    Tag = scalarNode.Tag,
                    Style = scalarNode.Style
                };
            case YamlSequenceNode sequenceNode:
            {
                var clone = new YamlSequenceNode(sequenceNode.Children.Select(CloneYamlNode))
                {
                    Anchor = sequenceNode.Anchor,
                    Tag = sequenceNode.Tag,
                    Style = sequenceNode.Style
                };

                return clone;
            }
            case YamlMappingNode mappingNode:
            {
                var clone = new YamlMappingNode
                {
                    Anchor = mappingNode.Anchor,
                    Tag = mappingNode.Tag,
                    Style = mappingNode.Style
                };

                foreach ((YamlNode keyNode, YamlNode valueNode) in mappingNode.Children)
                {
                    clone.Add(CloneYamlNode(keyNode), CloneYamlNode(valueNode));
                }

                return clone;
            }
            default:
                throw new NotSupportedException($"YAML node type '{node.GetType().Name}' is not supported.");
        }
    }

    private static (string projectId, string nodePath) ParseStoplightNodeUrl(string stoplightNodeUrl)
    {
        if (!Uri.TryCreate(stoplightNodeUrl, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException($"'{stoplightNodeUrl}' is not a valid absolute URL.", nameof(stoplightNodeUrl));

        Match match = _stoplightNodeUrlRegex.Match(uri.AbsolutePath);

        if (!match.Success)
            throw new ArgumentException($"'{stoplightNodeUrl}' is not a supported Stoplight node URL.", nameof(stoplightNodeUrl));

        string projectId = Uri.UnescapeDataString(match.Groups["projectId"].Value);
        string nodePath = Uri.UnescapeDataString(match.Groups["nodePath"].Value);

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(nodePath))
            throw new ArgumentException($"'{stoplightNodeUrl}' is missing the project id or node path.", nameof(stoplightNodeUrl));

        return (projectId, nodePath);
    }
}