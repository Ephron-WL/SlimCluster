# SlimCluster

SlimCluster has the [Raft](https://raft.github.io/raft.pdf) distributed consensus algorithm implemented in .NET.
Additionally, it implements the [SWIM](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) cluster membership list (where nodes join and leave/die).

* Membership list is required to maintain what micro-service instances (nodes) constitute a cluster.
* Raft consensus helps propagate state across the micro-service instances and ensures there is a designated leader instance performing the coordination of work.

The library goal is to provide a common groundwork for coordination and consensus of your distributed micro-service instances.
With that, the developer can focus on the business problem at hand.
The library promises to have a friendly API and pluggable architecture.

The strategic aim for SlimCluster is to implement other algorithms to make distributed .NET micro-services easier and not require one to pull in a load of other 3rd party libraries or products.

[![Gitter](https://badges.gitter.im/SlimCluster/community.svg)](https://gitter.im/SlimCluster/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)
[![GitHub license](https://img.shields.io/github/license/zarusz/SlimCluster)](https://github.com/zarusz/SlimCluster/blob/master/LICENSE)
[![Build](https://github.com/zarusz/SlimCluster/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/zarusz/SlimCluster/actions/workflows/build.yml)

## Roadmap

> This is a new project and still a work in progress!

The path to a stable production release:

* :white_check_mark: Step 1: Implement the SWIM membership over UDP + sample.
* :white_large_square: Step 2: Documentation on SWIM membership.
* :white_large_square: Step 3: Implement the Raft over TCP/UDP + sample.
* :white_large_square: Step 4: Documentation on Raft consensus.
* :white_large_square: Step 5: Other extensions and flavor.

## Packages

| Name                             | Description                                           | NuGet                                                                                                                                        |
| -------------------------------- | ----------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `SlimCluster`                    | The core cluster interfaces                           | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.svg)](https://www.nuget.org/packages/SlimCluster)                                       |
| `SlimCluster.Membership`         | The membership core interfaces                        | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Membership.svg)](https://www.nuget.org/packages/SlimCluster.Membership)                 |
| `SlimCluster.Membership.Swim`    | The SWIM membership algorithm implementation over UDP | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Membership.Swim.svg)](https://www.nuget.org/packages/SlimCluster.Membership.Swim)       |
| `SlimCluster.Concensus`          | The consensus protocol core interfaces                | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Consensus.svg)](https://www.nuget.org/packages/SlimCluster.Consensus)                   |
| `SlimCluster.Consensus.Raft`     | Raft RPC implemented over TCP                         | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Consensus.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Consensus.Raft)         |
| `SlimCluster.Serialization`      | The core serialization interfaces                     | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Serialization.svg)](https://www.nuget.org/packages/SlimCluster.Serialization)           |
| `SlimCluster.Serialization.Json` | JSON serialization plugin                             | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Serialization.Json.svg)](https://www.nuget.org/packages/SlimCluster.Serialization.Json) |

## Samples

Check out the [Samples](src/Samples/) folder on how to get started.

### Example usage

Setup membership discovery using the SWIM algorithm:

```cs
// Assuming you're using Microsoft.Extensions.DependencyInjection
IServicesCollection services;

// We are setting up the SWIM membership algorithm for your micro-service instances
services.AddClusterMembership(opts =>
    {
        opts.Port = 60001;
        opts.MulticastGroupAddress = "239.1.1.1";
        opts.ClusterId = "MyMicroserviceCluster";
        opts.MembershipEventPiggybackCount = 2;
    },
    serializerFactory: (svp) => new JsonSerializer(Encoding.ASCII)
);

// Requires packages: SlimCluster.Membership.Swim, SlimCluster.Serialization.Json
```

Then somewhere in the micro-service, you can inject the `IClusterMembership`:

```cs
// Injected, this will be a singleton
IClusterMembership cluster;

// Provides a snapshot collection of the current instances discovered and alive/healthy:
cluster.Members 

// Allows to get notifications when an new instance joines or leaves (dies):
cluster.MemberJoined += (sender, e) => { /* e.Node and e.Timestamp */ };
cluster.MemberLeft += (sender, e) => { /* e.Node and e.Timestamp */ };

```

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Build

```cmd
cd src
dotnet build
dotnet pack --output ../dist
```

NuGet packaged end up in `dist` folder

## Testing

To run tests you need to update the respective `appsettings.json` to match your cloud infrastructure or local infrastructure.

Run all tests:
```cmd
dotnet test
```

Run all tests except  integration tests which require local/cloud infrastructure:
```cmd
dotnet test --filter Category!=Integration
```
