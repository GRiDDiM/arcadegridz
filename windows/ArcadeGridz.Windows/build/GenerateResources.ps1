param(
    [Parameter(Mandatory = $true)][string]$IntermediateRoot,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
$kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makePri = Get-ChildItem $kitRoot -Filter makepri.exe -File -Recurse |
    Where-Object { $_.FullName -match "\\x64\\makepri\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makePri) { throw "makepri.exe was not found in the Windows 10 SDK." }

$rootXbf = Get-ChildItem $IntermediateRoot -Filter *.xbf -File |
    Where-Object { $_.DirectoryName -eq (Resolve-Path $IntermediateRoot).Path }
if (-not ($rootXbf.Name -contains "App.xbf") -or -not ($rootXbf.Name -contains "MainWindow.xbf")) {
    throw "The WinUI compiler did not produce App.xbf and MainWindow.xbf in $IntermediateRoot."
}

$staging = Join-Path $env:RUNNER_TEMP "ArcadeGridz-Pri-$PID"
New-Item -ItemType Directory -Force $staging | Out-Null
try {
    Copy-Item $rootXbf.FullName $staging
    $config = Join-Path $staging "priconfig.xml"
    & $makePri.FullName createconfig /cf $config /dq en-US /o
    if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed with $LASTEXITCODE." }

    [xml]$xml = Get-Content $config
    if ($xml.resources.packaging) {
        $null = $xml.resources.RemoveChild($xml.resources.packaging)
        $xml.Save($config)
    }

    New-Item -ItemType Directory -Force $PublishDirectory | Out-Null
    $output = Join-Path $PublishDirectory "resources.pri"
    & $makePri.FullName new /pr $staging /cf $config /of $output /o
    if ($LASTEXITCODE -ne 0) { throw "makepri new failed with $LASTEXITCODE." }
    if (-not (Test-Path $output)) { throw "makepri did not produce resources.pri." }
    Write-Host "Generated unpackaged application resource index: $output"
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
