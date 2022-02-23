# FORMULA 2.0 - Formal Specifications for Verification and Synthesis
[![build](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/VUISIS/formula-dotnet/actions/workflows/build.yml)

## Building FORMULA
### With Nix flakes
To build and run the command line interpreter with Nix flakes, run

```bash
$ nix run github:VUISIS/formula-dotnet
```

Note that if you are on an M1 Mac, use `github:VUISIS/formula-dotnet#defaultPackages.x86_64-darwin` instead.

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
