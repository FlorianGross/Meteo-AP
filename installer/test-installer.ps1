<#
.SYNOPSIS
    Installiert und deinstalliert das MSI-Paket und prüft das Ergebnis.

.DESCRIPTION
    Runs the package for real, in both scopes, and checks what it left behind.

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

# ------------------------------------------------------- settings survive
#
# Put something in the settings folder first. Losing it on uninstall is the
# failure worth testing for: the vehicle configuration is the part that took
# somebody an afternoon.

$settingsDirectory = Join-Path $env:APPDATA "ELW-Meteo"
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
$marker = Join-Path $settingsDirectory "settings.json"
Set-Content -Path $marker -Value '{"HomeName":"Testwache"}' -Encoding UTF8

# =============================================================== per user

Write-Host "`n=== Installation pro Benutzer (Voreinstellung) ==="
Invoke-Msi -Arguments @("/i", $msi) -LogName "peruser-install.log"

$userRoot = Join-Path $env:LOCALAPPDATA "Programs\ELW-Meteo"
$userExe = Join-Path $userRoot "ELW-Meteo.exe"

Assert-That "Anwendung unter $userRoot" (Test-Path $userExe)
Assert-That "Assets mitinstalliert" (Test-Path (Join-Path $userRoot "Assets\map.html"))
Assert-That "Fachlogik mitinstalliert" (Test-Path (Join-Path $userRoot "ElwMeteo.Core.dll"))

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\ELW-Meteo.lnk"
Assert-That "Startmenü-Verknüpfung" (Test-Path $startMenu)

$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "ELW-Meteo.lnk"
Assert-That "Desktop-Verknüpfung" (Test-Path $desktop)

# The shortcut has to point at the executable that was actually installed —
# a shortcut to a path that does not exist is the classic silent installer bug.
if (Test-Path $startMenu) {
    $shell = New-Object -ComObject WScript.Shell
    $target = $shell.CreateShortcut($startMenu).TargetPath
    Assert-That "Verknüpfung zeigt auf $userExe (ist: $target)" ($target -eq $userExe)
}

Assert-That "Eintrag in Programme und Features" (
    @(Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue("DisplayName") -eq "ELW-Meteo" }).Count -gt 0)

Write-Host "`n=== Deinstallation pro Benutzer ==="
Invoke-Msi -Arguments @("/x", $msi) -LogName "peruser-uninstall.log"

Assert-That "Programmdateien entfernt" (-not (Test-Path $userExe))
Assert-That "Startmenü-Verknüpfung entfernt" (-not (Test-Path $startMenu))
Assert-That "Einstellungen bleiben erhalten" (Test-Path $marker)

# ============================================================ per machine

Write-Host "`n=== Installation pro Rechner ==="
Invoke-Msi -Arguments @("/i", $msi, "ALLUSERS=1", 'MSIINSTALLPERUSER=""') -LogName "permachine-install.log"

$machineRoot = Join-Path $env:ProgramFiles "ELW-Meteo"
$machineExe = Join-Path $machineRoot "ELW-Meteo.exe"

Assert-That "Anwendung unter $machineRoot" (Test-Path $machineExe)
Assert-That "Assets mitinstalliert" (Test-Path (Join-Path $machineRoot "Assets\map.html"))

$allUsersStart = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\ELW-Meteo.lnk"
Assert-That "Startmenü-Verknüpfung für alle Benutzer" (Test-Path $allUsersStart)

Assert-That "Eintrag in Programme und Features" (
    @(Get-ChildItem "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue("DisplayName") -eq "ELW-Meteo" }).Count -gt 0)

Write-Host "`n=== Deinstallation pro Rechner ==="
Invoke-Msi -Arguments @("/x", $msi) -LogName "permachine-uninstall.log"

Assert-That "Programmdateien entfernt" (-not (Test-Path $machineExe))
Assert-That "Einstellungen bleiben erhalten" (Test-Path $marker)

# ================================================== desktop shortcut off

Write-Host "`n=== Installation ohne Desktop-Verknüpfung ==="
Invoke-Msi -Arguments @("/i", $msi, "INSTALLDESKTOPSHORTCUT=0") -LogName "nodesktop-install.log"

Assert-That "Anwendung installiert" (Test-Path $userExe)
Assert-That "Keine Desktop-Verknüpfung" (-not (Test-Path $desktop))

Invoke-Msi -Arguments @("/x", $msi) -LogName "nodesktop-uninstall.log"
Assert-That "Restlos entfernt" (-not (Test-Path $userRoot))

# ------------------------------------------------------------------ result

Remove-Item $marker -ErrorAction SilentlyContinue

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) Prüfung(en) fehlgeschlagen:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "`nAlle Prüfungen bestanden."
