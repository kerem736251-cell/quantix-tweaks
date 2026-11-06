param([ValidateSet('Inspect','Optimize','Restore','EthernetMinimal')][string]$Mode = 'Inspect',
      [ValidateSet('Pro','Elite')][string]$Tier = 'Pro',
      [string]$BackupPath = "$env:ProgramData\Quantix\Network\baseline-v1.json",
      [switch]$Library)
$ErrorActionPreference = 'Stop'

function Get-ActiveConnection {
    $routes = @(Get-NetRoute -ErrorAction Stop | Where-Object {
        $_.DestinationPrefix -in @('0.0.0.0/0','::/0') -and $_.State -eq 'Alive'
    })
    $candidates = foreach ($route in $routes) {
        $ip = Get-NetIPInterface -InterfaceIndex $route.InterfaceIndex -AddressFamily $route.AddressFamily
        $nic = Get-NetAdapter | Where-Object { $_.ifIndex -eq $route.InterfaceIndex } | Select-Object -First 1
        if ($nic -and $nic.Status -eq 'Up') {
            [pscustomobject]@{ Nic=$nic; Family=$route.AddressFamily; Cost=([long]$route.RouteMetric + [long]$ip.InterfaceMetric) }
        }
    }
    $selected = $candidates | Sort-Object @{Expression={if ($_.Family -eq 'IPv4') {0} else {1}}},Cost | Select-Object -First 1
    if (!$selected) { return [pscustomobject]@{ Type='Offline'; Status='No active default route'; Adapter='None'; Id=''; Index=0; LinkSpeed='—'; Dns='—'; Physical=$false } }
    $nic = $selected.Nic
    $type = 'Virtual / other'
    if ($nic.HardwareInterface) {
        if ([int]$nic.NdisPhysicalMedium -in @(1,9) -or [int]$nic.InterfaceType -eq 71) { $type='Wi-Fi' }
        elseif ([int]$nic.NdisPhysicalMedium -eq 14 -or [int]$nic.InterfaceType -eq 6) { $type='Ethernet' }
    }
    $dns = @(Get-DnsClientServerAddress -InterfaceIndex $nic.ifIndex | ForEach-Object { $_.ServerAddresses })
    [pscustomobject]@{ Type=$type; Status='Connected (default route)'; Adapter=$nic.Name; Id=([guid]$nic.InterfaceGuid).ToString('D'); Index=[int]$nic.ifIndex; LinkSpeed=$nic.LinkSpeed.ToString(); Dns=($dns -join ', '); Physical=[bool]$nic.HardwareInterface }
}

function Get-SavedEthernet($entry) {
    $nic = Get-NetAdapter | Where-Object { ([guid]$_.InterfaceGuid).ToString('D') -eq $entry.AdapterId } | Select-Object -First 1
    if (!$nic -or !$nic.HardwareInterface -or [int]$nic.InterfaceType -ne 6 -or [int]$nic.NdisPhysicalMedium -in @(1,9)) { throw 'Original Ethernet adapter unavailable.' }
    return $nic
}

