#
# Module manifest for module 'ThreadJob'
#

@{

# Script module or binary module file associated with this manifest.
RootModule = '.\ThreadJob.dll'

# Version number of this module.
ModuleVersion = '1.1.2'

# ID used to uniquely identify this module
GUID = '29955884-f6a6-49ba-a071-a4dc8842697f'

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

Added Runspace cleanup.
Added Using variable expression support.
"

# Minimum version of the Windows PowerShell engine required by this module
PowerShellVersion = '3.0'

# Cmdlets to export from this module
CmdletsToExport = 'Start-ThreadJob'

}
