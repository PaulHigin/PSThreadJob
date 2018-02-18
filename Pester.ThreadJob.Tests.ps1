﻿##
## ThreadJob Tests
##

Import-Module -Name ".\ThreadJob.psd1"
if (! $?)
{
    return
}

Describe 'Basic ThreadJob Tests' -Tags 'CI' {

    BeforeAll {

        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force

        $scriptFilePath1 = Join-Path $testdrive "TestThreadJobFile1.ps1"
        @'
        for ($i=0; $i -lt 10; $i++)
        {
            Write-Output "Hello $i"
        }
'@ > $scriptFilePath1

        $scriptFilePath2 = Join-Path $testdrive "TestThreadJobFile2.ps1"
        @'
        param ($arg1, $arg2)
        Write-Output $arg1
        Write-Output $arg2
'@ > $scriptFilePath2

        $scriptFilePath3 = Join-Path $testdrive "TestThreadJobFile3.ps1"
        @'
        $input | foreach {
            Write-Output $_
        }
'@ > $scriptFilePath3

    }

    It 'ThreadJob with ScriptBlock' {

        $job = Start-ThreadJob -ScriptBlock { "Hello" }
        $results = $job | Receive-Job -Wait
        $results | Should be "Hello"
    }

    It 'ThreadJob with ScriptBlock and Initialization script' {

        $job = Start-ThreadJob -ScriptBlock { "Goodbye" } -InitializationScript { "Hello" }
        $results = $job | Receive-Job -Wait
        $results[0] | Should be "Hello"
        $results[1] | Should be "Goodbye"
    }

    It 'ThreadJob with ScriptBlock and Argument list' {

        $job = Start-ThreadJob -ScriptBlock { param ($arg1, $arg2) $arg1; $arg2 } -ArgumentList @("Hello","Goodbye")
        $results = $job | Receive-Job -Wait
        $results[0] | Should be "Hello"
        $results[1] | Should be "Goodbye"
    }

    It 'ThreadJob with ScriptBlock and piped input' {

        $job = "Hello","Goodbye" | Start-ThreadJob -ScriptBlock { $input | foreach { $_ } }
        $results = $job | Receive-Job -Wait
        $results[0] | Should be "Hello"
        $results[1] | Should be "Goodbye"
    }

    It 'ThreadJob with ScriptFile' {

        $job = Start-ThreadJob -FilePath $scriptFilePath1
        $results = $job | Receive-Job -Wait
        $results.Count | Should be 10
        $results[9] | Should be "Hello 9"
    }

    It 'ThreadJob with ScriptFile and Initialization script' {

        $job = Start-ThreadJob -FilePath $scriptFilePath1 -Initialization { "Goodbye" }
        $results = $job | Receive-Job -Wait
        $results.Count | Should be 11
        $results[0] | Should be "Goodbye"
    }

    It 'ThreadJob with ScriptFile and Argument list' {

        $job = Start-ThreadJob -FilePath $scriptFilePath2 -ArgumentList @("Hello","Goodbye")
        $results = $job | Receive-Job -Wait
        $results[0] | Should be "Hello"
        $results[1] | Should be "Goodbye"
    }

    It 'ThreadJob with ScriptFile and piped input' {

        $job = "Hello","Goodbye" | Start-ThreadJob -FilePath $scriptFilePath3
        $results = $job | Receive-Job -Wait
        $results[0] | Should be "Hello"
        $results[1] | Should be "Goodbye"
    }

    It 'ThreadJob ThrottleLimit and Queue' {

        try
        {
            # Start four thread jobs with ThrottleLimit set to two
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
            Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 2
            Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
            Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
            Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }

            # Allow jobs to start
            Start-Sleep -Seconds 1

            $numRunningThreadJobs = (Get-Job | where { ($_.PSJobTypeName -eq "ThreadJob") -and ($_.State -eq "Running") }).Count
            $numQueuedThreadJobs = (Get-Job | where { ($_.PSJobTypeName -eq "ThreadJob") -and ($_.State -eq "NotStarted") }).Count

            $numRunningThreadJobs | Should be 2
            $numQueuedThreadJobs | Should be 2
        }
        finally
        {
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        }

        $numThreadJobs = (Get-Job | where PSJobTypeName -eq "ThreadJob").Count
        $numThreadJobs | Should be 0
    }

    It 'ThreadJob Runspaces should be cleaned up at completion' {

        try
        {
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
            $rsStartCount = (Get-Runspace).Count

            # Start four thread jobs with ThrottleLimit set to two
            $Job1 = Start-ThreadJob -ScriptBlock { "Hello 1!" } -ThrottleLimit 5
            $job2 = Start-ThreadJob -ScriptBlock { "Hello 2!" }
            $job3 = Start-ThreadJob -ScriptBlock { "Hello 3!" }
            $job4 = Start-ThreadJob -ScriptBlock { "Hello 4!" }

            $job1,$job2,$job3,$job4 | Wait-Job

            # Allow for clean to happen
            Start-Sleep -Seconds 1

            (Get-Runspace).Count | Should Be $rsStartCount
        }
        finally
        {
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        }
    }

    It 'ThreadJob Runspaces should be cleaned up after job removal' {

        try {
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
            $rsStartCount = (Get-Runspace).Count

            # Start four thread jobs with ThrottleLimit set to two
            $Job1 = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 2
            $job2 = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
            $job3 = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
            $job4 = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }

            # Allow jobs to start
            Start-Sleep -Seconds 1

            (Get-Runspace).Count | Should Be ($rsStartCount + 4)

            # Stop two jobs
            $job1 | Remove-Job -Force
            $job3 | Remove-Job -Force

            (Get-Runspace).Count | Should Be ($rsStartCount + 2)

        }
        finally
        {
            Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        }

        (Get-Runspace).Count | Should Be $rsStartCount
    }

    It 'ThreadJob jobs should work with Receive-Job -AutoRemoveJob' {

        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force

        $job1 = Start-ThreadJob -ScriptBlock { 1..2 | foreach { Start-Sleep -Seconds 1; "Output $_" } } -ThrottleLimit 2
        $job2 = Start-ThreadJob -ScriptBlock { 1..2 | foreach { Start-Sleep -Seconds 1; "Output $_" } }
        $job3 = Start-ThreadJob -ScriptBlock { 1..2 | foreach { Start-Sleep -Seconds 1; "Output $_" } }
        $job4 = Start-ThreadJob -ScriptBlock { 1..2 | foreach { Start-Sleep -Seconds 1; "Output $_" } }

        $null = $job1,$job2,$job3,$job4 | Receive-Job -Wait -AutoRemoveJob

        (Get-Job | where PSJobTypeName -eq "ThreadJob").Count | Should Be 0
    }

    It 'ThreadJob passes Information stream' {

        $job = Start-ThreadJob -ScriptBlock { Write-Information "My Info"}
        $null = $job | Receive-Job -Wait -InformationVariable info
        $info  | Should be "My Info"
        $job | Remove-Job
    }
}
