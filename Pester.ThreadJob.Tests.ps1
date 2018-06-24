﻿# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

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

        $scriptFilePath4 = Join-Path $testdrive "TestThreadJobFile4.ps1"
        @'
        Write-Output $using:Var1
        Write-Output $($using:Array1)[2]
        Write-Output @(,$using:Array1)
'@ > $scriptFilePath4

        $scriptFilePath5 = Join-Path $testdrive "TestThreadJobFile5.ps1"
        @'
        param ([string]$param1)
        Write-Output "$param1 $using:Var1 $using:Var2"
'@ > $scriptFilePath5
    }

    AfterEach {

        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
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

    It 'ThreadJob with ScriptBlock and Using variables' {

        $Var1 = "Hello"
        $Var2 = "Goodbye"
        $Var3 = 102
        $Var4 = 1..5
        $global:GVar1 = "GlobalVar"
        $job = Start-ThreadJob -ScriptBlock {
            Write-Output $using:Var1
            Write-Output $using:Var2
            Write-Output $using:Var3
            Write-Output ($using:Var4)[1]
            Write-Output @(,$using:Var4)
            Write-Output $using:GVar1
        }

        $results = $job | Receive-Job -Wait
        $results[0] | Should Be $Var1
        $results[1] | Should Be $Var2
        $results[2] | Should Be $Var3
        $results[3] | Should Be 2
        $results[4] | Should Be $Var4
        $results[5] | Should Be $global:GVar1
    }

    It 'ThreadJob with ScriptBlock and Using variables and argument list' {

        $Var1 = "Hello"
        $Var2 = 52
        $job = Start-ThreadJob -ScriptBlock {
            param ([string] $param1)

            "$using:Var1 $param1 $using:Var2"
        } -ArgumentList "There"

        $results = $job | Receive-Job -Wait
        $results | Should Be "Hello There 52"
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

    It 'ThreadJob with ScriptFile and Using variables' {

        $Var1 = "Hello!"
        $Array1 = 1..10

        $job = Start-ThreadJob -FilePath $scriptFilePath4
        $results = $job | Receive-Job -Wait
        $results[0] | Should be $Var1
        $results[1] | Should be 3
        $results[2] | Should be $Array1
    }

    It 'ThreadJob with ScriptFile and Using variables with argument list' {

        $Var1 = "There"
        $Var2 = 60
        $job = Start-ThreadJob -FilePath $scriptFilePath5 -ArgumentList "Hello"
        $results = $job | Receive-Job -Wait
        $results | Should Be "Hello There 60"
    }

    It 'ThreadJob with terminating error' {

        $job = Start-ThreadJob -ScriptBlock { throw "MyError!" }
        $job | Wait-Job
        $job.JobStateInfo.Reason.Message | Should Be "MyError!"
    }

    # Information stream is only available since PS v5
    if ($PSVersionTable.PSVersion -ge '5.0')
    {
        It 'ThreadJob passes Information stream' {

            $job = Start-ThreadJob -ScriptBlock { Write-Information "My Info"}
            $null = $job | Receive-Job -Wait -InformationVariable info
            $info  | Should be "My Info"
        }

        It 'ThreadJob does not send Write-Host output to pipeline' {

            $job = Start-ThreadJob -ScriptBlock { Write-Host "My message" }
            $result = $job | Receive-Job -Wait 6>$null
            $result.Count  | Should be 0
            $job | Remove-Job
        }
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
}

Describe 'Job2 Tests' -Tags 'CI' {

    AfterEach {

        Get-Job | where PSJobTypeName -eq "ThreadJob" | Remove-Job -Force
    }

    It 'Verifies StopJob API' {

        $job = Start-ThreadJob -ScriptBlock { Start-Sleep -Seconds 60 } -ThrottleLimit 5
        # Give it a chance to start
        Start-Sleep -Milliseconds 500
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
    }

    It 'Verifies JobSourceAdapter Get-Jobs' {

        $job = Start-ThreadJob -ScriptBlock { "Hello" } | Wait-Job

        $getJob = Get-Job -InstanceId $job.InstanceId 2>$null
        $getJob | Should Be $job

        $getJob = Get-Job -Name $job.Name 2>$null
        $getJob | Should Be $job

        $getJob = Get-Job -Command ' "hello" ' 2>$null
        $getJob | Should Be $job

        $getJob = Get-Job -State $job.JobStateInfo.State 2>$null
        $getJob | Should Be $job

        $getJob = Get-Job -Id $job.Id 2>$null
        $getJob | Should Be $job

        # Get-Job -Filter is not supported
        $result = Get-Job -Filter @{Id = ($job.Id)} 3>$null
        $result | Should Be $null
    }

    It 'Verifies terminating job error' {

        $job = Start-ThreadJob -ScriptBlock { throw "My Job Error!" } | Wait-Job
        $results = $job | Receive-Job 2>&1
        $results.ToString() | Should Be "My Job Error!"
    }
}
