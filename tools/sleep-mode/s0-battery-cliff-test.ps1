<#
    Battery-cliff probe (standalone). Answers ONE question: on a Modern Standby laptop
    ON BATTERY, can the documented "power requests terminated 5 min after the sleep
    timeout" cliff be defeated by either
      (a) ONE-SHOT pings   - SetThreadExecutionState(ES_SYSTEM_REQUIRED) WITHOUT
                             ES_CONTINUOUS every 5 s (each ping = a single idle-timer
                             reset, not a persistent power request), or
      (b) CYCLED claims    - release the persistent claim and immediately re-claim it
                             every 5 s (a genuinely fresh power request each cycle).
    Each variant runs 10 minutes with both timeouts shortened to 2 min (restored
    afterwards) and a lock-free child process observing whether OTHER apps survive too.

    USAGE (on battery!):
        powershell -NoProfile -ExecutionPolicy Bypass -File .\s0-battery-cliff-test.ps1
    Variant (a) always runs; variant (b) is offered afterwards (10 more minutes).
    Report: s0-cliff-report.txt next to this script.
#>
$ErrorActionPreference = 'Continue'

# self-verification banner - compare against the values published with this file
$self = Get-Item -LiteralPath $MyInvocation.MyCommand.Path
$hash = (Get-FileHash -LiteralPath $self.FullName -Algorithm SHA256).Hash
Write-Host ('s0-battery-cliff-test: {0} bytes, sha256 {1}' -f $self.Length, $hash.Substring(0, 16))

$script:Dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:Report = Join-Path $script:Dir 's0-cliff-report.txt'
$script:RestoreFile = Join-Path $script:Dir 's0-cliff-restore.txt'

$GUID_SUB_BUTTONS = '4f971e89-eebd-4455-a8de-9e59040e7347'
$GUID_LIDACTION   = '5ca83367-6e45-459f-a27b-476b1d01c936'
$GUID_SUB_SLEEP   = '238c9fa8-0aad-41ed-83f4-97be242c8f20'
$GUID_STANDBYIDLE = '29f6c1db-86da-48c5-9fdb-f2b67b1f44da'
$GUID_SUB_VIDEO   = '7516b95f-f776-4464-8c53-06167f40cc99'
$GUID_VIDEOIDLE   = '3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e'

# SetThreadExecutionState flags as decimal strings (PS parses 0x80000000+ hex as negative Int32)
$ES_RELEASE     = [uint32]'2147483648'   # lone ES_CONTINUOUS (release)
$ES_HOLD_SYSTEM = [uint32]'2147483649'   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
$ES_PING_SYSTEM = [uint32]'1'            # ES_SYSTEM_REQUIRED alone (one-shot idle-timer reset)

$src = @'
using System;
using System.Runtime.InteropServices;

