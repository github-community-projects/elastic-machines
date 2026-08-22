$ErrorActionPreference = "Stop"

$downloadUrl = "{{DOWNLOAD_URL}}"
$downloadToken = "{{DOWNLOAD_TOKEN}}"
$dotRunnerFile = "{{DOT_RUNNER_FILE}}"
$dotCredentialsFile = "{{DOT_CREDENTIALS_FILE}}"
$dotCredentialsRsaparamsFile = "{{DOT_CREDENTIALS_RSAPARAMS_FILE}}"

$username = "runneruser"
$runnerLocation = "C:\runner"

# Create or update user
Add-Type -AssemblyName "System.Web"
$password = [System.Web.Security.Membership]::GeneratePassword(32, 8)
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force

if (!(Get-LocalUser -Name $username -ErrorAction Ignore)) {
    New-LocalUser -Name $username -Password $securePassword
    Write-Host "Created user '$username'"
}
else {
    Set-LocalUser -Name $username -Password $securePassword
    Write-Host "Changed password for user '$username'"
}

# Add user to groups
foreach ($group in @("Administrators", "docker-users")) {
    if ((Get-LocalGroup -Name "$group" -ErrorAction Ignore) -and
        !(Get-LocalGroupMember -Group "$group" -Member $username -ErrorAction Ignore)) {
        Add-LocalGroupMember -Group "$group" -Member $username
        Write-Host "Added user '$username' to group '$group'"
    }
}

# Disable User Access Control (UAC)
Set-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System -Name ConsentPromptBehaviorAdmin -Value 0 -Force
Write-Host "Disabled User Access Control (UAC)"

# Install runner if needed
if (!(Test-Path "$runnerLocation\run.cmd")) {
    $zipFile = "${env:Temp}\runner.zip"
    $tries = 0

    while ($true) {
        try {
            Invoke-WebRequest -Headers @{ Authorization = "Bearer $downloadToken" } -Uri "$downloadUrl" -OutFile "$zipFile"
            break
        }
        catch {
            if ($tries++ -gt 3) {
                throw "Unable to download runner"
            }

            Write-Host "Runner download failed. Retrying in 2 seconds"
            Start-Sleep -Seconds 2
        }
    }

    Expand-Archive "$zipFile" "$runnerLocation"
    Write-Host "Installed runner to directory '$runnerLocation'"
}

# Create configuration files
Add-Type -AssemblyName System.Security
[System.Convert]::FromBase64String($dotRunnerFile) | Set-Content "$runnerLocation\.runner" -Encoding Byte
[System.Convert]::FromBase64String($dotCredentialsFile) | Set-Content "$runnerLocation\.credentials" -Encoding Byte
[System.Security.Cryptography.ProtectedData]::Protect([System.Convert]::FromBase64String($dotCredentialsRsaparamsFile), $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine) | Set-Content "$runnerLocation\.credentials_rsaparams" -Encoding Byte
Write-Host "Created configuration files"

# Schedule to run
$action = New-ScheduledTaskAction -WorkingDirectory "$runnerLocation" -Execute "run.cmd"
$trigger = Get-CimClass "MSFT_TaskRegistrationTrigger" -Namespace "Root/Microsoft/Windows/TaskScheduler"
Register-ScheduledTask -TaskName "runnertask" -Action $action -Trigger $trigger -User $username -Password $password -RunLevel Highest -Force

$tries = 0

while (!(Get-Process Runner.Listener -ErrorAction Ignore)) {
    if ($tries++ -gt 30) {
        throw "Runner.Listener failed to start"
    }

    Write-Host "Waiting for Runner.Listener to start"
    Start-Sleep -Seconds 1
}

# The orchestrator will look for this final output to determine if the this script succeeded
Write-Host "Started runner in background"
