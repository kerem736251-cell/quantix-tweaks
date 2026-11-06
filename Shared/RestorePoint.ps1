param([ValidateSet('Pro','Elite')][string]$Tier, [switch]$Library)
$ErrorActionPreference='Stop'
function Get-RestorePoints { @(Get-CimInstance -Namespace root/default -ClassName SystemRestore -OperationTimeoutSec 30) }
function Open-RestoreFrequencyKey { [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore') }
function Create-SystemPoint([string]$description) {
    Invoke-CimMethod -Namespace root/default -ClassName SystemRestore -MethodName CreateRestorePoint -Arguments @{
        Description=$description; RestorePointType=[uint32]12; EventType=[uint32]100
    } -OperationTimeoutSec 180
}
function Invoke-RestorePointHelper {
$mutex=[Threading.Mutex]::new($false,'Global\QuantixSystemRestoreCreation')
$held=$false
try {
    try { $held=$mutex.WaitOne([TimeSpan]::FromSeconds(30)) } catch [Threading.AbandonedMutexException] { $held=$true }
    if (!$held) { throw 'Another restore-point request is still running.' }
    $description='Quantix '+$Tier+' - Before Tweaks'
    $before=@(Get-RestorePoints | Select-Object -ExpandProperty SequenceNumber)
    $latest=if ($before.Count) { ($before | Measure-Object -Maximum).Maximum } else { 0 }
    $key=Open-RestoreFrequencyKey
    $name='SystemRestorePointCreationFrequency'
    $exists=$name -in $key.GetValueNames()
    $old=if ($exists) { $key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { $null }
    $kind=if ($exists) { $key.GetValueKind($name) } else { [Microsoft.Win32.RegistryValueKind]::DWord }
    try {
        # This independent helper survives a normal UI close and restores the temporary
        # frequency preference in its own finally block. No new retry is launched.
        $key.SetValue($name,0,[Microsoft.Win32.RegistryValueKind]::DWord)
        $result=Create-SystemPoint $description
        if ($result.ReturnValue -ne 0) { throw ('Windows returned '+$result.ReturnValue+'. System Protection may be disabled.') }
        $verified=@(Get-RestorePoints | Where-Object { $_.SequenceNumber -gt $latest -and $_.Description -eq $description }).Count -gt 0
        if ($verified) { $message='Restore point created: '+$description }
        else { $message='Windows accepted the request but no new restore point could be verified.' }
    } finally {
        try { if ($exists) { $key.SetValue($name,$old,$kind) } else { $key.DeleteValue($name,$false) } }
        finally { $key.Dispose() }
    }
    [pscustomobject]@{Message=$message} | ConvertTo-Json -Compress
} catch {
    [pscustomobject]@{Message=('Restore point unavailable: '+$_.Exception.Message)} | ConvertTo-Json -Compress
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

}
if (!$Library) { Invoke-RestorePointHelper }