function Get-EthernetPlan($connection, $notes) {
    $nic = Get-SavedEthernet ([pscustomobject]@{AdapterId=$connection.Id})
    $bindings=@($nic | Get-NetAdapterBinding -AllBindings -ErrorAction Stop)
    if ('ms_tcpip' -notin @($bindings.ComponentID) -or 'ms_tcpip6' -notin @($bindings.ComponentID)) { throw 'IPv4/IPv6 bindings unavailable; no changes made.' }
    foreach ($binding in $bindings) {
        [pscustomobject]@{Key=('binding:'+ $connection.Id+':'+$binding.ComponentID); Kind='Binding'; AdapterId=$connection.Id; Component=$binding.ComponentID; Original=''; Target=($binding.ComponentID -in @('ms_tcpip','ms_tcpip6')).ToString()}
    }
    $names = @('Advanced EEE','Battery Mode Link Speed','Battery Mode Link Speed - Laptop','Energy Efficient Ethernet','Green Ethernet','Low Power Mode','Power Saving','Ultra Lower Power Mode','Ultra Low Power Mode','Reduce Speed On Power Down','Idle Power Saving','Green Mode','Wake On Magic Packet','Adaptive Inter-Frame Spacing','Receive Side Scaling','Receive Side Scaling (RSS)','TCP/UDP Checksum Offload','TCP Checksum Offload (IPv4)','TCP Checksum Offload (IPv6)','UDP Checksum Offload (IPv4)','UDP Checksum Offload (IPv6)','Interrupt Moderation','Flow Control')
    $keywords = @('*EEE','*WakeOnMagicPacket','*RSS','*TCPChecksumOffloadIPv4','*TCPChecksumOffloadIPv6','*UDPChecksumOffloadIPv4','*UDPChecksumOffloadIPv6','*IPChecksumOffloadIPv4','*InterruptModeration','*FlowControl')
    $properties = @($nic | Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction Stop)
    foreach ($prop in $properties) {
        if ($prop.DisplayName -notin $names -and $prop.RegistryKeyword -notin $keywords) { continue }
        $target=$null
        for ($i=0; $i -lt @($prop.ValidDisplayValues).Count; $i++) {
            if ($prop.ValidDisplayValues[$i] -match '^(Disabled|Disable|Off|Deaktiviert|Aus)$') { $target=[string]$prop.ValidRegistryValues[$i]; break }
        }
        if ($null -eq $target -and $prop.RegistryKeyword -in $keywords -and '0' -in @($prop.ValidRegistryValues)) { $target='0' }
        if ($null -eq $target -or @($prop.RegistryValue).Count -ne 1) { $notes.Add('Skipped ambiguous/unsupported: '+$prop.DisplayName); continue }
        [pscustomobject]@{Key=('advanced:'+ $connection.Id+':'+$prop.RegistryKeyword); Kind='Advanced'; AdapterId=$connection.Id; Keyword=$prop.RegistryKeyword; Original=''; Target=$target}
    }
    foreach ($name in $names) { if ($name -notin @($properties.DisplayName)) { $notes.Add('Not exposed by driver: '+$name) } }
}

function Get-Plan {
    $text = & "$env:SystemRoot\System32\powercfg.exe" /getactivescheme
    if ($LASTEXITCODE -ne 0 -or "$text" -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}') { throw 'Active power plan unavailable.' }
    $Matches[0]
}

function Read-Setting($entry) {
    switch ($entry.Kind) {
        'Binding' {
            $nic=Get-SavedEthernet $entry
            $binding=@($nic | Get-NetAdapterBinding -AllBindings | Where-Object { $_.ComponentID -eq $entry.Component })
            if ($binding.Count -ne 1) { throw 'Original binding unavailable.' }
            return ([bool]$binding[0].Enabled).ToString()
        }
        'Advanced' {
            $nic=Get-SavedEthernet $entry
            $prop=@($nic | Get-NetAdapterAdvancedProperty -AllProperties | Where-Object { $_.RegistryKeyword -eq $entry.Keyword })
            if ($prop.Count -ne 1 -or @($prop[0].RegistryValue).Count -ne 1) { throw 'Original driver property unavailable.' }
            return [string]$prop[0].RegistryValue[0]
        }
        'Delivery' {
            $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization')
            try {
                if (!$key -or 'DODownloadMode' -notin $key.GetValueNames()) { return 'missing' }
                if ($key.GetValueKind('DODownloadMode') -ne [Microsoft.Win32.RegistryValueKind]::DWord) { throw 'Non-DWORD policy: skipped.' }
                return [string]$key.GetValue('DODownloadMode')
            } finally { if ($key) { $key.Dispose() } }
        }
        'WirelessAC' {
            $text = @(& "$env:SystemRoot\System32\powercfg.exe" /query $entry.Plan '19cbb8fa-5279-450e-9fac-8a3d5fedd0c1' '12bbebe6-58d6-4636-95bb-3217ef867c1a') -join "`n"
            if ($LASTEXITCODE -ne 0) { throw 'Wireless power setting unsupported.' }
            $values = [regex]::Matches($text,'0x([0-9a-fA-F]{8})')
            if ($values.Count -lt 2) { throw 'Wireless power setting unreadable.' }
            return [string][Convert]::ToInt32($values[$values.Count-2].Groups[1].Value,16)
        }
        'Rss' {
            $nic = Get-NetAdapter | Where-Object { ([guid]$_.InterfaceGuid).ToString('D') -eq $entry.AdapterId } | Select-Object -First 1
            if (!$nic -or !$nic.HardwareInterface -or [int]$nic.InterfaceType -ne 6 -or [int]$nic.NdisPhysicalMedium -in @(1,9)) { throw 'Original Ethernet adapter unavailable.' }
            $rss = $nic | Get-NetAdapterRss -ErrorAction Stop
            if (!$rss -or $rss.NumberOfReceiveQueues -lt 1) { throw 'RSS unsupported by adapter.' }
            return ([bool]$rss.Enabled).ToString()
        }
        default { throw 'Unknown backup setting.' }
    }
}