public static class CliffProbe
{
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, IntPtr policyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, uint value);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static IntPtr GuidPtr(string guidText)
    {
        byte[] raw = new Guid(guidText).ToByteArray();
        IntPtr p = Marshal.AllocHGlobal(16);
        Marshal.Copy(raw, 0, p, 16);
        return p;
    }

    public static string GetScheme()
    {
        IntPtr p;
        if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0)
            return null;
        try
        {
            Guid g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            return g.ToString();
        }
        finally
        {
            LocalFree(p);
        }
    }

    public static uint ApplyScheme(string schemeGuid)
    {
        IntPtr p = GuidPtr(schemeGuid);
        try { return PowerSetActiveScheme(IntPtr.Zero, p); }
        finally { Marshal.FreeHGlobal(p); }
    }

    public static uint[] ReadValue(string schemeGuid, string groupGuid, string settingGuid, bool ac)
    {
        IntPtr scheme = GuidPtr(schemeGuid);
        IntPtr group = GuidPtr(groupGuid);
        IntPtr setting = GuidPtr(settingGuid);
        try
        {
            uint value;
            uint status = ac
                ? PowerReadACValueIndex(IntPtr.Zero, scheme, group, setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, scheme, group, setting, out value);
            return new uint[] { status, value };
        }
        finally
        {
            Marshal.FreeHGlobal(scheme);
            Marshal.FreeHGlobal(group);
            Marshal.FreeHGlobal(setting);
        }
    }

    public static uint WriteValue(string schemeGuid, string groupGuid, string settingGuid, uint value, bool ac)
    {
        IntPtr scheme = GuidPtr(schemeGuid);
        IntPtr group = GuidPtr(groupGuid);
        IntPtr setting = GuidPtr(settingGuid);
        try
        {
            return ac
                ? PowerWriteACValueIndex(IntPtr.Zero, scheme, group, setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, scheme, group, setting, value);
        }
        finally
        {
            Marshal.FreeHGlobal(scheme);
            Marshal.FreeHGlobal(group);
            Marshal.FreeHGlobal(setting);
        }
    }

    public static uint ExecutionState(uint flags)
    {
        return SetThreadExecutionState(flags);
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

function Log([string]$text)
{
    $stamp = Get-Date -Format 'HH:mm:ss'
    foreach ($line in ($text -split '\r?\n'))
    {
        $entry = $line
        if ($line.Trim().Length -gt 0) { $entry = '{0} {1}' -f $stamp, $line }
        Add-Content -Path $script:Report -Value $entry -Encoding UTF8
        Write-Host $entry
    }
}

function Save-RestoreSnapshot([string]$scheme)
{
    if (Test-Path $script:RestoreFile) { return }
    $slots = @(
        @{ k = 'lidAc';     g = $GUID_SUB_BUTTONS; s = $GUID_LIDACTION;   ac = $true  },
        @{ k = 'lidDc';     g = $GUID_SUB_BUTTONS; s = $GUID_LIDACTION;   ac = $false },
        @{ k = 'standbyAc'; g = $GUID_SUB_SLEEP;   s = $GUID_STANDBYIDLE; ac = $true  },
        @{ k = 'standbyDc'; g = $GUID_SUB_SLEEP;   s = $GUID_STANDBYIDLE; ac = $false },
        @{ k = 'videoAc';   g = $GUID_SUB_VIDEO;   s = $GUID_VIDEOIDLE;   ac = $true  },
        @{ k = 'videoDc';   g = $GUID_SUB_VIDEO;   s = $GUID_VIDEOIDLE;   ac = $false }
    )
    $lines = @('# values saved by s0-battery-cliff-test.ps1; reapplied if a run was interrupted')
    foreach ($slot in $slots)
    {
        $res = [CliffProbe]::ReadValue($scheme, $slot.g, $slot.s, $slot.ac)
        if ($res[0] -eq 0) { $lines += ('{0}={1}' -f $slot.k, $res[1]) }
    }
    $lines | Set-Content -Path $script:RestoreFile -Encoding ASCII
}

function Restore-And-Clean([string]$scheme)
{
    if (-not (Test-Path $script:RestoreFile)) { return }
    Log 'RESTORE (safety net: re-applying saved original values)'
    foreach ($line in (Get-Content $script:RestoreFile))
    {
        if ($line -like '#*') { continue }
        $parts = $line -split '=', 2
        if ($parts.Count -ne 2) { continue }
        $key = $parts[0]; $val = [uint32]$parts[1]
        $group = $(if ($key -like 'lid*') { $GUID_SUB_BUTTONS } elseif ($key -like 'video*') { $GUID_SUB_VIDEO } else { $GUID_SUB_SLEEP })
        $setting = $(if ($key -like 'lid*') { $GUID_LIDACTION } elseif ($key -like 'video*') { $GUID_VIDEOIDLE } else { $GUID_STANDBYIDLE })
        [void][CliffProbe]::WriteValue($scheme, $group, $setting, $val, ($key -like '*Ac'))
    }
    [void][CliffProbe]::ApplyScheme($scheme)
    Remove-Item $script:RestoreFile -Force
    Log 'restore snapshot deleted.'
}

function Run-Variant([string]$title, [string]$mode)
{
    Log ''
    Log ('======== VARIANT: {0} ========' -f $title)
    $scheme = [CliffProbe]::GetScheme()
    if ($scheme -eq $null) { Log 'no active scheme - skip'; return }

    Add-Type -AssemblyName System.Windows.Forms
    $powerStatus = [System.Windows.Forms.SystemInformation]::PowerStatus
    $onAc = ($powerStatus.PowerLineStatus -eq 'Online')
    Log ('current power source: {0} - NOTE: this question is battery-specific; results on AC are not informative about the cliff.' -f $(if ($onAc) { 'AC (!)' } else { 'BATTERY' }))

    Save-RestoreSnapshot $scheme
    $origStandby = [CliffProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc)
    $origVideo = [CliffProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc)
    Log ('original standby timeout: {0}s; display-off timeout: {1}s' -f $origStandby[1], $origVideo[1])

    $childPs1 = Join-Path $script:Dir 's0-child-heartbeat.ps1'
    $childLog = Join-Path $script:Dir 's0-child-heartbeat.txt'
    Set-Content -Path $childPs1 -Encoding ASCII -Value @'
param([string]$logPath)
for ($i = 0; $i -lt 140; $i++)
{
    Add-Content -LiteralPath $logPath -Value ('{0:HH:mm:ss.fff} heartbeat' -f (Get-Date))
    Start-Sleep -Seconds 5
}
'@
    Remove-Item $childLog -Force -ErrorAction SilentlyContinue
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $childPs1, $childLog) -WindowStyle Hidden -PassThru
    Log ('lock-free child process started (pid {0})' -f $child.Id)

    $t0 = Get-Date
    $tailMark = $null
    $ticks = New-Object System.Collections.Generic.List[datetime]
    try
    {
        [void][CliffProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc)
        [void][CliffProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc)
        [void][CliffProbe]::ApplyScheme($scheme)
        Log 'standby AND display-off timeouts set to 120s (restored afterwards).'
        if ($mode -eq 'ping')
        {
            Log 'parent sends ONE-SHOT ES_SYSTEM_REQUIRED pings every 5 s (NO persistent claim); child holds nothing.'
        }
        else
        {
            Log 'parent CYCLES the persistent claim every 5 s (release + fresh re-claim); child holds nothing.'
        }
        Log 'DO NOT touch the machine. Screen turns off ~2 min in. Everything must keep running for the FULL 10 min (past the ~7 min mark) to PASS.'

        $deadline = (Get-Date).AddSeconds(600)
        while ((Get-Date) -lt $deadline)
        {
            $now = Get-Date
            if ($mode -eq 'ping')
            {
                [void][CliffProbe]::ExecutionState($ES_PING_SYSTEM)
            }
            else
            {
                [void][CliffProbe]::ExecutionState($ES_RELEASE)
                [void][CliffProbe]::ExecutionState($ES_HOLD_SYSTEM)
            }
            $ticks.Add($now)
            Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat (parent)' -f $now) -Encoding UTF8
            Start-Sleep -Seconds 5
        }
    }
    catch
    {
        Log ('VARIANT FAILED: {0}' -f $_.Exception.ToString())
    }
    finally
    {
        $tailMark = Get-Date
        [void][CliffProbe]::ExecutionState($ES_RELEASE)
        Log 'parent stopped.'
        if ($origStandby[0] -eq 0)
        {
            [void][CliffProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $origStandby[1], $onAc)
            Log ('standby timeout restored to {0}s.' -f $origStandby[1])
        }
        if ($origVideo[0] -eq 0)
        {
            [void][CliffProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo[1], $onAc)
            Log ('display-off timeout restored to {0}s.' -f $origVideo[1])
        }
        [void][CliffProbe]::ApplyScheme($scheme)
        if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue }
        $child.WaitForExit()
        Remove-Item $childPs1, $script:RestoreFile -Force -ErrorAction SilentlyContinue
    }

    $maxGap = 0; $gapAt = $null
    for ($i = 1; $i -lt $ticks.Count; $i++)
    {
        $gap = ($ticks[$i] - ($ticks[$i - 1])).TotalSeconds
        if ($gap -gt $maxGap) { $maxGap = $gap; $gapAt = $ticks[$i] }
    }
    if (($ticks.Count -gt 0) -and ($null -ne $tailMark))
    {
        $tailGap = ($tailMark - $ticks[$ticks.Count - 1]).TotalSeconds
        if ($tailGap -gt 30) { Log ('NOTE: tail gap of {0:N0}s after the last tick - the parent was suspended at phase end (counted into the verdict).' -f $tailGap) }
        if ($tailGap -gt $maxGap) { $maxGap = $tailGap; $gapAt = $ticks[$ticks.Count - 1] }
    }
    Log ('PARENT: largest heartbeat gap {0:N0}s at {1:HH:mm:ss}' -f $maxGap, $gapAt)

    $childTicks = New-Object System.Collections.Generic.List[datetime]
    if (Test-Path $childLog)
    {
        foreach ($line in (Get-Content $childLog))
        {
            $stamp = ($line -split ' ')[0]
            $parsed = [datetime]::MinValue
            if ([datetime]::TryParseExact($stamp, 'HH:mm:ss.fff', $null, [System.Globalization.DateTimeStyles]::None, [ref]$parsed))
            { $childTicks.Add($parsed) }
        }
        Remove-Item $childLog -Force -ErrorAction SilentlyContinue
    }
    $maxChildGap = -1
    if ($childTicks.Count -ge 2)
    {
        $maxChildGap = 0; $gapAtC = $null
        for ($i = 1; $i -lt $childTicks.Count; $i++)
        {
            $gap = ($childTicks[$i] - ($childTicks[$i - 1])).TotalSeconds
            if ($gap -gt $maxChildGap) { $maxChildGap = $gap; $gapAtC = $childTicks[$i] }
        }
        Log ('CHILD (lock-free, no request): {0} lines, largest gap {1:N0}s at {2:HH:mm:ss}' -f $childTicks.Count, $maxChildGap, $gapAtC)
    }
    else
    {
        Log 'CHILD (lock-free): no usable heartbeat lines - inconclusive.'
    }

    if (($maxGap -le 30) -and ($maxChildGap -ge 0 -and $maxChildGap -le 30))
    {
        Log 'VERDICT: PASS - parent AND child alive for the full 10 min: this variant HOLDS the whole system awake past the cliff.'
    }
    elseif ($maxGap -le 30)
    {
        Log 'VERDICT: MIXED - parent alive but child suspended; unexpected, inspect the report/events.'
    }
    elseif ($maxGap -gt 30)
    {
        Log 'VERDICT: FAIL - the parent itself slept despite this variant; the cliff stands.'
    }
}

