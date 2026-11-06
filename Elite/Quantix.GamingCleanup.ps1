param([ValidateSet('Scan','Apply','Restore')][string]$Mode='Scan', [string[]]$Selected=@(), [string]$BackupRoot=(Join-Path $env:LOCALAPPDATA 'Quantix\Elite\GamingCleanup'), [switch]$Library)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$apps=@('Microsoft.BingNews','Microsoft.BingWeather','Microsoft.Getstarted','Microsoft.People','Microsoft.WindowsFeedbackHub','Microsoft.ZuneMusic','Microsoft.ZuneVideo','Clipchamp.Clipchamp','Microsoft.WindowsMaps','Microsoft.SkypeApp','Microsoft.Microsoft3DViewer','Microsoft.MicrosoftOfficeHub','Microsoft.YourPhone','Microsoft.BingFinance','Microsoft.BingSports','Microsoft.WindowsSoundRecorder','Microsoft.BingTravel','Microsoft.BingFoodAndDrink','Microsoft.BingHealthAndFitness','Microsoft.549981C3F5F10')
$services=@('MapsBroker','Fax','RetailDemo','DiagTrack','WMPNetworkSvc','RemoteRegistry')
$policies=@{
    OptionalWidgets=@{Path='HKLM:\SOFTWARE\Policies\Microsoft\Dsh';Name='AllowNewsAndInterests';Min=22000;Max=999999}
    OptionalNewsFeeds=@{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds';Name='EnableFeeds';Min=19041;Max=21999}
}
$tasks=@{
    OptionalTaskFeedback=@{Path='\Microsoft\Windows\Feedback\Siuf\';Name='DmClient'}
    OptionalTaskFeedbackScenario=@{Path='\Microsoft\Windows\Feedback\Siuf\';Name='DmClientOnScenarioDownload'}
    OptionalTaskCeip=@{Path='\Microsoft\Windows\Customer Experience Improvement Program\';Name='Consolidator'}
    OptionalTaskUsbCeip=@{Path='\Microsoft\Windows\Customer Experience Improvement Program\';Name='UsbCeip'}
    OptionalTaskMapsToast=@{Path='\Microsoft\Windows\Maps\';Name='MapsToastTask'}
    OptionalTaskMapsUpdate=@{Path='\Microsoft\Windows\Maps\';Name='MapsUpdateTask'}
}
function Get-CleanupTaskState($definition) {
    $task=Get-ScheduledTask -TaskPath $definition.Path -TaskName $definition.Name -ErrorAction SilentlyContinue
    if(!$task){return $null}
    $fingerprint=Get-CleanupTaskSignature (Export-ScheduledTask -TaskPath $definition.Path -TaskName $definition.Name -ErrorAction Stop)
    return @{Enabled=[bool]$task.Settings.Enabled;Fingerprint=$fingerprint}
}
function Get-CleanupTaskSignature([string]$text) {
    [xml]$xml=$text
    $namespace=New-Object Xml.XmlNamespaceManager($xml.NameTable);$namespace.AddNamespace('t',$xml.DocumentElement.NamespaceURI)
    $enabled=$xml.SelectSingleNode('/t:Task/t:Settings/t:Enabled',$namespace)
    if($enabled){$enabled.ParentNode.RemoveChild($enabled)|Out-Null}
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($xml.OuterXml))).Replace('-','')}finally{$hash.Dispose()}
    return $fingerprint
}
function Set-CleanupTaskEnabled($definition,[bool]$enabled) {
    if($enabled){Enable-ScheduledTask -TaskPath $definition.Path -TaskName $definition.Name -ErrorAction Stop|Out-Null}
    else{Disable-ScheduledTask -TaskPath $definition.Path -TaskName $definition.Name -ErrorAction Stop|Out-Null}
}
function Get-CleanupPlatform {
    try {
        $os=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $computer=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        $managed=[bool]$computer.PartOfDomain
        foreach($path in @('HKLM:\SOFTWARE\Microsoft\Enrollments','HKLM:\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo')) {
            if(Test-Path -LiteralPath $path){foreach($child in Get-ChildItem -LiteralPath $path){$entry=Get-ItemProperty -LiteralPath $child.PSPath;if($entry.EnrollmentType -or $entry.TenantId -or $entry.ProviderID){$managed=$true}}}
        }
        return @{Build=[int]$os.CurrentBuildNumber;Edition=[string]$os.EditionID;Managed=$managed}
    }catch{return @{Build=0;Edition='Unknown';Managed=$true}}
}
function Get-CleanupPolicyState($policy) {
    if(!(Test-Path -LiteralPath $policy.Path)){return @{Exists=$false;Value=$null;Kind=$null}}
    $key=Get-Item -LiteralPath $policy.Path
    if($policy.Name -notin $key.GetValueNames()){return @{Exists=$false;Value=$null;Kind=$null}}
    return @{Exists=$true;Value=$key.GetValue($policy.Name);Kind=$key.GetValueKind($policy.Name).ToString()}
}
function Set-CleanupPolicyState($policy,$state) {
    if($state.Exists){New-Item -Path $policy.Path -Force|Out-Null;New-ItemProperty -LiteralPath $policy.Path -Name $policy.Name -PropertyType DWord -Value ([int]$state.Value) -Force|Out-Null}
    else {Remove-ItemProperty -LiteralPath $policy.Path -Name $policy.Name -ErrorAction Stop}
}
if (@($Selected | Where-Object {$_ -notin ($apps+$services+@($policies.Keys)+@($tasks.Keys))}).Count) {throw 'Unknown cleanup target.'}
function Assert-CleanupAdministrator {
    if (!(New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run Elite as administrator to change or restore services. Nothing changed.' }
}
function Save-ServiceBackup($path,$backup) {
    $temp=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        $bytes=[Text.Encoding]::UTF8.GetBytes(($backup|ConvertTo-Json))
        $file=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {$file.Write($bytes,0,$bytes.Length);$file.Flush($true)}finally{$file.Dispose()}
        [IO.File]::Move($temp,$path)
    } finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
}
function Assert-ServiceBackup($saved,$id) {
    if($saved.Name -cne $id -or $saved.Start -notin @(2,3,4) -or $saved.Running -isnot [bool] -or ($null -ne $saved.Delayed -and $saved.Delayed -notin @(0,1))){throw 'Invalid service backup; no changes performed.'}
}
function Invoke-GamingCleanup {
if (@($Selected | Where-Object {$_ -notin ($apps+$services+@($policies.Keys)+@($tasks.Keys))}).Count) {throw 'Unknown cleanup target.'}
if ($Mode -eq 'Restore' -or ($Mode -eq 'Apply' -and @($Selected | Where-Object {$_ -in ($services+@($policies.Keys)+@($tasks.Keys))}).Count)) {Assert-CleanupAdministrator}
$lock=$null
try {
if($Mode -ne 'Scan') {
    [IO.Directory]::CreateDirectory($BackupRoot)|Out-Null
    $lock=[IO.File]::Open((Join-Path $BackupRoot 'workflow.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
}
$targets=@($apps+$services+@($policies.Keys)+@($tasks.Keys))
if($Mode -eq 'Apply'){$targets=@($targets|Where-Object {$_ -in $Selected})}
if($Mode -eq 'Restore'){$targets=@($services)+@($policies.Keys)+@($tasks.Keys)}
# Retain recovery for service backups created by earlier releases; never offer these for new changes.
if($Mode -eq 'Restore'){$targets+=@('WSearch','Spooler','PrintNotify','WerSvc','PhoneSvc','lfsvc','TrkWks','stisvc','FrameServer','FrameServerMonitor','CDPSvc','SSDPSRV','upnphost','WiaRpc')}
$platform=Get-CleanupPlatform
$step=0
$results=New-Object 'System.Collections.Generic.List[object]'
function Result($id,$state) {$results.Add([pscustomobject]@{Id=$id;State=$state})}
foreach($id in $targets) {
    if (!$Library) { [Console]::Error.WriteLine(('QUANTIX_PROGRESS|'+$step+'|'+$targets.Count+'|'+$id)) }
    $step++
    if($Mode -eq 'Apply' -and $id -notin $Selected){continue}
    try {
        if($tasks.ContainsKey($id)) {
            $definition=$tasks[$id];$path=Join-Path $BackupRoot ($id+'.json')
            if($platform.Managed -and $Mode -ne 'Scan'){if($Mode -eq 'Restore' -and (Test-Path -LiteralPath $path)){throw 'Managed device: saved task settings retained.'};Result $id 'Skipped: managed or unverified device';continue}
            $current=Get-CleanupTaskState $definition
            if(!$current){if($Mode -eq 'Restore' -and (Test-Path -LiteralPath $path)){throw 'Saved task is missing; original retained.'};Result $id 'Not installed';continue}
            if($Mode -eq 'Scan'){Result $id $(if($current.Enabled){'Enabled'}else{'Disabled'});continue}
            if($Mode -eq 'Apply') {
                if(!$current.Enabled){Result $id 'Already disabled';continue}
                if(!(Test-Path -LiteralPath $path)){Save-ServiceBackup $path ([pscustomobject]@{Name=$id;Enabled=$current.Enabled;Fingerprint=$current.Fingerprint})}
                $saved=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
                if($saved.Name -cne $id -or $saved.Enabled -isnot [bool] -or $saved.Fingerprint -cne $current.Fingerprint){throw 'Task baseline changed; no new changes made.'}
                Set-CleanupTaskEnabled $definition $false
                $verified=Get-CleanupTaskState $definition
                if(!$verified -or $verified.Enabled -or $verified.Fingerprint -cne $saved.Fingerprint){throw 'Task change could not be verified; original retained.'}
                Result $id 'Disabled scheduled trigger; original saved'
            } else {
                if(!(Test-Path -LiteralPath $path)){continue}
                $saved=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
                if($saved.Name -cne $id -or $saved.Enabled -isnot [bool] -or $saved.Fingerprint -cne $current.Fingerprint){throw 'Task definition changed; original retained without overwriting it.'}
                if($current.Enabled -ne $saved.Enabled){Set-CleanupTaskEnabled $definition ([bool]$saved.Enabled)}
                $verified=Get-CleanupTaskState $definition
                if(!$verified -or $verified.Enabled -ne $saved.Enabled -or $verified.Fingerprint -cne $saved.Fingerprint){throw 'Task restore verification failed; original retained.'}
                Remove-Item -LiteralPath $path
                Result $id 'Original scheduled trigger restored'
            }
        } elseif($policies.ContainsKey($id)) {
            $policy=$policies[$id];$path=Join-Path $BackupRoot ($id+'.json');$current=Get-CleanupPolicyState $policy
            if($Mode -eq 'Scan'){Result $id $(if($current.Exists){'Policy DWORD: '+$current.Value}else{'Not configured'});continue}
            if($platform.Managed){if($Mode -eq 'Restore' -and (Test-Path -LiteralPath $path)){throw 'Managed device: saved policy retained for recovery.'};Result $id 'Skipped: managed or unverified device';continue}
            if($Mode -eq 'Apply') {
                if($platform.Build -lt $policy.Min -or $platform.Build -gt $policy.Max -or $platform.Edition -notin @('Professional','ProfessionalN','Enterprise','EnterpriseN','Education','EducationN','IoTEnterprise','IoTEnterpriseS')){Result $id 'Skipped: policy not supported on this Windows version/edition';continue}
                if($current.Exists -and ($current.Kind -ne 'DWord' -or [int]$current.Value -ne 0)){Result $id 'Skipped: existing policy preserved';continue}
                if($current.Exists -and [int]$current.Value -eq 0){Result $id 'Already disabled by policy';continue}
                if(!(Test-Path -LiteralPath $path)){Save-ServiceBackup $path ([pscustomobject]@{Name=$id;Exists=$current.Exists;Value=$current.Value;Kind=$current.Kind})}
                Set-CleanupPolicyState $policy @{Exists=$true;Value=0}
                $verified=Get-CleanupPolicyState $policy
                if(!$verified.Exists -or $verified.Kind -ne 'DWord' -or [int]$verified.Value -ne 0){throw 'Policy write could not be verified; original retained.'}
                Result $id 'Disabled by policy; original saved; sign out or restart to take effect'
            } else {
                if(!(Test-Path -LiteralPath $path)){continue}
                $saved=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
                if($saved.Name -cne $id -or $saved.Exists -isnot [bool] -or ($saved.Exists -and ($saved.Kind -ne 'DWord' -or $saved.Value -isnot [int]))){throw 'Invalid policy backup; no changes made.'}
                if($current.Exists -and ($current.Kind -ne 'DWord' -or [int]$current.Value -ne 0)){throw 'Policy changed outside Elite; original retained without overwriting it.'}
                if($current.Exists -or $saved.Exists){Set-CleanupPolicyState $policy $saved}
                $verified=Get-CleanupPolicyState $policy
                if($verified.Exists -ne $saved.Exists -or ($saved.Exists -and ($verified.Kind -ne $saved.Kind -or $verified.Value -ne $saved.Value))){throw 'Policy restore verification failed; backup retained.'}
                Remove-Item -LiteralPath $path
                Result $id 'Original policy restored'
            }
        } elseif($id -in $apps) {
            $packages=@(Get-AppxPackage -Name $id | Where-Object {$_.Name -eq $id -and -not $_.IsFramework -and -not $_.NonRemovable})
            if($Mode -eq 'Scan') {if($packages.Count){Result $id 'Installed'};continue}
            if(!$packages.Count){Result $id 'Already absent';continue}
            foreach($package in $packages){Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop}
            if(@(Get-AppxPackage -Name $id).Count){throw 'Package still present after removal.'}
            Result $id 'Removed for this Windows account'
        } else {
            $service=Get-Service -Name $id -ErrorAction SilentlyContinue
            $path=Join-Path $BackupRoot ($id+'.json')
            if(!$service){if($Mode -ne 'Scan'){Result $id 'Not installed'};continue}
            if($Mode -eq 'Scan') {Result $id ($service.Status.ToString()+' / '+$service.StartType.ToString());continue}
            $key='HKLM:\SYSTEM\CurrentControlSet\Services\'+$id
            if($Mode -eq 'Apply') {
                if($platform.Managed){Result $id 'Skipped: managed or unverified device';continue}
                if($service.Status -notin @('Running','Stopped')){Result $id 'Skipped: service is paused or transitioning';continue}
                if($service.StartType -eq 'Disabled'){Result $id 'Already disabled';continue}
                if(@($service.DependentServices).Count){throw 'Another service depends on this service; skipped.'}
                New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
                if(!(Test-Path -LiteralPath $path)) {
                    $reg=Get-ItemProperty -LiteralPath $key
                    $backup=[pscustomobject]@{Name=$id;Start=[int]$reg.Start;Delayed=$reg.DelayedAutoStart;Running=($service.Status -eq 'Running')}
                    Save-ServiceBackup $path $backup
                }
                Assert-ServiceBackup (Get-Content -LiteralPath $path -Raw|ConvertFrom-Json) $id
                # Backup is durable before either operation. Never force dependent services to stop.
                Stop-Service -Name $id -ErrorAction Stop
                Set-Service -Name $id -StartupType Disabled -ErrorAction Stop
                $check=Get-Service -Name $id
                if($check.StartType -ne 'Disabled' -or $check.Status -ne 'Stopped'){throw 'Service change could not be verified; restore is available.'}
                Result $id 'Disabled; original settings saved'
            } else {
                if(!(Test-Path -LiteralPath $path)){continue}
                $saved=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                Assert-ServiceBackup $saved $id
                $type=@{2='Automatic';3='Manual';4='Disabled'}[[int]$saved.Start]
                Set-Service -Name $id -StartupType $type
                if($null -eq $saved.Delayed){Remove-ItemProperty -LiteralPath $key -Name DelayedAutoStart -ErrorAction SilentlyContinue}
                else {Set-ItemProperty -LiteralPath $key -Name DelayedAutoStart -Value ([int]$saved.Delayed)}
                if($saved.Running -and $saved.Start -ne 4){Start-Service -Name $id}
                elseif(!$saved.Running){Stop-Service -Name $id -ErrorAction Stop}
                $verified=Get-ItemProperty -LiteralPath $key
                $restored=Get-Service -Name $id
                if([int]$verified.Start -ne [int]$saved.Start -or $verified.DelayedAutoStart -ne $saved.Delayed -or ($saved.Running -and $restored.Status -ne 'Running') -or (!$saved.Running -and $restored.Status -ne 'Stopped')){throw 'Restore verification failed; backup kept.'}
                Remove-Item -LiteralPath $path
                Result $id 'Original startup settings restored'
            }
        }
    } catch {Result $id ('Failed: '+$_.Exception.Message)}
}
return ,@($results.ToArray())
} finally {if($lock){$lock.Dispose()}}
}
if (!$Library) {
    try { $result=Invoke-GamingCleanup; ConvertTo-Json -InputObject @($result) -Compress }
    catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
}
