# FORMULA 2.0 - Formal Specifications for Verification and Synthesis
[![build](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml)

## Building and running FORMULA
### With Nix flakes (macOS/Linux)
To build and run the command line interpreter with Nix flakes, run

```bash
$ nix run github:VUISIS/formula-dotnet
```

### With .NET on x64
To build the command line interpreter, run the following commands from Src/CommandLine.

```bash
$ dotnet build CommandLine.sln /p:Configuration=Debug|Release /p:Platform=x64
$ dotnet ./bin/<Configuration>/<OS>/<PLATFORM>/net6.0/CommandLine.dll
```

### With .NET on ARM64 MacOS
```bash
For native ARM64 builds on Mac OS X run nuget add before restore and build:
$ dotnet nuget add source --username USERNAME --password GITHUB_TOKEN --store-password-in-clear-text --name github "https://nuget.pkg.github.com/VUISIS/index.json"
$ dotnet build CommandLineARM.sln /p:Configuration=Debug|Release /p:Platform=ARM64
$ dotnet ./bin/<Configuration>/<OS>/<PLATFORM>/net6.0/CommandLine.dll

If you are unable to add the VUISIS ARM nuget package, you can build with the x64 commands.
$ dotnet build CommandLine.sln /p:Configuration=Debug|Release /p:Platform=x64
$ dotnet ./bin/<Configuration>/<OS>/<PLATFORM>/net6.0/CommandLine.dll
```

To run unit tests with Formula, run the following command from
Src/Tests.

```bash
$  dotnet test Tests.csproj /p:Configuration=Debug|Release /p:Platform=x64|ARM64

For specific tests
$ dotnet test Tests.csproj /p:Configuration=Debug|Release /p:Platform=x64|ARM64 --filter "FullyQualifiedName=<NAMESPACE>.<CLASS>.<METHOD>"
```

You can exit the command line interpreter with the "exit" command.
