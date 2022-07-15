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
$ dotnet build
```

To run the command line interpreter:

```bash
$ dotnet run
```

You can exit the command line interpreter with the "exit" command.
