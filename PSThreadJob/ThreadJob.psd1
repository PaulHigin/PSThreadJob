#
# Module manifest for module 'ThreadJob'
#

@{

# Script module or binary module file associated with this manifest.
RootModule = '.\Microsoft.PowerShell.ThreadJob.dll'

# Version number of this module.
ModuleVersion = '2.0.1'

# ID used to uniquely identify this module
GUID = 'c28d8b9b-75c0-4229-bb43-b60218c47bef'

Author = 'Microsoft Corporation'
CompanyName = 'Microsoft Corporation'
Copyright = '(c) Microsoft Corporation. All rights reserved.'

# Description of the functionality provided by this module
Description = "
PowerShell's built-in BackgroundJob jobs (Start-Job) are run in separate processes on the local machine.
They provide excellent isolation but are resource heavy.  Running hundreds of BackgroundJob jobs can quickly
absorb system resources.

This module extends the existing PowerShell BackgroundJob to include a new thread based ThreadJob job.  This is a 
lighter weight solution for running concurrent PowerShell scripts that works within the existing PowerShell job 
infrastructure.

ThreadJob jobs will tend to run quicker because there is lower overhead and they do not use the remoting serialization 
system.  And they will use up fewer system resources.  In addition output objects returned from the job will be
'live' since they are not re-hydrated from the serialization system.  However, there is less isolation.  If one
ThreadJob job crashes the process then all ThreadJob jobs running in that process will be terminated.

This module exports a single cmdlet, Start-ThreadJob, which works similarly to the existing Start-Job cmdlet.
The main difference is that the jobs which are created run in separate threads within the local process.

One difference is that ThreadJob jobs support a ThrottleLimit parameter to limit the number of running jobs,
and thus active threads, at a time.  If more jobs are started then they go into a queue and wait until the current
number of jobs drops below the throttle limit.

Source for this module is at GitHub.  Please submit any issues there.
https://github.com/PaulHigin/PSThreadJob

Added Runspace cleanup.
Added Using variable expression support.
Added StreamingHost parameter to stream host data writes to a provided host object.
Added Information stream handling.
Bumped version to 2.0.0, and now only support PowerShell version 5.1 and higher.
Fixed using keyword bug with PowerShell preview version, and removed unneeded version check.
"

# Minimum version of the Windows PowerShell engine required by this module
PowerShellVersion = '5.1'

# Cmdlets to export from this module
CmdletsToExport = 'Start-ThreadJob'

}
