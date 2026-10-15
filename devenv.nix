{
  pkgs,
  lib,
  ...
}: let
  linuxRuntimeLibraries = with pkgs; [
    fontconfig
    freetype
    libGL
    libice
    libsm
    libx11
    libxcursor
    libxext
    libxfixes
    libxi
    libxinerama
    libxrandr
    libxrender
    libxcb
    libxkbcommon
    wayland
  ];
in {
  packages = [pkgs.dotnet-sdk_10] ++ linuxRuntimeLibraries;

  # Avalonia's Linux native backends load these libraries at runtime.
  env.LD_LIBRARY_PATH = lib.makeLibraryPath linuxRuntimeLibraries;

  scripts.restore.exec = "dotnet restore WheelWizard.sln";
  scripts.build.exec = "dotnet build WheelWizard.sln --configuration Release";
  scripts.test.exec = "dotnet test WheelWizard.sln --configuration Release";
  scripts.run.exec = "dotnet run --project WheelWizard/WheelWizard.csproj -c Release";
}
