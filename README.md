[![](https://img.shields.io/nuget/v/soenneker.stoplight.openapibundler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.stoplight.openapibundler/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.stoplight.openapibundler/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.stoplight.openapibundler/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.stoplight.openapibundler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.stoplight.openapibundler/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.stoplight.openapibundler/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.stoplight.openapibundler/actions/workflows/codeql.yml)

# Soenneker.Stoplight.OpenApiBundler

Downloads a Stoplight OpenAPI document and inlines its relative external YAML references into one local YAML file.

## Installation

```bash
dotnet add package Soenneker.Stoplight.OpenApiBundler
```

## Registration

```csharp
using Soenneker.Stoplight.OpenApiBundler.Registrars;

builder.Services.AddStoplightOpenApiBundlerAsSingleton();
```

The scoped registrar is also available when each dependency-injection scope should have its own bundler instance. Both registrations reuse the shared HTTP client cache.

## Bundle by project and node path

```csharp
using Soenneker.Stoplight.OpenApiBundler.Abstract;

string outputPath = await bundler.Bundle(
    projectId: "calendly",
    rootNodePath: "reference/calendly-api/openapi.yaml",
    outputFilePath: "artifacts/calendly.openapi.yaml",
    cancellationToken);
```

## Bundle from a Stoplight node URL

```csharp
string outputPath = await bundler.Bundle(
    "https://stoplight.io/api/v1/projects/calendly/api-docs/nodes/reference/calendly-api/openapi.yaml",
    "artifacts/calendly.openapi.yaml",
    cancellationToken);
```

The return value is the absolute path of the written file. Missing output directories are created. The final file replaces an existing destination only after the complete YAML has been serialized successfully.

## Reference behavior

Relative references to other Stoplight nodes are downloaded and inlined, including JSON Pointer fragments. References within the current document, such as `#/components/schemas/User`, remain references. Absolute remote URLs also remain unchanged.

Sibling properties next to an external `$ref` are merged over the referenced mapping. Circular external references and paths that escape above the Stoplight project root fail the bundle instead of producing partial output.

The built-in client calls Stoplight's public project-node API and has no authentication option. It is therefore suitable only for nodes that endpoint permits the application to fetch.
