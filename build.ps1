param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $vswhere)) {
    throw "Visual Studio Build Tools (vswhere.exe) was not found."
}

$installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installationPath) {
    throw "Visual Studio Build Tools was not found."
}

$csc = Join-Path $installationPath "MSBuild\Current\Bin\Roslyn\csc.exe"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$sourcePng = Join-Path $root "Assets\mimi-source.png"
$iconFile = Join-Path $root "Assets\mimi.ico"
$toolOutputDirectory = Join-Path $root "obj\IconMaker"
$iconMaker = Join-Path $toolOutputDirectory "IconMaker.exe"

New-Item -ItemType Directory -Force -Path $toolOutputDirectory | Out-Null

& $csc /nologo /target:exe "/out:$iconMaker" /reference:System.Drawing.dll (Join-Path $root "tools\IconMaker.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Compiling IconMaker failed."
}

& $iconMaker $sourcePng $iconFile
if ($LASTEXITCODE -ne 0) {
    throw "Generating the application icon failed."
}

$outputDirectory = Join-Path $root "bin\$Configuration"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$references = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Net.Http.dll",
    "System.Web.Extensions.dll",
    "System.Windows.Forms.dll"
)

$sources = @(
    "Program.cs",
    "TrayApplicationContext.cs",
    "MainForm.cs",
    "Services\MciAudioRecorder.cs",
    "Services\OpenAiTranscriptionClient.cs",
    "Properties\AssemblyInfo.cs"
)

$compilerArguments = @(
    "/nologo",
    "/noconfig",
    "/nostdlib+",
    "/target:winexe",
    "/platform:anycpu",
    "/langversion:latest",
    "/warn:4",
    "/win32icon:$iconFile",
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    "/resource:$iconFile,Mimi.Assets.mimi.ico",
    "/out:$(Join-Path $outputDirectory 'Mimi.exe')"
)

if ($Configuration -eq "Release") {
    $compilerArguments += "/optimize+"
    $compilerArguments += "/debug:pdbonly"
} else {
    $compilerArguments += "/optimize-"
    $compilerArguments += "/debug:full"
    $compilerArguments += "/define:DEBUG;TRACE"
}

foreach ($reference in $references) {
    $compilerArguments += "/reference:$(Join-Path $framework $reference)"
}

foreach ($source in $sources) {
    $compilerArguments += (Join-Path $root $source)
}

& $csc $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Building mimi failed."
}

$exe = Join-Path $root "bin\$Configuration\Mimi.exe"
$smokeTest = Join-Path $toolOutputDirectory "SmokeTests.exe"
$smokeArguments = @(
    "/nologo",
    "/noconfig",
    "/nostdlib+",
    "/target:exe",
    "/platform:anycpu",
    "/langversion:latest",
    "/out:$smokeTest"
)

foreach ($reference in $references) {
    $smokeArguments += "/reference:$(Join-Path $framework $reference)"
}

$smokeArguments += (Join-Path $root "tests\SmokeTests.cs")
& $csc $smokeArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compiling smoke tests failed."
}

& $smokeTest $exe $iconFile
if ($LASTEXITCODE -ne 0) {
    throw "Smoke tests failed."
}

Write-Host ""
Write-Host "Build complete: $exe" -ForegroundColor Green
