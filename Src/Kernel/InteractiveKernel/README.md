# FORMULA 2.0 Jupyter Notebook Kernel
Formal Specifications for Verification and Synthesis

## Building FORMULA Kernel

In order to build FORMULA, you will need .NET core installed.

Clone modified jupyter-core submodule into the Src\Kernel folder.

```bash
cd to top directory
git submodule init
git submodule update --remote
```

To build the interactive kernel, from Src\Kernel\InteractiveKernel:

```bash
dotnet build InteractiveKernel.csproj
```

Install jupyter globally or install with dotnet and --sys-prefix if using conda or venv.

```bash
sudo -H pip3 install jupyter
```

To install the kernelspec to jupyter, from Src\Kernel\InteractiveKernel:

```bash
dotnet run -- install
or
dotnet run -- install --sys-prefix <CONDA/VENV INSTALL>
```