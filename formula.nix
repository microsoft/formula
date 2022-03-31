{ lib, stdenv, fetchFromGitHub, buildDotnetModule, dotnetCorePackages }:

buildDotnetModule rec {
  pname = "formula-dotnet";
  version = "2.0";

  src = ./.;

  nugetDeps = ./nuget.nix;
  projectFile = "Src/CommandLine/CommandLine.csproj";

  dotnet-runtime = dotnetCorePackages.runtime_6_0;
  dotnet-sdk = dotnetCorePackages.sdk_6_0;
  postFixup = if stdenv.isLinux then ''
    mv $out/bin/CommandLine $out/bin/formula
  '' else lib.optionalString stdenv.isDarwin ''
    makeWrapper ${dotnetCorePackages.runtime_6_0}/bin/dotnet $out/bin/formula \
      --add-flags "$out/lib/formula-dotnet/CommandLine.dll" \
      --prefix DYLD_LIBRARY_PATH : $out/lib/formula-dotnet/runtimes/macos/native
  '';

  meta = with lib; {
    description = "Formal Specifications for Verification and Synthesis";
    homepage = "https://github.com/VUISIS/formula-dotnet";
    license = licenses.mspl;
    maintainers = with maintainers; [ siraben ];
    platforms = platforms.unix;
    mainProgram = "formula";
  };
}