function Write-Setting($entry, [string]$value) {
    switch ($entry.Kind) {
        'Binding' {
            $nic=Get-SavedEthernet $entry
            $binding=$nic | Get-NetAdapterBinding -AllBindings | Where-Object { $_.ComponentID -eq $entry.Component }
            $binding | Set-NetAdapterBinding -Enabled ([bool]::Parse($value)) -Confirm:$false -ErrorAction Stop
        }
        'Advanced' {
            $nic=Get-SavedEthernet $entry
            $prop=$nic | Get-NetAdapterAdvancedProperty -AllProperties | Where-Object { $_.RegistryKeyword -eq $entry.Keyword }
            $prop | Set-NetAdapterAdvancedProperty -RegistryValue @($value) -NoRestart -Confirm:$false -ErrorAction Stop
        }
        'Delivery' {
            $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization')
            try {
                if ($value -eq 'missing') { $key.DeleteValue('DODownloadMode',$false) }
                else { $key.SetValue('DODownloadMode',[int]$value,[Microsoft.Win32.RegistryValueKind]::DWord) }
            } finally { $key.Dispose() }
        }
        'WirelessAC' {
            & "$env:SystemRoot\System32\powercfg.exe" /setacvalueindex $entry.Plan '19cbb8fa-5279-450e-9fac-8a3d5fedd0c1' '12bbebe6-58d6-4636-95bb-3217ef867c1a' $value | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Windows rejected wireless power preference.' }
            if ((Get-Plan) -eq $entry.Plan) {
                & "$env:SystemRoot\System32\powercfg.exe" /setactive $entry.Plan | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Power preference saved but could not be activated.' }
            }
        }
        'Rss' {
            $nic = Get-NetAdapter | Where-Object { ([guid]$_.InterfaceGuid).ToString('D') -eq $entry.AdapterId } | Select-Object -First 1
            if (!$nic -or !$nic.HardwareInterface -or [int]$nic.InterfaceType -ne 6 -or [int]$nic.NdisPhysicalMedium -in @(1,9)) { throw 'Original Ethernet adapter unavailable.' }
            $nic | Set-NetAdapterRss -Enabled ([bool]::Parse($value)) -NoRestart -Confirm:$false -ErrorAction Stop
        }
        default { throw 'Unknown backup setting.' }
    }
}

function Save-Journal($journal) {
    $temp = $BackupPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $journal -Depth 8))
        $file = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
        if ([IO.File]::Exists($BackupPath)) { [IO.File]::Replace($temp,$BackupPath,$BackupPath+'.previous') }
        else { [IO.File]::Move($temp,$BackupPath) }
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run Quantix as administrator to change or restore network settings.' }
}

