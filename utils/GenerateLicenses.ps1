<#
.SYNOPSIS
Generiert die THIRD-PARTY-PACKAGES.txt für alle Projekte.
#>

$ErrorActionPreference = "Stop"

$rootDir = Get-Location
$outputFile = Join-Path $rootDir "THIRD-PARTY-NOTICES.txt"
$tempDir = Join-Path $rootDir "TempLicenses"

$projectFiles = Get-ChildItem -Path $rootDir -Filter *.csproj -Recurse

if ($projectFiles.Count -eq 0) {
    Write-Error "Keine .csproj Dateien gefunden!"
    exit
}

Write-Host "$($projectFiles.Count) Projekte gefunden. Starte Lizenz-Export..." -ForegroundColor Cyan

# Temp Ordner aufräumen/erstellen
if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

# 1. Lizenzen exportieren mit den KORREKTEN Parametern
foreach ($proj in $projectFiles) {
    Write-Host "  -> Verarbeite $($proj.Name)..." -ForegroundColor DarkGray
    try {
        # -i = input projekt
        # -e = export-license-texts
        # -f = output-directory (Hier lag der Fehler!)
        # -c = convert-html-to-text (Natives Feature des Tools!)
        # -t = include-transitive
        dotnet-project-licenses -i $proj.FullName -e -f $tempDir -c -t | Out-Null
    }
    catch {
        Write-Warning "Fehler bei $($proj.Name), überspringe..."
    }
}

# Lade alle exportierten txt-Dateien (HTML wurde ja bereits vom Tool konvertiert)
$licenseFiles = Get-ChildItem -Path $tempDir -Filter *.txt

if ($licenseFiles.Count -eq 0) {
    Write-Error "Es konnten keine Lizenzen exportiert werden."
    exit
}

Write-Host "$($licenseFiles.Count) einzigartige Lizenzen gefunden. Erstelle Datei..." -ForegroundColor Cyan

# 2. Header schreiben
$header = @"
This project uses third-party libraries or other resources that may be
distributed under licenses granting rights in addition to those listed above.
This file lists these licenses.

========================================================================
THIRD PARTY NOTICES
========================================================================
"@

Set-Content -Path $outputFile -Value $header -Encoding UTF8

# 3. Dateien zusammenfügen
foreach ($file in $licenseFiles) {
    $packageName = $file.BaseName
    
    $separator = @"

========================================================================
$packageName
========================================================================

"@
    Add-Content -Path $outputFile -Value $separator -Encoding UTF8
    
    $content = Get-Content -Path $file.FullName -Raw
    Add-Content -Path $outputFile -Value $content -Encoding UTF8
}

# 4. Aufräumen
Write-Host "Räume auf... Lösche $tempDir Ordner" -ForegroundColor Cyan
Remove-Item -Path $tempDir -Recurse -Force

Write-Host "Erfolg! Die saubere Datei $outputFile wurde im Root erstellt." -ForegroundColor Green