# FORMULA 2.0 Jupyter Notebook Kernel
Formal Specifications for Verification and Synthesis

## Building FORMULA Kernel

In order to build FORMULA, you will need .NET core installed.

To build the interactive kernel, from Src\Kernel\InteractiveKernel:

```bash
dotnet build InteractiveKernel.csproj
```

To install the kernelspec to jupyter, from Src\Kernel\InteractiveKernel:

```bash
dotnet run -- install
```