function Invoke-NetworkWorkflow {
    if ($Mode -eq 'Inspect') { return [pscustomobject]@{ Success=$true; Connection=(Get-ActiveConnection); Message='Adapter details refreshed.' } }
    if ($Mode -in @('Optimize','EthernetMinimal')) { throw 'New network presets have been removed. Use the original Network controls.' }
    Assert-Administrator
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($BackupPath)) | Out-Null
    $lock = [IO.File]::Open($BackupPath+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $journal = [pscustomobject]@{ Version=1; Entries=@() }
        if (Test-Path -LiteralPath $BackupPath) {
            $journal = Get-Content -LiteralPath $BackupPath -Raw | ConvertFrom-Json
            if ($journal.Version -ne 1 -or $null -eq $journal.Entries) { throw 'Invalid network backup. Changes blocked to protect recovery data.' }
            foreach ($e in $journal.Entries) {
                if ($e.Kind -notin @('Delivery','WirelessAC','Rss','Binding','Advanced') -or !$e.Key -or $null -eq $e.Original -or $null -eq $e.Target) { throw 'Invalid network backup entry.' }
                if ($e.Kind -eq 'WirelessAC' -and $e.Plan -notmatch '^[0-9a-fA-F-]{36}$') { throw 'Invalid power-plan backup.' }
                if ($e.Kind -in @('Rss','Binding','Advanced') -and $e.AdapterId -notmatch '^[0-9a-fA-F-]{36}$') { throw 'Invalid adapter backup.' }
            }
        }
        $notes = [Collections.Generic.List[string]]::new()
        $success = $true
        if ($Mode -eq 'Restore') {
            foreach ($entry in @($journal.Entries)) {
                try {
                    $current = Read-Setting $entry
                    if ($current -ne $entry.Original -and $current -ne $entry.Target -and $current -ne $entry.Observed) { throw 'Setting changed outside Quantix; original backup retained.' }
                    if ($current -ne $entry.Original) { Write-Setting $entry $entry.Original }
                    if ((Read-Setting $entry) -ne $entry.Original) { throw 'Restore verification failed; backup retained.' }
                    $journal.Entries = @($journal.Entries | Where-Object { $_.Key -ne $entry.Key })
                    Save-Journal $journal
                    $notes.Add('Restored: '+$entry.Kind)
                } catch { $success=$false; $notes.Add($entry.Kind+': '+$_.Exception.Message) }
            }
            if ($notes.Count -eq 0) { $notes.Add('No saved network changes to restore.') }
        } else {
            $connection = Get-ActiveConnection
            if ($connection.Type -notin @('Wi-Fi','Ethernet') -or !$connection.Physical) { throw 'No supported active physical Wi-Fi/Ethernet default route. Nothing changed.' }
            if ($Mode -eq 'EthernetMinimal') {
                if ($connection.Type -ne 'Ethernet') { throw 'Ethernet-only action: no settings changed.' }
                $plan=@(Get-EthernetPlan $connection $notes)
                $live=Get-ActiveConnection
                if ($live.Id -ne $connection.Id -or $live.Type -ne 'Ethernet') { throw 'Active connection changed; nothing changed.' }
                $oldRss=@($journal.Entries | Where-Object { $_.Kind -eq 'Rss' -and $_.AdapterId -eq $connection.Id })
                $newRss=@($plan | Where-Object { $_.Kind -eq 'Advanced' -and $_.Keyword -eq '*RSS' })
                # Consolidate RSS baselines so restoring cannot enable a previously disabled feature.
                if ($oldRss.Count -and $newRss.Count) {
                    $currentRss=Read-Setting $oldRss[0]
                    if ($currentRss -ne $oldRss[0].Original -and $currentRss -ne $oldRss[0].Target) { throw 'RSS changed outside Quantix; existing backup retained.' }
                }
                # Capture ALL bindings before any binding writes: Windows can change related bindings.
                foreach ($entry in $plan) {
                    $entry.Original=Read-Setting $entry
                    if ($entry.Kind -eq 'Advanced' -and $entry.Keyword -eq '*RSS' -and $oldRss.Count) { $entry.Original=if ([bool]::Parse($oldRss[0].Original)) {'1'} else {'0'} }
                    $saved=@($journal.Entries | Where-Object { $_.Key -eq $entry.Key })
                    if ($saved.Count) {
                        if ($entry.Original -ne $saved[0].Target -and $entry.Original -ne $saved[0].Original) { throw 'Setting changed outside Quantix; restore/review existing backup first.' }
                        $saved[0].Target=$entry.Target
                    } else { $journal.Entries=@($journal.Entries)+@($entry) }
                }
                if ($oldRss.Count -and $newRss.Count) { $journal.Entries=@($journal.Entries | Where-Object { $_.Key -ne $oldRss[0].Key }) }
                Save-Journal $journal
                foreach ($entry in $plan) {
                    try {
                        # Binding changes can temporarily remove the route; resolve the saved physical GUID.
                        $null=Get-SavedEthernet $entry
                        if ((Read-Setting $entry) -ne $entry.Target) { Write-Setting $entry $entry.Target }
                        if ((Read-Setting $entry) -ne $entry.Target) { throw 'Driver rejected change; backup retained.' }
                        if ($entry.Kind -eq 'Binding') {
                            foreach ($bindingEntry in @($journal.Entries | Where-Object { $_.Kind -eq 'Binding' -and $_.AdapterId -eq $connection.Id })) {
                                $bindingEntry | Add-Member -NotePropertyName Observed -NotePropertyValue (Read-Setting $bindingEntry) -Force
                            }
                            Save-Journal $journal
                        }
                        $notes.Add('Applied: '+$entry.Key)
                    } catch { $success=$false; $notes.Add($_.Exception.Message) }
                }
                foreach ($entry in $plan) { if ((Read-Setting $entry) -ne $entry.Target) { $success=$false; $notes.Add('Final verification failed: '+$entry.Key) } }
                $notes.Insert(0,'Ethernet: IPv4/IPv6 only + supported disabled driver properties. Connection may briefly drop. Restart Windows to activate deferred driver changes.')
                return [pscustomobject]@{Success=$success;Message=($notes -join "`n")}
            }
            $plan = @([pscustomobject]@{ Key='delivery'; Kind='Delivery'; AdapterId=''; Plan=''; Original=''; Target='0' })
            if ($Tier -eq 'Elite' -and $connection.Type -eq 'Wi-Fi') {
                try { $guid=Get-Plan; $plan += [pscustomobject]@{ Key=('wifi-ac:'+ $guid); Kind='WirelessAC'; AdapterId=$connection.Id; Plan=$guid; Original=''; Target='0' } }
                catch { $notes.Add('Wireless AC preference skipped: '+$_.Exception.Message) }
            }
            if ($Tier -eq 'Elite' -and $connection.Type -eq 'Ethernet') {
                $plan += [pscustomobject]@{ Key=('rss:'+ $connection.Id); Kind='Rss'; AdapterId=$connection.Id; Plan=''; Original=''; Target='True' }
            }
            foreach ($entry in $plan) {
                try {
                    # Re-resolve the route before each change. Never follow a stale adapter name/index.
                    $live = Get-ActiveConnection
                    if ($live.Id -ne $connection.Id -or $live.Type -ne $connection.Type) { throw 'Active connection changed. Refresh and retry.' }
                    if ($entry.Kind -eq 'Rss' -and @($journal.Entries | Where-Object { $_.Kind -eq 'Advanced' -and $_.AdapterId -eq $entry.AdapterId -and $_.Keyword -eq '*RSS' }).Count) { $notes.Add('RSS preserved: explicit Ethernet profile is active.'); continue }
                    if (@($journal.Entries | Where-Object { $_.Key -eq $entry.Key }).Count) { $notes.Add('Already backed up: '+$entry.Kind); continue }
                    try { $entry.Original = Read-Setting $entry }
                    catch { $notes.Add('Skipped unsupported '+$entry.Kind+': '+$_.Exception.Message); continue }
                    if ($entry.Original -eq $entry.Target) { $notes.Add('Already configured: '+$entry.Kind); continue }
                    $journal.Entries = @($journal.Entries) + @($entry)
                    Save-Journal $journal # Durable baseline before the first write, including partial failures.
                    Write-Setting $entry $entry.Target
                    if ((Read-Setting $entry) -ne $entry.Target) { throw 'Change not verified. Baseline retained for restore.' }
                    $notes.Add('Applied: '+$entry.Kind)
                } catch { $success=$false; $notes.Add($entry.Kind+': '+$_.Exception.Message); break }
            }
            $notes.Insert(0,$Tier+' / '+$connection.Type+' profile')
            if ($Tier -eq 'Elite' -and $connection.Type -eq 'Ethernet') { $notes.Add('RSS uses NoRestart; driver may require a later restart.') }
        }
        return [pscustomobject]@{ Success=$success; Message=($notes -join "`n") }
    } finally { $lock.Dispose() }
}

if (!$Library) {
    try { Invoke-NetworkWorkflow | ConvertTo-Json -Depth 8 -Compress }
    catch { [pscustomobject]@{ Success=$false; Message=$_.Exception.Message } | ConvertTo-Json -Compress }
}