# start
Log ('======== s0-battery-cliff-test ======== {0:yyyy-MM-dd HH:mm:ss} ========' -f (Get-Date))
$scheme0 = [CliffProbe]::GetScheme()
if ($scheme0 -ne $null) { Restore-And-Clean $scheme0 }

Add-Type -AssemblyName System.Windows.Forms
$ps0 = [System.Windows.Forms.SystemInformation]::PowerStatus
if ($ps0.PowerLineStatus -eq 'Online')
{
    Write-Host ''
    Write-Host 'WARNING: you are on AC. The cliff is a battery-only phenomenon - unplug first for meaningful results.' -ForegroundColor Yellow
    Write-Host ''
}

$proceed = Read-Host 'Start variant (a) now? Answer N to ONLY restore the previously saved settings and exit [Y/n]'
if ($proceed -notmatch '^[Nn]')
{
    Run-Variant 'one-shot pings (ES_SYSTEM_REQUIRED without ES_CONTINUOUS, every 5 s)' 'ping'

    $again = Read-Host 'Run the second variant too - cycled persistent claims (release + re-claim every 5 s, 10 more min)? [y/N]'
    if ($again -match '^[Yy]')
    {
        Run-Variant 'cycled persistent claims (release + fresh re-claim every 5 s)' 'cycle'
    }
}

$schemeEnd = [CliffProbe]::GetScheme()
if ($schemeEnd -ne $null) { Restore-And-Clean $schemeEnd }
Log 'DONE.'
Write-Host ''
Write-Host ('Report: {0}' -f $script:Report)
