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

        # Start four thread jobs with ThrottleLimit set to two
        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 2
        Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
        Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }
        Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }

        $numRunningThreadJobs = (Get-Job | where { ($_.PSJobTypeName -eq "ThreadJob") -and ($_.State -eq "Running") }).Count
        $numQueuedThreadJobs = (Get-Job | where { ($_.PSJobTypeName -eq "ThreadJob") -and ($_.State -eq "NotStarted") }).Count

        $numRunningThreadJobs | Should be 2
        $numQueuedThreadJobs | Should be 2

        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
        $numThreadJobs = (Get-Job | where PSJobTypeName -eq "ThreadJob").Count
        $numThreadJobs | Should be 0
    }
}

Describe 'Job2 Tests' -Tags 'CI' {

    It 'Verifies StopJob API' {

        $job = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 5
        $job.StopJob($true, "No Reason")
        $job.JobStateInfo.State | Should Be "Stopped"
    }

    It 'Verifies StopJobAsync API' {

        $job = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 5
        $job.StopJobAsync($true, "No Reason")
        Wait-Job $job
        $job.JobStateInfo.State | Should Be "Stopped"
    }

    It 'Verifies StartJobAsync API' {

        $jobRunning = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 1
        $jobNotRunning = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 }

        $jobNotRunning.JobStateInfo.State | Should Be "NotStarted"

        # StartJobAsync starts jobs synchronously for ThreadJob jobs
        $jobNotRunning.StartJobAsync()
        $jobNotRunning.JobStateInfo.State | Should Be "Running"

        Get-Job | Where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
    }

    It 'Verifies terminating job error' {

        $job = Start-ThreadJob -ScriptBlock { throw "My Job Error!" } | Wait-Job
        $results = $job | Receive-Job 2>&1
        $results.ToString() | Should Be "My Job Error!"
    }
}
