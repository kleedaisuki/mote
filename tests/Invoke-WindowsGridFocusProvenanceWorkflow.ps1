# Retain one independently supervised UIA attempt on a disposable hosted runner.
param([Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_OS -cne 'Windows' -or
    $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { throw 'Hosted Windows runner required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier/grid-focus-provenance"

# All artifacts are fresh and retained; existing ancestors must not redirect writes.
function Assert-OwnedPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $cache = Join-Path $root '.cache'
    if (-not $full.StartsWith($cache + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Artifact must be below checkout .cache.'
    }
    $cursor = $full
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe artifact ancestry.' }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
Assert-OwnedPath $directory
if (Test-Path -LiteralPath $directory) { throw 'Diagnostic directory must be fresh.' }
New-Item -ItemType Directory -Path $directory | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$supervisor = [ordered]@{
    schema_version = 1; rid = $RuntimeIdentifier; binary_sha256 = $null; binary_sha256_after = $null
    client_source_sha256 = $null; client_project_sha256 = $null; build_exit_code = $null
    client_exit_code = $null; timed_out = $false; job_empty = $false; cleanup_forced = $false; error_class = 'preflight'
}

# JOB_LIST assigns ownership before the child's first instruction; HANDLE_LIST
# admits only standard-file handles, never the job or process handle.
$nativeSource = @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>Narrow atomic job supervisor; never opens a process by global PID.</summary>
public static class MoteGridFocusJob
{
    /// <summary>Actual process exit and independently queried ownership facts.</summary>
    public sealed class Result
    {
        /// <summary>Actual signed exit from the retained, signaled process handle.</summary>
        public int? ExitCode;
        /// <summary>Independent deadline, queried-tree, and forced-cleanup witnesses.</summary>
        public bool TimedOut, JobEmpty, CleanupForced;
        /// <summary>Closed failure category only; never an exception message.</summary>
        public string ErrorClass;
    }
    /// <summary>Owns exactly one kernel handle, with no inheritance.</summary>
    private sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Handle() : base(true) { }
        protected override bool ReleaseHandle() { return CloseHandle(handle); }
    }
    /// <summary>SECURITY_ATTRIBUTES; only standard-file handles are inheritable.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Security { public int Size; public IntPtr Descriptor; public int Inherit; }
    /// <summary>Native STARTUPINFO layout; reserved slots must remain zero.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        public ushort Show, ReservedBytes; public IntPtr ReservedData, Input, Output, Error;
    }
    /// <summary>Attribute storage remains alive through native list deletion.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct StartupEx { public Startup Startup; public IntPtr Attributes; }
    /// <summary>Owned kernel handles; numeric identities never enter reports.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process,Thread; public uint ProcessId,ThreadId; }
    /// <summary>Only kill-on-close is set; no UI restrictions or breakaway permission.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime,JobTime; public uint Flags; public UIntPtr MinWorking,MaxWorking; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
    /// <summary>Required native padding/accounting slots; no resource threshold is set.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Io { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION, checked at 144 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Limits
    { public BasicLimits Basic; public Io Io; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
    /// <summary>Only ActiveProcesses from a successful native query certifies emptiness.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    { public long User,Kernel,PeriodUser,PeriodKernel; public uint Faults,Total,Active,Terminated; }

    /// <summary>Managed-only ABI checks for both supported 64-bit architectures.</summary>
    public static void CheckLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<Security>() != 24 || Marshal.SizeOf<BasicLimits>() != 64 ||
            Marshal.SizeOf<Startup>() != 104 || Marshal.SizeOf<StartupEx>() != 112 ||
            Marshal.SizeOf<ProcessInfo>() != 24 || Marshal.SizeOf<Limits>() != 144 || Marshal.SizeOf<Accounting>() != 48 ||
            Marshal.OffsetOf<Startup>("Input").ToInt32() != 80 || Marshal.OffsetOf<Accounting>("Active").ToInt32() != 40)
            throw new InvalidOperationException("Unsupported supervisor ABI.");
    }
    /// <summary>Quote one Windows CRT argument, including trailing slashes and quotes.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            text.Append(character); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    /// <summary>Job draining and process signaling share one ten-second cleanup budget.</summary>
    public static uint RemainingCleanupMilliseconds(long elapsed)
    { return (uint)Math.Max(0L, 10000L - Math.Max(0L, elapsed)); }
    /// <summary>Run once; timeout kills only this job and never invents normal exit.</summary>
    public static Result Run(string executable, string[] arguments, string cwd, string stdout, string stderr, int milliseconds)
    {
        CheckLayout(); var result = new Result();
        Handle job = null; IntPtr attributes = IntPtr.Zero, jobs = IntPtr.Zero, files = IntPtr.Zero;
        ProcessInfo child = new ProcessInfo(); bool attributesReady = false;
        try
        {
            job = CreateJobObjectW(IntPtr.Zero, null); Check(!job.IsInvalid);
            var limits = new Limits(); limits.Basic.Flags = 0x2000;
            Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Limits>()));
            var security = new Security { Size = Marshal.SizeOf<Security>(), Inherit = 1 };
            using (var input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero))
            using (var output = CreateFileW(stdout, 0x40000000, 1, ref security, 1, 0, IntPtr.Zero))
            using (var error = CreateFileW(stderr, 0x40000000, 1, ref security, 1, 0, IntPtr.Zero))
            {
                Check(!input.IsInvalid && !output.IsInvalid && !error.IsInvalid);
                UIntPtr size = UIntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
                Check(size != UIntPtr.Zero);
                attributes = Marshal.AllocHGlobal(checked((int)size.ToUInt64()));
                Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size)); attributesReady = true;
                jobs = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobs, job.DangerousGetHandle());
                files = Marshal.AllocHGlobal(IntPtr.Size * 3);
                Marshal.WriteIntPtr(files, 0, input.DangerousGetHandle());
                Marshal.WriteIntPtr(files, IntPtr.Size, output.DangerousGetHandle());
                Marshal.WriteIntPtr(files, IntPtr.Size * 2, error.DangerousGetHandle());
                Check(UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x2000D, jobs, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
                Check(UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x20002, files, (UIntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero));
                var startup = new StartupEx { Attributes = attributes };
                startup.Startup.Size = Marshal.SizeOf<StartupEx>(); startup.Startup.Flags = 0x101;
                startup.Startup.Show = 0; startup.Startup.Input = input.DangerousGetHandle();
                startup.Startup.Output = output.DangerousGetHandle(); startup.Startup.Error = error.DangerousGetHandle();
                var command = new StringBuilder(Quote(executable));
                foreach (string argument in arguments) command.Append(' ').Append(Quote(argument));
                Check(CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080000,
                    IntPtr.Zero, cwd, ref startup, out child));
            }
            uint wait = WaitForSingleObject(child.Process, (uint)milliseconds);
            if (wait == 0x102) { result.TimedOut = true; result.ErrorClass = "timeout"; }
            else Check(wait == 0);
        }
        catch (Exception error) when (!(error is OutOfMemoryException)) { result.ErrorClass = "supervisor"; }
        finally
        {
            var cleanup = Stopwatch.StartNew();
            if (job != null && !job.IsInvalid)
            {
                try
                {
                    if (Active(job) != 0)
                    {
                        result.CleanupForced = true;
                        Check(TerminateJobObject(job, 124));
                    }
                    while (Active(job) != 0 && RemainingCleanupMilliseconds(cleanup.ElapsedMilliseconds) != 0)
                        Thread.Sleep((int)Math.Min(20U, RemainingCleanupMilliseconds(cleanup.ElapsedMilliseconds)));
                    result.JobEmpty = Active(job) == 0;
                    if (!result.JobEmpty) result.ErrorClass = "cleanup";
                }
                catch (Exception error) when (!(error is OutOfMemoryException)) { result.ErrorClass = "cleanup"; }
            }
            if (child.Process != IntPtr.Zero)
            {
                try
                {
                    // ActiveProcesses==0 is not a signaled process-handle witness.
                    // Keep the original handle and use only the remaining shared budget.
                    Check(WaitForSingleObject(child.Process, RemainingCleanupMilliseconds(cleanup.ElapsedMilliseconds)) == 0);
                    uint exit; Check(GetExitCodeProcess(child.Process, out exit));
                    result.ExitCode = unchecked((int)exit);
                }
                catch (Exception error) when (!(error is OutOfMemoryException)) { result.ErrorClass = "cleanup"; }
                CloseHandle(child.Process);
            }
            if (child.Thread != IntPtr.Zero) CloseHandle(child.Thread);
            if (attributesReady) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (jobs != IntPtr.Zero) Marshal.FreeHGlobal(jobs);
            if (files != IntPtr.Zero) Marshal.FreeHGlobal(files);
            if (job != null) job.Dispose();
        }
        return result;
    }
    /// <summary>Only a successful native query can certify the owned tree empty.</summary>
    private static uint Active(Handle job)
    { Accounting value; Check(QueryInformationJobObject(job, 1, out value, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero)); return value.Active; }
    /// <summary>Keep native failures internal; only Run's closed category is exported.</summary>
    private static void Check(bool condition) { if (!condition) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [DllImport("kernel32", SetLastError=true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern Handle CreateJobObjectW(IntPtr security,string name);
    [DllImport("kernel32", SetLastError=true)] private static extern bool SetInformationJobObject(Handle job,int information,ref Limits value,uint size);
    [DllImport("kernel32", SetLastError=true)] private static extern bool QueryInformationJobObject(Handle job,int information,out Accounting value,uint size,IntPtr returned);
    [DllImport("kernel32", SetLastError=true)] private static extern bool TerminateJobObject(Handle job,uint code);
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern Handle CreateFileW(string path,uint access,uint share,ref Security security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32", SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr attributes,int count,uint flags,ref UIntPtr size);
    [DllImport("kernel32", SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr attributes,uint flags,UIntPtr key,IntPtr value,UIntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32")] private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessW(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string cwd,ref StartupEx startup,out ProcessInfo process);
    [DllImport("kernel32", SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32", SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process,out uint exit);
}
'@
try {
    $exe = (Resolve-Path (Join-Path $root "src/Mote.Native/bin/Release/net10.0/$RuntimeIdentifier/publish/mote.exe")).Path
    $source = Join-Path $root 'tests/WindowsGridFocusProvenanceProbe/Program.cs'
    $project = Join-Path $root 'tests/WindowsGridFocusProvenanceProbe/WindowsGridFocusProvenanceProbe.csproj'
    $supervisor.binary_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $supervisor.client_source_sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $supervisor.client_project_sha256 = (Get-FileHash -LiteralPath $project -Algorithm SHA256).Hash
    # Independent client review 08b1df3 freezes corrected source 2d96897 (LF/CRLF).
    if ($supervisor.client_source_sha256 -cnotin @('B7E498A9D6BD190D4F835874E53C818F7A12E542FD9979CD038892280624FBF3', 'B96BD2445B69186FE51941C396ED089D3B431E50243CE1AF86F9674D05C7D33D') -or
        $supervisor.client_project_sha256 -cnotin @('5A442CDB96A250C26556165CABD5C58224378CD4573D6ECC13318DFCFF95F5E2', '6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4')) { throw 'Unreviewed client bytes.' }
    $inventoryPath = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier/publish-inventory.json"
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    if ($inventory.rid -cne $RuntimeIdentifier -or $inventory.executable -cne 'mote.exe' -or
        $inventory.payload_files.Count -ne 1 -or $inventory.payload_files[0].path -cne 'mote.exe') { throw 'Strict published inventory required.' }
    $artifacts = Join-Path $directory 'build'
    $arch = $RuntimeIdentifier.Substring(4)
    & dotnet build $project --configuration Release --arch $arch --artifacts-path $artifacts *> (Join-Path $directory 'build.log')
    $supervisor.build_exit_code = $LASTEXITCODE
    $supervisor.error_class = 'build'
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
    $client = @(Get-ChildItem -LiteralPath $artifacts -File -Recurse -Filter 'WindowsGridFocusProvenanceProbe.dll' |
        Where-Object { $_.DirectoryName -match '[\\/]bin[\\/]' })
    if ($client.Count -ne 1) { throw 'Exact current-architecture client missing.' }
    $supervisor.error_class = 'supervisor'
    Add-Type -TypeDefinition $nativeSource
    [MoteGridFocusJob]::CheckLayout()
    # Console-only ownership controls precede the one UIA attempt.
    $controlPath = Join-Path $directory 'ownership-control.ps1'
    $control = @'
param([ValidateSet('normal','timeout')][string] $Mode)
$start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$command = if ($Mode -ceq 'normal') { 'Start-Sleep -Milliseconds 100' } else { 'Start-Sleep -Seconds 60' }
foreach ($argument in @('-NoProfile', '-Command', $command)) { $start.ArgumentList.Add($argument) }
$child = [Diagnostics.Process]::Start($start)
Write-Output 'owned-descendant-started'
if ($Mode -ceq 'normal') {
    if (-not $child.WaitForExit(5000) -or $child.ExitCode -ne 0) { exit 1 }
    $child.Dispose(); exit 0
}
Start-Sleep -Seconds 60
'@
    [IO.File]::WriteAllText($controlPath, $control, $utf8)
    $pwsh = (Get-Process -Id $PID).Path
    $supervisor.error_class = 'control'
    foreach ($mode in @('normal', 'timeout')) {
        $limit = if ($mode -ceq 'normal') { 10000 } else { 5000 }
        $check = [MoteGridFocusJob]::Run($pwsh, @('-NoProfile', '-File', $controlPath, '-Mode', $mode), $root,
            (Join-Path $directory "control-$mode.stdout.txt"), (Join-Path $directory "control-$mode.stderr.txt"), $limit)
        $check | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory "control-$mode.json") -Encoding utf8
        $marker = Get-Content -LiteralPath (Join-Path $directory "control-$mode.stdout.txt")
        if ($marker -cnotcontains 'owned-descendant-started' -or -not $check.JobEmpty -or
            ($mode -ceq 'normal' -and ($check.ExitCode -ne 0 -or $check.TimedOut -or $check.CleanupForced -or $null -ne $check.ErrorClass)) -or
            ($mode -ceq 'timeout' -and (-not $check.TimedOut -or -not $check.CleanupForced -or $check.ExitCode -ne 124 -or $check.ErrorClass -cne 'timeout'))) {
            throw 'Atomic job ownership control failed.'
        }
    }
    $scratch = Join-Path $directory 'scratch'
    $report = Join-Path $directory 'report.json'
    $supervisor.error_class = 'supervisor'
    $result = [MoteGridFocusJob]::Run((Get-Command dotnet).Source, @($client[0].FullName, $exe, $scratch, $report), $root,
        (Join-Path $directory 'stdout.txt'), (Join-Path $directory 'stderr.txt'), 120000)
    $supervisor.client_exit_code = $result.ExitCode
    $supervisor.timed_out = $result.TimedOut
    $supervisor.job_empty = $result.JobEmpty
    $supervisor.cleanup_forced = $result.CleanupForced
    $supervisor.error_class = $result.ErrorClass
    $supervisor.binary_sha256_after = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($supervisor.binary_sha256_after -cne $supervisor.binary_sha256) { $supervisor.error_class = 'binary_changed' }
    if ($null -eq $supervisor.error_class -and $result.ExitCode -ne 0) { $supervisor.error_class = 'client' }
} finally {
    Assert-OwnedPath (Join-Path $directory 'supervisor.json')
    $supervisor | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'supervisor.json') -Encoding utf8
}
if ($null -ne $supervisor.error_class -or -not $supervisor.job_empty) { throw 'Independent focus diagnostic incomplete; inspect retained evidence.' }
