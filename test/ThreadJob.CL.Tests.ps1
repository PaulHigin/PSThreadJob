# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

##
## ----------
## Test Note:
## ----------
## Since these tests change session and system state (constrained language and system lockdown)
## they will all use try/finally blocks instead of Pester AfterEach/AfterAll to ensure session
## and system state is restored.
## Pester AfterEach, AfterAll is not reliable when the session is constrained language or locked down.
##

function Get-RandomFileName
{
    [System.IO.Path]::GetFileNameWithoutExtension([IO.Path]::GetRandomFileName())
}

if ($IsWindows)
{
    $code = @'

    #region Using directives

    using System;
    using System.Management.Automation;

    #endregion

    /// <summary>Adds a new type to the Application Domain</summary>
    [Cmdlet("Invoke", "LanguageModeTestingSupportCmdlet")]
    public sealed class InvokeLanguageModeTestingSupportCmdlet : PSCmdlet
    {
        [Parameter()]
        public SwitchParameter EnableFullLanguageMode { get; set; }

        [Parameter()]
        public SwitchParameter SetLockdownMode { get; set; }

        [Parameter()]
        public SwitchParameter RevertLockdownMode { get; set; }

        protected override void BeginProcessing()
        {
            if (EnableFullLanguageMode)
            {
                SessionState.LanguageMode = PSLanguageMode.FullLanguage;
            }

            if (SetLockdownMode)
            {
                Environment.SetEnvironmentVariable("__PSLockdownPolicy", "0x80000007", EnvironmentVariableTarget.Machine);
            }

            if (RevertLockdownMode)
            {
                Environment.SetEnvironmentVariable("__PSLockdownPolicy", null, EnvironmentVariableTarget.Machine);
            }
        }
    }
'@

    if (-not (Get-Command Invoke-LanguageModeTestingSupportCmdlet -ErrorAction Ignore))
    {
        $moduleName = Get-RandomFileName
        $moduleDirectory = join-path $TestDrive\Modules $moduleName
        if (-not (Test-Path $moduleDirectory))
        {
            $null = New-Item -ItemType Directory $moduleDirectory -Force
        }

        try
        {
            Add-Type -TypeDefinition $code -OutputAssembly $moduleDirectory\TestCmdletForConstrainedLanguage.dll -ErrorAction Ignore
        } catch {}

        Import-Module -Name $moduleDirectory\TestCmdletForConstrainedLanguage.dll
    }
}

try
{
    $defaultParamValues = $PSDefaultParameterValues.Clone()
    $PSDefaultParameterValues["it:Skip"] = !$IsWindows

    Describe "ThreadJob Constrained Language Tests" -Tags 'Feature','RequireAdminOnWindows' {

        BeforeAll {

            $sb = { $ExecutionContext.SessionState.LanguageMode }

            $scriptTrustedFilePath = Join-Path $TestDrive "ThJobTrusted_System32.ps1"
            @'
            Write-Output $ExecutionContext.SessionState.LanguageMode
'@ | Out-File -FilePath $scriptTrustedFilePath

            $scriptUntrustedFilePath = Join-Path $TestDrive "ThJobUntrusted.ps1"
            @'
            Write-Output $ExecutionContext.SessionState.LanguageMode
'@ | Out-File -FilePath $scriptUntrustedFilePath
        }

        AfterAll {
            Get-Job | Where-Object PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        }

        It "ThreadJob script must run in ConstrainedLanguage mode with system lock down" {

            try
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -ScriptBlock { $ExecutionContext.SessionState.LanguageMode } | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "ConstrainedLanguage"
        }

        It "ThreadJob script block using variable must run in ConstrainedLanguage mode with system lock down" {

            try
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -ScriptBlock { & $using:sb } | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "ConstrainedLanguage"
        }

        It "ThreadJob script block argument variable must run in ConstrainedLanguage mode with system lock down" {

            try
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -ScriptBlock { param ($sb) & $sb } -ArgumentList $sb | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "ConstrainedLanguage"
        }

        It "ThreadJob script block piped variable must run in ConstrainedLanguage mode with system lock down" {

            try
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = $sb | Start-ThreadJob -ScriptBlock { $input | ForEach-Object { & $_ } } | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "ConstrainedLanguage"
        }

        It "ThreadJob untrusted script file must run in ConstrainedLanguage mode with system lock down" {
            try 
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -File $scriptUntrustedFilePath | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "ConstrainedLanguage"
        }

        It "ThreadJob trusted script file must run in FullLanguage mode with system lock down" {
            try 
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -File $scriptTrustedFilePath | Wait-Job | Receive-Job
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results | Should -BeExactly "FullLanguage"
        }

        It "ThreadJob trusted script file *with* untrusted initialization script must run in ConstrainedLanguage mode with system lock down" {
            try 
            {
                $ExecutionContext.SessionState.LanguageMode = "ConstrainedLanguage"
                Invoke-LanguageModeTestingSupportCmdlet -SetLockdownMode

                $results = Start-ThreadJob -File $scriptTrustedFilePath -InitializationScript { "Hello" } | Wait-Job | Receive-Job 3>&1
            }
            finally
            {
                Invoke-LanguageModeTestingSupportCmdlet -RevertLockdownMode -EnableFullLanguageMode
            }

            $results.Count | Should -BeExactly 3 -Because "Includes init script, file script, warning output"
            $results[0] | Should -BeExactly "Hello" -Because "This is the expected initialization script output"
            $results[1] | Should -BeExactly "ConstrainedLanguage" -Because "This is the expected script file language mode"
        }
    }
}
finally
{
    if ($defaultParamValues -ne $null)
    {
        $Global:PSDefaultParameterValues = $defaultParamValues
    }
}
