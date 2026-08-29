[![](https://img.shields.io/nuget/v/soenneker.stoplight.openapibundler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.stoplight.openapibundler/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.stoplight.openapibundler/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.stoplight.openapibundler/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.stoplight.openapibundler.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.stoplight.openapibundler/)

# Soenneker.Stoplight.OpenApiBundler

A utility library to download and bundle OpenApi specs from Stoplight.

## Install

```bash
dotnet add package Soenneker.Stoplight.OpenApiBundler
```

## Quick start

```csharp
using Soenneker.Stoplight.OpenApiBundler.Registrars;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var result = services.AddStoplightOpenApiBundlerAsSingleton();
```

Adds `IStoplightOpenApiBundler` as a singleton service.

## What you get

- `IStoplightOpenApiBundler` — A utility library to download and bundle OpenApi specs from Stoplight.
- `StoplightOpenApiBundlerRegistrar` — A utility library to download and bundle OpenApi specs from Stoplight.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `IStoplightOpenApiBundler.Bundle(projectId, rootNodePath, outputFilePath, cancellationToken)` | Bundles the root spec from Stoplight into a single YAML file. | The full local path to the bundled file. |
| `IStoplightOpenApiBundler.Bundle(stoplightNodeUrl, outputFilePath, cancellationToken)` | Bundles the root spec from a Stoplight node URL into a single YAML file. | The full local path to the bundled file. |
| `StoplightOpenApiBundlerRegistrar.AddStoplightOpenApiBundlerAsSingleton(services)` | Adds `IStoplightOpenApiBundler` as a singleton service. | The same service collection, so additional registrations can be chained. |
| `StoplightOpenApiBundlerRegistrar.AddStoplightOpenApiBundlerAsScoped(services)` | Adds `IStoplightOpenApiBundler` as a scoped service. | The same service collection, so additional registrations can be chained. |

## Practical notes

- Cancellation stops pending work; it does not undo work that has already completed.
