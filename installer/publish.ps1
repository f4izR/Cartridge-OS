# Publishes Launcher + Service as self-contained win-x64 builds for the Inno Setup installer to
# package. Self-contained (not framework-dependent) so end users never need to separately install
# the .NET runtime — bigger output, but this is a closed-source consumer app handed to a real user's
# PC, not a dev tool where a shared runtime is a reasonable ask.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "publish"

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue

dotnet publish "$root\src\CartridgeOS.Launcher\CartridgeOS.Launcher.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "$out\Launcher"
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed" }

dotnet publish "$root\src\CartridgeOS.Service\CartridgeOS.Service.csproj" -c Release -r win-x64 --self-contained true -o "$out\Service"
if ($LASTEXITCODE -ne 0) { throw "Service publish failed" }

Write-Output "Published to $out"
