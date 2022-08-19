# FORMULA 2.0 - Formal Specifications for Verification and Synthesis
[![build](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml)

## Building FORMULA
### With Nix flakes (macOS/Linux)
To build and run the command line interpreter with Nix flakes, run

```bash
$ nix run github:VUISIS/formula-dotnet
```

### With .NET
To build the command line interpreter, run the following command from
Src/CommandLine.

```bash
For Debug and x64:
$ dotnet build

For release builds:
$ dotnet build CommandLine.csproj /p:Configuration=Release

For native arm64 builds on Mac OS X:
$ dotnet nuget add source --username USERNAME --password GITHUB_TOKEN --store-password-in-clear-text --name github "https://nuget.pkg.github.com/VUISIS/index.json"
$ dotnet restore CommandLine.csproj 
$ dotnet build CommandLine.csproj /p:Configuration=Debug|Release /p:Platform=ARM64
```

To run the command line interpreter:

```bash
$ dotnet run
```

You can exit the command line interpreter with the "exit" command.
