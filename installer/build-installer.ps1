<#
.SYNOPSIS
    Baut das MSI-Paket für ELW-Meteo.

.DESCRIPTION
    Harvests a published folder into a WiX fragment and builds the installer
    from it.

    The file list is generated rather than maintained. A hand-written one rots
    the first time a dependency is added, and it does so quietly: the package
    installs, and then the application does not start because one assembly was
    never listed. Generating it means the installer always contains exactly what
    `dotnet publish` produced.

.PARAMETER PublishDirectory
    Folder produced by `dotnet publish`. Must contain ELW-Meteo.exe.

.PARAMETER Version
    Three-part version for the package, e.g. 1.2.0.

.PARAMETER OutputPath
    Where the .msi is written.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputPath = "ELW-Meteo.msi",
    [string] $WixCommand = "wix",
    [string] $UiExtension = "WixToolset.UI.wixext/5.0.2"
)

$ErrorActionPreference = "Stop"

$installerDirectory = Split-Path -Parent $PSCommandPath
$publish = (Resolve-Path $PublishDirectory).Path

$executable = Join-Path $publish "ELW-Meteo.exe"

if (-not (Test-Path $executable)) {
    # Without this the build still succeeds and produces a package that installs
    # a folder with no application in it.
    throw "ELW-Meteo.exe fehlt in '$publish' — wurde dotnet publish ausgeführt?"
}

# ---------------------------------------------------------------- version
#
# MSI compares three parts and ignores the fourth, so a pre-release suffix has
# to go. Shipping "1.2.0-beta.1" would be rejected by the installer outright.

$numeric = [regex]::Match($Version, '^\d+(\.\d+){0,2}')

if (-not $numeric.Success) {
    throw "Version '$Version' enthält keine verwertbare Nummer."
}

$msiVersion = $numeric.Value

while (($msiVersion -split '\.').Count -lt 3) {
    $msiVersion += ".0"
}

Write-Host "Paketversion: $msiVersion (aus '$Version')"

# ----------------------------------------------------------- file harvest

function ConvertTo-WixIdentifier {
    <#
        WiX identifiers allow letters, digits, underscore and period, and must
        not start with a digit. A hash of the relative path is appended so two
        files with the same name in different folders cannot collide — which
        they otherwise would, silently, and one of them would go missing.
    #>
    param([string] $Prefix, [string] $RelativePath)

    $safe = ($RelativePath -replace '[^A-Za-z0-9_.]', '_')

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($RelativePath))
    $sha.Dispose()

    $suffix = ([System.BitConverter]::ToString($bytes) -replace '-', '').Substring(0, 8)

    if ($safe.Length -gt 40) {
        $safe = $safe.Substring($safe.Length - 40)
    }

    return "${Prefix}_${safe}_${suffix}"
}

