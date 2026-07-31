<#
.SYNOPSIS
    Installiert und deinstalliert das MSI-Paket und prüft das Ergebnis.

.DESCRIPTION
    Runs the package for real and checks what it left behind.

    This exists because an installer is the one artefact whose defects only
    appear on somebody else's machine. It builds without complaint, and then a
    vehicle laptop ends up with a folder in the wrong place, a shortcut that
    points nowhere, or — worst — an uninstall that takes the vehicle's settings
    with it. None of that is visible in the .wxs.

    Run on a machine you do not mind installing to. On CI that is a fresh
    runner; locally, read what it does first.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [string] $LogDirectory = "installer-logs"
)

$ErrorActionPreference = "Stop"

$msi = (Resolve-Path $MsiPath).Path
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Msi {
    param([string[]] $Arguments, [string] $LogName)

    $log = Join-Path $LogDirectory $LogName
    $all = $Arguments + @("/qn", "/norestart", "/l*v", $log)

    Write-Host "  msiexec $($all -join ' ')"
    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $all -Wait -PassThru

    # 3010 is "success, reboot queued" and is not a failure.
    if ($process.ExitCode -notin @(0, 3010)) {
        Write-Host "::group::$LogName"
        Get-Content $log -Tail 60 | Write-Host
        Write-Host "::endgroup::"
        throw "msiexec endete mit Code $($process.ExitCode) — siehe $log"
    }
}

function Assert-That {
    param([string] $What, [bool] $Condition)

    if ($Condition) {
        Write-Host "  ok    $What"
    } else {
        Write-Host "  FEHLT $What"
        $failures.Add($What)
    }
}

function Find-UninstallEntry {
    <#
        Where „Programme und Features" lists the application, or $null.

        All four locations are searched and the hit is printed, because which
        one it lands in is the interesting part rather than an implementation
        detail: a per-user file layout registered under HKLM would mean every
        user on the machine is offered an uninstall for files that live in one
        person's profile. Asserting only against the hive I expected would have
        told me it was missing, not where it went.
    #>
    $roots = @(
        @{ Name = "HKCU";        Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKCU (Wow64)"; Path = "HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKLM";        Path = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKLM (Wow64)"; Path = "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }
    )

    foreach ($root in $roots) {
        $hit = Get-ChildItem $root.Path -ErrorAction SilentlyContinue |
            Where-Object { $_.GetValue("DisplayName") -eq "ELW-Meteo" } |
            Select-Object -First 1

        if ($hit) {
            Write-Host "        Eintrag gefunden in $($root.Name), InstallLocation=$($hit.GetValue('InstallLocation'))"
            return $root.Name
        }
    }

    return $null
}

# ------------------------------------------------------- settings survive
#
# Put something in the settings folder first. Losing it on uninstall is the
# failure worth testing for: the vehicle configuration is the part that took
# somebody an afternoon.

$settingsDirectory = Join-Path $env:APPDATA "ELW-Meteo"
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
$marker = Join-Path $settingsDirectory "settings.json"
Set-Content -Path $marker -Value '{"HomeName":"Testwache"}' -Encoding UTF8

$root = Join-Path $env:LOCALAPPDATA "Programs\ELW-Meteo"
$exe = Join-Path $root "ELW-Meteo.exe"
$machineRoot = Join-Path $env:ProgramFiles "ELW-Meteo"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\ELW-Meteo.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "ELW-Meteo.lnk"

# =============================================================== install

Write-Host "`n=== Installation ==="
Invoke-Msi -Arguments @("/i", $msi) -LogName "install.log"

Assert-That "Anwendung unter $root" (Test-Path $exe)
Assert-That "Assets mitinstalliert" (Test-Path (Join-Path $root "Assets\map.html"))
Assert-That "Fachlogik mitinstalliert" (Test-Path (Join-Path $root "ElwMeteo.Core.dll"))
Assert-That "Startmenü-Verknüpfung" (Test-Path $startMenu)
Assert-That "Desktop-Verknüpfung" (Test-Path $desktop)

# The package is per user, fixed. It must land in the user's profile even when
# it is run elevated — an installer that quietly does something other than what
# its name says is the defect three earlier attempts shipped.
Assert-That "Nichts unter Programme abgelegt" (-not (Test-Path (Join-Path $machineRoot "ELW-Meteo.exe")))

$entry = Find-UninstallEntry
Assert-That "Eintrag in Programme und Features" ($null -ne $entry)
Assert-That "Eintrag im Benutzerzweig (ist: $entry)" ($entry -like "HKCU*")

# The shortcut has to point at the executable that was actually installed —
# a shortcut to a path that does not exist is the classic silent installer bug.
if (Test-Path $startMenu) {
    $shell = New-Object -ComObject WScript.Shell
    $target = $shell.CreateShortcut($startMenu).TargetPath
    Assert-That "Verknüpfung zeigt auf $exe (ist: $target)" ($target -eq $exe)
}

Write-Host "`n=== Deinstallation ==="
Invoke-Msi -Arguments @("/x", $msi) -LogName "uninstall.log"

Assert-That "Programmdateien entfernt" (-not (Test-Path $exe))
Assert-That "Startmenü-Verknüpfung entfernt" (-not (Test-Path $startMenu))
Assert-That "Desktop-Verknüpfung entfernt" (-not (Test-Path $desktop))
Assert-That "Eintrag aus Programme und Features entfernt" ($null -eq (Find-UninstallEntry))

# The one thing that must survive: the vehicle configuration is the part that
# took somebody an afternoon, and an update cycle that discards it is one
# nobody runs twice.
Assert-That "Einstellungen bleiben erhalten" (Test-Path $marker)

# ================================================== desktop shortcut off

Write-Host "`n=== Installation ohne Desktop-Verknüpfung ==="
Invoke-Msi -Arguments @("/i", $msi, "INSTALLDESKTOPSHORTCUT=0") -LogName "nodesktop-install.log"

Assert-That "Anwendung installiert" (Test-Path $exe)
Assert-That "Keine Desktop-Verknüpfung" (-not (Test-Path $desktop))

Invoke-Msi -Arguments @("/x", $msi) -LogName "nodesktop-uninstall.log"
Assert-That "Restlos entfernt" (-not (Test-Path $root))

# ==================================================== repeated installation
#
# Installing over an existing copy is the normal case once an update exists,
# and MajorUpgrade is the part of the package that has never been exercised.

Write-Host "`n=== Erneute Installation über eine vorhandene ==="
Invoke-Msi -Arguments @("/i", $msi) -LogName "reinstall-first.log"
Invoke-Msi -Arguments @("/i", $msi) -LogName "reinstall-second.log"

Assert-That "Anwendung weiterhin vorhanden" (Test-Path $exe)
Assert-That "Nur ein Eintrag in Programme und Features" (
    @(Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue("DisplayName") -eq "ELW-Meteo" }).Count -eq 1)

Invoke-Msi -Arguments @("/x", $msi) -LogName "reinstall-uninstall.log"
Assert-That "Restlos entfernt" (-not (Test-Path $root))

# ------------------------------------------------------------------ result

Remove-Item $marker -ErrorAction SilentlyContinue

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) Prüfung(en) fehlgeschlagen:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "`nAlle Prüfungen bestanden."
