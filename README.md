# FORMULA 2.0 - Formal Specifications for Verification and Synthesis
[![build](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml)

## Building FORMULA
### With Nix flakes (macOS/Linux)
To build and run the command line interpreter with Nix flakes, run

```bash
$ nix run github:VUISIS/formula-dotnet
```

### With .NET
To build the command line interpreter, run the following commands from
Src/CommandLine.

```bash
For Debug|Release and x64:
$ dotnet build CommandLine.csproj /p:Configuration=Debug|Release /p:Platform=x64

For native arm64 builds on Mac OS X:
$ dotnet nuget add source --username USERNAME --password GITHUB_TOKEN --store-password-in-clear-text --name github "https://nuget.pkg.github.com/VUISIS/index.json"
$ dotnet restore CommandLine.csproj 
$ dotnet build CommandLine.csproj /p:Configuration=Debug|Release /p:Platform=ARM64
```

To run the command line interpreter:

```bash
$ dotnet ./bin/Release/<OS>/<PLATFORM>/CommandLine.dll
```

To run unit tests with Formula, run the following command from
Src/Tests.

```bash
$  dotnet test Tests.csproj /p:Configuration=Debug /p:Platform=ARM64

For specific tests
$ dotnet test Tests.csproj /p:Configuration=Debug /p:Platform=ARM64 --filter "FullyQualifiedName=<NAMESPACE>.<CLASS>.<METHOD>"
```

You can exit the command line interpreter with the "exit" command.
