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

function Get-UninstallEntries {
    <#
        Every place „Programme und Features" reads from, as objects carrying the
        hive and the InstallLocation the entry advertises.

        The hive is reported but deliberately not asserted on. Which one Windows
        files a per-user installation under depends on whether the installing
        process was elevated — on a build runner it is, on a vehicle laptop it
        is not — and neither answer is a defect in the package.

        InstallLocation is the thing worth checking, and it is the direct
        measurement of the failure that started all this: an entry that offers
        to uninstall a folder other than the one the files are actually in.
        Inferring that from the hive was a detour around the question.
    #>
    $roots = @(
        @{ Name = "HKCU";         Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKCU (Wow64)"; Path = "HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKLM";         Path = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall" }
        @{ Name = "HKLM (Wow64)"; Path = "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }
    )

    $found = @()

    foreach ($root in $roots) {
        foreach ($key in (Get-ChildItem $root.Path -ErrorAction SilentlyContinue)) {
            if ($key.GetValue("DisplayName") -eq "ELW-Meteo") {
                $found += [PSCustomObject]@{
                    Hive            = $root.Name
                    InstallLocation = $key.GetValue("InstallLocation")
                }
            }
        }
    }

    return @($found)
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

$entries = Get-UninstallEntries
Assert-That "Eintrag in Programme und Features" ($entries.Count -eq 1)

if ($entries.Count -ge 1) {
    $entry = $entries[0]
    Write-Host "        Eintrag in $($entry.Hive), InstallLocation=$($entry.InstallLocation)"

    # The invariant the whole installer story turned on: what the entry offers
    # to uninstall has to be where the files actually are.
    Assert-That "Eintrag verweist auf $root (ist: $($entry.InstallLocation))" (
        $entry.InstallLocation -and $entry.InstallLocation.TrimEnd('\') -eq $root.TrimEnd('\'))
}

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
Assert-That "Eintrag aus Programme und Features entfernt" ((Get-UninstallEntries).Count -eq 0)

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

# Two entries would mean MajorUpgrade did not recognise the previous copy —
# the classic result being an application that cannot be fully uninstalled.
$afterUpgrade = Get-UninstallEntries
Assert-That "Nur ein Eintrag in Programme und Features (sind: $($afterUpgrade.Count))" (
    $afterUpgrade.Count -eq 1)

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
