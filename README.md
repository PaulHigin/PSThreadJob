# PSThreadJob
A PowerShell module for running concurrent jobs based on threads rather than processes

PowerShell's built-in BackgroundJob jobs (Start-Job) are run in separate processes on the local machine. They provide excellent isolation but are resource heavy.  Running hundreds of BackgroundJob jobs can quickly absorb system resources by creating hundreds of processes. There is no throttling mechanism and so all jobs are started immediately and are all run currently.

This module extends the existing PowerShell BackgroundJob to include a new thread based ThreadJob job. It is a lighter weight solution for running concurrent PowerShell scripts that works within the existing PowerShell job infrastructure. So these jobs work with existing PowerShell job cmdlets.

ThreadJob jobs will tend to run much faster because there is lower overhead and they do not use the remoting serialization system as BackgroundJob jobs do. And they will use up fewer system resources. In addition output objects returned from the job will be 'live' since they are not re-hydrated from the serialization system.  However, there is less isolation.  If one ThreadJob job crashes the process then all ThreadJob jobs running in that process will be terminated.

This module exports a single cmdlet, Start-ThreadJob, which works similarly to the existing Start-Job cmdlet. The main difference is that the jobs which are created run in separate threads within the local process.

Also ThreadJob jobs support a ThrottleLimit parameter to limit the number of running jobs, and thus running threads, at a time. If more jobs are started then they go into a queue and wait until the current number of jobs drops below the throttle limit.

## Examples

```powershell
PS C:\> Start-ThreadJob -ScriptBlock { 1..100 | % { sleep 1; "Output $_" } } -ThrottleLimit 2
PS C:\> Start-ThreadJob -ScriptBlock { 1..100 | % { sleep 1; "Output $_" } }
PS C:\> Start-ThreadJob -ScriptBlock { 1..100 | % { sleep 1; "Output $_" } }
PS C:\> get-job

Id     Name            PSJobTypeName   State         HasMoreData     Location             Command
--     ----            -------------   -----         -----------     --------             -------
1      Job1            ThreadJob       Running       True            PowerShell            1..100 | % { sleep 1;...
2      Job2            ThreadJob       Running       True            PowerShell            1..100 | % { sleep 1;...
3      Job3            ThreadJob       NotStarted    False           PowerShell            1..100 | % { sleep 1;...
```

```powershell
PS C:\> $job = Start-ThreadJob { Get-Process -id $pid }
PS C:\> $myProc = Receive-Job $job
# !!Don't do this.  $myProc is a live object!!
PS C:\> $myProc.Kill()
```

```powershell
# start five background jobs each running 1 second
PS C:\> Measure-Command {1..5 | % {Start-Job {Sleep 1}} | Wait-Job} | Select TotalSeconds 
PS C:\> Measure-Command {1..5 | % {Start-ThreadJob {Sleep 1}} | Wait-Job} | Select TotalSeconds

TotalSeconds
------------
   5.7665849 # jobs creation time > 4.7 sec; results may vary
   1.5735008 # jobs creation time < 0.6 sec (8 times less!)
```

## Installing

You can install this module from [PowerShell Gallery](https://www.powershellgallery.com/packages/ThreadJob/1.1.2) using this command:

```powershell
Install-Module -Name ThreadJob -Scope CurrentUser
```