function Get-RelativeKey {
    <#
        Path relative to the publish folder, always with backslashes.

        Deliberately string work rather than the Path API: GetDirectoryName
        returns the separator of the machine running the script, so a lookup
        keyed on one form and queried with the other silently misses and the
        file lands in no directory at all. The installer is built on Windows
        today, but a helper whose output depends on where it ran is not one that
        can be reasoned about — the same assumption already cost a bug in the
        update swap script.
    #>
    param([string] $FullPath)

    return $FullPath.Substring($publish.Length).TrimStart('\', '/').Replace('/', '\')
}

$files = Get-ChildItem -Path $publish -Recurse -File | Sort-Object FullName
Write-Host "$($files.Count) Dateien gefunden."

$directories = [System.Collections.Generic.Dictionary[string, string]]::new()
$directories[""] = "INSTALLFOLDER"

# Every folder needs a Directory element before anything inside it can be
# referenced, and parents must come before children.
$folders = $files |
    ForEach-Object {
        $relative = Get-RelativeKey $_.FullName
        $cut = $relative.LastIndexOf('\')
        if ($cut -lt 0) { "" } else { $relative.Substring(0, $cut) }
    } |
    Where-Object { $_ -ne "" } |
    Sort-Object -Unique

foreach ($folder in $folders) {
    $parts = $folder -split '\\'

    for ($i = 0; $i -lt $parts.Count; $i++) {
        $path = ($parts[0..$i] -join '\')

        if (-not $directories.ContainsKey($path)) {
            $directories[$path] = ConvertTo-WixIdentifier -Prefix "dir" -RelativePath $path
        }
    }
}

$xml = [System.Text.StringBuilder]::new()
[void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$xml.AppendLine('<!-- Erzeugt von build-installer.ps1. Nicht von Hand bearbeiten. -->')
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$xml.AppendLine('  <Fragment>')

# --- directory tree
foreach ($path in ($directories.Keys | Where-Object { $_ -ne "" } | Sort-Object)) {
    $cut = $path.LastIndexOf('\')
    $parent = if ($cut -lt 0) { "" } else { $path.Substring(0, $cut) }
    $parentId = $directories[$parent]
    $name = $path.Substring($cut + 1)

    [void]$xml.AppendLine(
        "    <DirectoryRef Id=`"$parentId`"><Directory Id=`"$($directories[$path])`" Name=`"$([System.Security.SecurityElement]::Escape($name))`" /></DirectoryRef>")
}

# --- components, one file each
[void]$xml.AppendLine('  </Fragment>')
[void]$xml.AppendLine('  <Fragment>')
[void]$xml.AppendLine('    <ComponentGroup Id="AppFiles">')

foreach ($file in $files) {
    $relative = Get-RelativeKey $file.FullName
    $cut = $relative.LastIndexOf('\')
    $folder = if ($cut -lt 0) { "" } else { $relative.Substring(0, $cut) }
    $directoryId = $directories[$folder]

    if ([string]::IsNullOrEmpty($directoryId)) {
        throw "Kein Verzeichnis für '$relative' — die Verzeichnisliste ist unvollständig."
    }

    # The shortcut in ELW-Meteo.wxs targets this identifier by name.
    $fileId = if ($relative -eq "ELW-Meteo.exe") {
        "MainExecutable"
    } else {
        ConvertTo-WixIdentifier -Prefix "file" -RelativePath $relative
    }

    $componentId = ConvertTo-WixIdentifier -Prefix "cmp" -RelativePath $relative
    $source = [System.Security.SecurityElement]::Escape($file.FullName)
    $name = [System.Security.SecurityElement]::Escape($file.Name)

    # One file per component, no explicit GUID: WiX derives a stable one from
    # the key path, and a component that holds exactly one file is the shape
    # the installer's own servicing rules are written for.
    [void]$xml.AppendLine("      <Component Id=`"$componentId`" Directory=`"$directoryId`">")
    [void]$xml.AppendLine("        <File Id=`"$fileId`" Name=`"$name`" Source=`"$source`" KeyPath=`"yes`" />")
    [void]$xml.AppendLine("      </Component>")
}

[void]$xml.AppendLine('    </ComponentGroup>')
[void]$xml.AppendLine('  </Fragment>')
[void]$xml.AppendLine('</Wix>')

$generated = Join-Path $installerDirectory "Files.generated.wxs"
[System.IO.File]::WriteAllText($generated, $xml.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Dateiliste geschrieben: $generated"

# ------------------------------------------------------------ build MSI

# The standard dialogue set lives in an extension that is not part of the base
# tool. Pinned rather than floating: a dialog set that changes underneath a
# release is a difference nobody sees until somebody runs the installer.
Write-Host "WiX-Erweiterung sicherstellen: $UiExtension"
& $WixCommand extension add -g $UiExtension | Out-Null

$arguments = @(
    "build"
    (Join-Path $installerDirectory "ELW-Meteo.wxs")
    $generated
    "-arch", "x64"
    "-ext", "WixToolset.UI.wixext"
    # Without this the package would be declared German (Language 1031) and then
    # show English dialogs — WiX falls back to en-us when no culture is named.
    "-culture", "de-de"
    "-d", "Version=$msiVersion"
    "-d", "HintFile=$(Join-Path $installerDirectory 'HINWEIS-WebView2.txt')"
    "-d", "LicenseFile=$(Join-Path $installerDirectory 'Lizenz.rtf')"
    "-o", $OutputPath
)

Write-Host "wix $($arguments -join ' ')"
& $WixCommand @arguments

if ($LASTEXITCODE -ne 0) {
    throw "wix build ist mit Code $LASTEXITCODE fehlgeschlagen."
}

Write-Host "Paket gebaut: $OutputPath"
