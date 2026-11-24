param(
    [Parameter(Mandatory)][ValidateSet('Start','Submit','Status','Pause','Resume','Cancel','Stop')][string]$Action,
    [string]$StateRoot = 'D:/STS2Modding/RolltheSpire2/.build-evidence/experiment-desk',
    [string]$Godot,
    [ValidateSet('vulkan','d3d12')][string]$Driver = 'vulkan',
    [string]$ReleaseBinaryDirectory,
    [string]$Owner,
    [string]$ExpectedModSha256,
    [string]$Context,
    [string]$Workload,
    [int]$Warmup = 0,
    [int]$Repeat = 1,
    [int]$TimeoutSeconds = 300,
    [switch]$CpuOnly,
    [string]$JobId
)
$ErrorActionPreference = 'Stop'
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($StateRoot)
$statusPath = Join-Path $root 'status.json'
function Write-Atomic([string]$Path, $Value) {
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($tmp, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($tmp, $Path, $true)
}
function Read-LiveStatus {
    if (-not (Test-Path -LiteralPath $statusPath)) { throw 'Desk is not started.' }
    $s = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    if ($s.state -eq 'stopped' -or -not (Get-Process -Id $s.pid -ErrorAction SilentlyContinue) -or
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]$s.heartbeatUtc).TotalSeconds -gt 30) {
        throw 'Desk is stopped/unresponsive; inspect runtime logs before restarting. No job was submitted.'
    }
    return $s
}
switch ($Action) {
    'Start' {
        if (-not $Godot -or -not $ReleaseBinaryDirectory) { throw 'Start requires -Godot and an already-built -ReleaseBinaryDirectory. Build ProductionSearchHost Release with -p:Rt2ModsDir= first.' }
        $gate = [Threading.Mutex]::new($false, 'Local\RT2_ExperimentDesk')
        $got = $false
        try {
            try { $got = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $got = $true }
            if (-not $got) { throw 'A desk is already running. Use Status or Stop; do not start another window.' }
        } finally { if ($got) { $gate.ReleaseMutex() }; $gate.Dispose() }
        $godotPath = (Resolve-Path -LiteralPath $Godot).Path
        $binaries = (Resolve-Path -LiteralPath $ReleaseBinaryDirectory).Path
        foreach ($name in 'ProductionSearchHost.dll','RolltheSpire2.dll') {
            if (-not (Test-Path -LiteralPath (Join-Path $binaries $name))) { throw "Missing $name" }
        }
        $instance = Join-Path $root ('instances/' + [guid]::NewGuid().ToString('N'))
        $load = Join-Path $instance '.godot/mono/temp/bin/Debug'
        New-Item -ItemType Directory -Path $load -Force | Out-Null
        # An immutable runtime copy prevents another chat's build overwriting the
        # loaded candidate. Changing DLLs requires one explicit desk restart.
        Get-ChildItem -LiteralPath $binaries | Copy-Item -Destination $load -Recurse
        foreach ($file in 'project.godot','Resident.tscn','ResidentMain.cs','Main.cs') {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $instance
        }
        $settings = Get-Content -LiteralPath (Join-Path $instance 'project.godot') -Raw
        $settings = $settings.Replace('res://Main.tscn', 'res://Resident.tscn')
        $settings = $settings.Replace('RolltheSpire2 Production Replay', 'RT2 Experiment Desk')
        [IO.File]::WriteAllText((Join-Path $instance 'project.godot'), $settings, [Text.UTF8Encoding]::new($false))
        function Q([string]$v) { if ($v.Contains('"')) { throw 'Quote in path is unsupported' }; return '"' + $v.TrimEnd('\') + '"' }
        $argsList = @('--path',(Q $instance),'--rendering-method','mobile','--rendering-driver',$Driver,
            '--resolution','720x250','--','--resident-root',(Q $root))
        $previousSelection = $env:RT2_PRIVATE_ORDINAL_SELECTION
        try {
            $env:RT2_PRIVATE_ORDINAL_SELECTION = Join-Path $instance 'private-ordinal-selection.json'
            $proc = Start-Process -FilePath $godotPath -ArgumentList $argsList -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $instance 'runtime.log') -RedirectStandardError (Join-Path $instance 'stderr.log')
        } finally { $env:RT2_PRIVATE_ORDINAL_SELECTION = $previousSelection }
        try {
            $ready = $false
            for ($i=0; $i -lt 80; $i++) {
                if ($proc.HasExited) { throw "Desk exited ($($proc.ExitCode)); inspect $instance/stderr.log" }
                if (Test-Path -LiteralPath $statusPath) {
                    $s = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
                    # Godot's console executable may launch the real GUI process.
                    $child = if ($s.pid -ne $proc.Id) { Get-CimInstance Win32_Process -Filter "ProcessId = $($s.pid)" } else { $null }
                    if ($s.pid -eq $proc.Id -or ($null -ne $child -and $child.ParentProcessId -eq $proc.Id)) { $ready = $true; break }
                }
                Start-Sleep -Milliseconds 250
            }
            if (-not $ready) { throw "Desk not ready yet; process $($proc.Id) retained. Inspect $instance before retrying." }
            Write-Atomic (Join-Path $root 'instance.json') @{ pid=$s.pid; launcherPid=$proc.Id; directory=$instance; driver=$Driver }
            $s | ConvertTo-Json
        } finally { $proc.Dispose() }
    }
    'Submit' {
        $s = Read-LiveStatus
        if ($s.state -in 'faulted-restart-required','stopped') { throw 'Desk needs restart.' }
        if (-not $Owner -or -not $Context -or -not $Workload -or $ExpectedModSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            throw 'Submit requires Owner, Context, Workload and the intended candidate ExpectedModSha256.'
        }
        if ($s.modSha256 -ne $ExpectedModSha256) { throw 'Candidate mismatch; no job submitted.' }
        if ($Warmup -lt 0 -or $Warmup -gt 100 -or $Repeat -lt 1 -or $Repeat -gt 1000 -or $TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) { throw 'Invalid run limits.' }
        $id = [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmssfffffff') + '-' + [guid]::NewGuid().ToString('N')
        $inputRoot = Join-Path $root "inputs/$id"
        New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null
        Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Context).Path -Destination (Join-Path $inputRoot 'context.json')
        Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Workload).Path -Destination (Join-Path $inputRoot 'workload.json')
        $job = @{ Id=$id; Owner=$Owner; ExpectedModSha256=$ExpectedModSha256; Context=(Join-Path $inputRoot 'context.json');
            Workload=(Join-Path $inputRoot 'workload.json'); Warmup=$Warmup; Repeat=$Repeat; TimeoutSeconds=$TimeoutSeconds; CpuOnly=[bool]$CpuOnly }
        Write-Atomic (Join-Path $root "queue/$id.json") $job
        @{ id=$id; state='queued'; result=(Join-Path $root "results/$id.json") } | ConvertTo-Json
    }
    'Status' {
        if ($JobId) {
            if ($JobId -notmatch '^[A-Za-z0-9-]{1,100}$') { throw 'Invalid JobId' }
            $result = Join-Path $root "results/$JobId.json"
            if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw }
            else { Read-LiveStatus | ConvertTo-Json }
        } else { Read-LiveStatus | ConvertTo-Json }
    }
    'Pause' { $null=Read-LiveStatus; [IO.File]::WriteAllText((Join-Path $root 'pause'), 'paused') }
    'Resume' { $null=Read-LiveStatus; Remove-Item -LiteralPath (Join-Path $root 'pause') -ErrorAction SilentlyContinue }
    'Cancel' {
        $s=Read-LiveStatus
        if (-not $JobId -or $s.activeJob -ne $JobId) { throw 'Cancel requires the current active JobId; no other job will be cancelled.' }
        [IO.File]::WriteAllText((Join-Path $root 'cancel'), $JobId)
    }
    'Stop' { $null=Read-LiveStatus; [IO.File]::WriteAllText((Join-Path $root 'stop'), 'stop') }
}
