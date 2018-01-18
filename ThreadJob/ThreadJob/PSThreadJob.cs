using System;
using System.Linq;
using System.Threading;
using System.Resources;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Language;
using System.Management.Automation.Host;

namespace ThreadJob
{
    [Cmdlet(VerbsLifecycle.Start, "ThreadJob")]
    [OutputType(typeof(Job2))]
    public sealed class StartThreadJobCommand : PSCmdlet
    {
        #region Private members

        private bool _processFirstRecord;
        private string _command;
        private ThreadJob _threadJob;

        #endregion

        #region Parameters

        private const string ScriptBlockParameterSet = "ScriptBlock";
        private const string FilePathParameterSet = "FilePath";

        [Parameter(ParameterSetName = ScriptBlockParameterSet, Mandatory=true, Position=0)]
        [ValidateNotNullAttribute]
        public ScriptBlock ScriptBlock { get; set; }

        [Parameter(ParameterSetName = FilePathParameterSet, Mandatory=true, Position=0)]
        [ValidateNotNullOrEmpty]
        public string FilePath { get; set; }
    
        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        [ValidateNotNull]
        public ScriptBlock InitializationScript { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet, ValueFromPipeline=true)]
        [Parameter(ParameterSetName = FilePathParameterSet, ValueFromPipeline=true)]
        [ValidateNotNull]
        public PSObject InputObject { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        [ValidateNotNullOrEmpty]
        public Object[] ArgumentList { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        [ValidateRange(1, 50)]
        public int ThrottleLimit { get; set; }

        #endregion

        #region Overrides

        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (ParameterSetName.Equals(ScriptBlockParameterSet))
            {
                _command = ScriptBlock.ToString();
            }
            else
            {
                _command = FilePath;
            }
        }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if (!_processFirstRecord)
            {
                _threadJob = new ThreadJob(Name, _command, ScriptBlock, FilePath, InitializationScript, ArgumentList,
                                           InputObject, this);
                ThreadJob.StartJob(_threadJob, ThrottleLimit);
                WriteObject(_threadJob);

                _processFirstRecord = true;
            }
            else
            {
                // Inject input.
                if (InputObject != null)
                {
                    _threadJob.InjectInput(InputObject);
                }
            }
        }

        protected override void EndProcessing()
        {
            base.EndProcessing();

            _threadJob.CloseInputStream();
        }

        #endregion
    }

    /// <summary>
    /// ThreadJob
    /// </summary>
    public sealed class ThreadJob : Job
    {
        #region Private members

        private ScriptBlock _sb;
        private string _filePath;
        private ScriptBlock _initSb;
        private object[] _argumentList;
        private PSDataCollection<object> _input;
        private Runspace _rs;
        private PowerShell _ps;
        private PSDataCollection<PSObject> _output;
        private bool _runningInitScript;

        private static ThreadJobQueue s_JobQueue;

        #endregion

        #region Constructors

        // Constructors
        static ThreadJob()
        {
            s_JobQueue = new ThreadJobQueue(5);
        }

        private ThreadJob()
        { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="command"></param>
        /// <param name="sb"></param>
        /// <param name="filePath"></param>
        /// <param name="initSb"></param>
        /// <param name="argumentList"></param>
        /// <param name="inputObject"></param>
        /// <param name="psCmdlet"></param>
        public ThreadJob(
            string name,
            string command,
            ScriptBlock sb,
            string filePath,
            ScriptBlock initSb,
            object[] argumentList,
            PSObject inputObject,
            PSCmdlet psCmdlet)
            : base(command, name)
        {
            _sb = sb;
            _filePath = filePath;
            _initSb = initSb;
            _argumentList = argumentList;
            _input = new PSDataCollection<object>();
            if (inputObject != null)
            {
                _input.Add(inputObject);
            }
            _output = new PSDataCollection<PSObject>();

            this.PSJobTypeName = "ThreadJob";

            // Create host object for thread jobs.
            ThreadJobHost host = new ThreadJobHost();
            HookupHostDataDelegates(host);

            // Create Runspace/PowerShell object and state callback.
            // The job script/command will run in a separate thread associated with the Runspace.
            _rs = RunspaceFactory.CreateRunspace(host);
            _rs.Open();
            _ps = PowerShell.Create();
            _ps.Runspace = _rs;
            _ps.InvocationStateChanged += (sender, psStateChanged) =>
            {
                // Update Job state.
                switch (psStateChanged.InvocationStateInfo.State)
                {
                    case PSInvocationState.Running:
                        SetJobState(JobState.Running);
                        break;

                    case PSInvocationState.Stopped:
                        SetJobState(JobState.Stopped);
                        break;

                    case PSInvocationState.Failed:
                        SetJobState(JobState.Failed);
                        break;

                    case PSInvocationState.Completed:
                        if (_runningInitScript)
                        {
                            // Begin running main script.
                            _runningInitScript = false;
                            RunScript();
                        }
                        else
                        {
                            SetJobState(JobState.Completed);
                        }
                        break;
                }
            };

            // Hook up data streams.
            this.Output = _output;
            this.Output.EnumeratorNeverBlocks = true;

            this.Error = _ps.Streams.Error;
            this.Error.EnumeratorNeverBlocks = true;

            this.Progress = _ps.Streams.Progress;
            this.Progress.EnumeratorNeverBlocks = true;

            this.Verbose = _ps.Streams.Verbose;
            this.Verbose.EnumeratorNeverBlocks = true;

            this.Warning = _ps.Streams.Warning;
            this.Warning.EnumeratorNeverBlocks = true;

            this.Debug = _ps.Streams.Debug;
            this.Debug.EnumeratorNeverBlocks = true;

            // Add to job repository
            if (psCmdlet != null)
            {
                psCmdlet.JobRepository.Add(this);
            }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// StartJob
        /// </summary>
        public void StartJob()
        {
            if (this.JobStateInfo.State != JobState.NotStarted)
            {
                throw new Exception(Properties.Resources.ResourceManager.GetString("CannotStartJob"));
            }

            // If initial script block provided then execute.
            if (_initSb != null)
            {
                // Run initial script and then the main script.
                _ps.Commands.Clear();
                _ps.AddScript(_initSb.ToString());
                _runningInitScript = true;
                _ps.BeginInvoke<object, PSObject>(_input, _output);
            }
            else
            {
                // Run main script.
                RunScript();
            }
        }

        /// <summary>
        /// InjectInput
        /// </summary>
        /// <param name="psObject"></param>
        public void InjectInput(PSObject psObject)
        {
            if (psObject != null)
            {
                _input.Add(psObject);
            }
        }

        /// <summary>
        /// CloseInputStream
        /// </summary>
        public void CloseInputStream()
        {
            _input.Complete();
        }

        /// <summary>
        /// StartJob
        /// </summary>
        /// <param name="job"></param>
        /// <param name="throttleLimit"></param>
        public static void StartJob(ThreadJob job, int throttleLimit)
        {
            s_JobQueue.EnqueueJob(job, throttleLimit);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _input.Complete();
                _output.Complete();
                if (_ps != null)
                {
                    _ps.Dispose();
                    _ps = null;
                }

                if (_rs != null)
                {
                    _rs.Dispose();
                    _rs = null;
                }
            }
        }

        /// <summary>
        /// StatusMessage
        /// </summary>
        public override string StatusMessage
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// HasMoreData
        /// </summary>
        public override bool HasMoreData
        {
            get
            {
                return (this.Output.Count > 0 ||
                        this.Error.Count > 0 ||
                        this.Progress.Count > 0 ||
                        this.Verbose.Count > 0 ||
                        this.Debug.Count > 0 ||
                        this.Warning.Count > 0);
            }
        }

        /// <summary>
        /// Location
        /// </summary>
        public override string Location
        {
            get { return "PowerShell"; }
        }

        /// <summary>
        /// StopJob
        /// </summary>
        public override void StopJob()
        {
            _ps.Stop();
        }

        /// <summary>
        /// ReportError
        /// </summary>
        /// <param name="e"></param>
        public void ReportError(Exception e)
        {
            try
            {
                SetJobState(JobState.Failed);

                this.Error.Add(
                        new ErrorRecord(e, "ThreadJobError", ErrorCategory.InvalidOperation, this));
            }
            catch (ObjectDisposedException)
            {
                // Ignore. Thrown if Job is disposed (race condition.).
            }
            catch (PSInvalidOperationException)
            {
                // Ignore.  Thrown if Error collection is closed (race condition.).
            }
        }

        #endregion

        #region Private methods

        // Private methods
        private void RunScript()
        {
            _ps.Commands.Clear();

            if (_sb != null)
            {
                // TODO: Add support for V3 using variables.  This is currently internal access only.
                // object[] usingValues = ScriptBlockToPowerShellConverter.GetUsingValues(scriptBlock, Context, null);
                // See: private PowerShell PSExecutionCmdlet.GetPowerShellForPsv3OrLater(string serverPsVersion)

                _ps.AddScript(_sb.ToString());
            }
            else if (!string.IsNullOrEmpty(_filePath))
            {
                ScriptBlock sb = GetScriptBlockFromFile(_filePath);
                if (sb == null)
                {
                    throw new InvalidOperationException(Properties.Resources.ResourceManager.GetString("CannotParseScriptFile"));
                }

                _ps.AddScript(sb.ToString());
            }

            if (_argumentList != null)
            {
                foreach (var arg in _argumentList)
                {
                    _ps.AddArgument(arg);
                }
            }

            _ps.BeginInvoke<object, PSObject>(_input, _output);
        }

        private ScriptBlock GetScriptBlockFromFile(string filePath)
        {
            if (WildcardPattern.ContainsWildcardCharacters(filePath))
            {
                throw new ArgumentException(Properties.Resources.ResourceManager.GetString("FilePathWildcards"));
            }

            if (!filePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(Properties.Resources.ResourceManager.GetString("FilePathExt"));
            }

            string resolvedPath = null;
            if (_rs.SessionStateProxy.Path != null)
            {
                ProviderInfo provider = null;
                resolvedPath = _rs.SessionStateProxy.Path.GetResolvedProviderPathFromPSPath(filePath, out provider).FirstOrDefault();
            }
            else
            {
                resolvedPath = filePath;
            }

            if (!string.IsNullOrEmpty(resolvedPath))
            {
                Token[] tokens;
                ParseError[] errors;
                ScriptBlockAst scriptBlockAst = Parser.ParseFile(resolvedPath, out tokens, out errors);
                if (scriptBlockAst != null && errors.Length == 0)
                {
                    return scriptBlockAst.GetScriptBlock();
                }

                foreach (var error in errors)
                {
                    this.Error.Add(
                        new ErrorRecord(
                            new ParseException(error.Message), "ThreadJobError", ErrorCategory.InvalidData, this));
                }
            }

            return null;
        }

        private void HookupHostDataDelegates(ThreadJobHost host)
        {
            ThreadJobHostUI hostUI = host.UI as ThreadJobHostUI;
            System.Diagnostics.Debug.Assert(hostUI != null, "Host UI cannot be null.");

            hostUI.Output.DataAdded += (sender, dataAddedEventArgs) =>
            {
                Collection<PSObject> output = hostUI.Output.ReadAll();
                foreach (var item in output)
                {
                    _output.Add(item);
                }
            };

            hostUI.Error.DataAdded += (sender, dataAddedEventArgs) =>
                {
                    Collection<ErrorRecord> error = hostUI.Error.ReadAll();
                    foreach (var item in error)
                    {
                        Error.Add(item);
                    }
                };
        }

        #endregion
    }

    /// <summary>
    /// ThreadJobHostUI
    /// </summary>
    internal sealed class ThreadJobHostUI : PSHostUserInterface
    {
        #region Private members

        private PSDataCollection<PSObject> _output;
        private PSDataCollection<ErrorRecord> _error;

        #endregion

        #region Public properties

        /// <summary>
        /// Output
        /// </summary>
        public PSDataCollection<PSObject> Output
        {
            get { return _output; }
        }

        /// <summary>
        /// Error
        /// </summary>
        public PSDataCollection<ErrorRecord> Error
        {
            get { return _error; }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public ThreadJobHostUI()
        {
            _output = new PSDataCollection<PSObject>();
            _error = new PSDataCollection<ErrorRecord>();
        }

        #endregion

        #region Public overrides

        /// <summary>
        /// RawUI
        /// </summary>
        public override PSHostRawUserInterface RawUI
        {
            get { return null; }
        }

        /// <summary>
        /// Prompt
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="message"></param>
        /// <param name="descriptions"></param>
        /// <returns></returns>
        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// PromptForChoice
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="message"></param>
        /// <param name="choices"></param>
        /// <param name="defaultChoice"></param>
        /// <returns></returns>
        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// PromptForCredential
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="message"></param>
        /// <param name="userName"></param>
        /// <param name="targetName"></param>
        /// <returns></returns>
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// PromptForCredential
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="message"></param>
        /// <param name="userName"></param>
        /// <param name="targetName"></param>
        /// <param name="allowedCredentialTypes"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ReadLine
        /// </summary>
        /// <returns></returns>
        public override string ReadLine()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ReadLineAsSecureString
        /// </summary>
        /// <returns></returns>
        public override System.Security.SecureString ReadLineAsSecureString()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="foregroundColor"></param>
        /// <param name="backgroundColor"></param>
        /// <param name="value"></param>
        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            Write(value);
        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="value"></param>
        public override void Write(string value)
        {
            _output.Add(
                new PSObject(value));
        }

        /// <summary>
        /// WriteErrorLine
        /// </summary>
        /// <param name="value"></param>
        public override void WriteErrorLine(string value)
        {
            _error.Add(
                new ErrorRecord(new RuntimeException(value), null, ErrorCategory.NotSpecified, null));
        }

        /// <summary>
        /// WriteLine
        /// </summary>
        /// <param name="value"></param>
        public override void WriteLine(string value)
        {
            _output.Add(
                new PSObject(value + "\r"));
        }

        /// <summary>
        /// WriteProgress
        /// </summary>
        /// <param name="sourceId"></param>
        /// <param name="record"></param>
        public override void WriteProgress(long sourceId, ProgressRecord record)
        {
            // NOOP since this is taken care of within "InternalHostUserInterface.cs"
        }

        /// <summary>
        /// WriteVerboseLine
        /// </summary>
        /// <param name="message"></param>
        public override void WriteVerboseLine(string message)
        {
            // NOOP since this is taken care of within "InternalHostUserInterface.cs"
        }

        /// <summary>
        /// WriteWarningLine
        /// </summary>
        /// <param name="message"></param>
        public override void WriteWarningLine(string message)
        {
            // NOOP since this is taken care of within "InternalHostUserInterface.cs"
        }

        /// <summary>
        /// WriteDebugLine
        /// </summary>
        /// <param name="message"></param>
        public override void WriteDebugLine(string message)
        {
            // NOOP since this is taken care of within "InternalHostUserInterface.cs"
        }

        #endregion
    }

    /// <summary>
    /// ThreadJobHost
    /// </summary>
    internal sealed class ThreadJobHost : PSHost
    {
        #region Private members

        private const string _name = "ThreadJobHost";
        private readonly Version _version;
        private readonly Guid _instanceId;
        private ThreadJobHostUI _ui;

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public ThreadJobHost()
        {
            _version = new Version(1, 0);
            _instanceId = Guid.NewGuid();
            _ui = new ThreadJobHostUI();
        }

        #endregion

        #region Public overrides

        /// <summary>
        /// Name
        /// </summary>
        public override string Name
        {
            get { return _name; }
        }

        /// <summary>
        /// Version
        /// </summary>
        public override Version Version
        {
            get { return _version; }
        }

        /// <summary>
        /// InstanceId
        /// </summary>
        public override Guid InstanceId
        {
            get { return _instanceId; }
        }

        /// <summary>
        /// UI
        /// </summary>
        public override PSHostUserInterface UI
        {
            get { return _ui; }
        }

        /// <summary>
        /// CurrentCulture
        /// </summary>
        public override System.Globalization.CultureInfo CurrentCulture
        {
            get { return System.Globalization.CultureInfo.CurrentCulture; }
        }

        /// <summary>
        /// CurrentUICulture
        /// </summary>
        public override System.Globalization.CultureInfo CurrentUICulture
        {
            get { return System.Globalization.CultureInfo.CurrentUICulture; }
        }

        /// <summary>
        /// SetShouldExit
        /// </summary>
        /// <param name="exitCode"></param>
        public override void SetShouldExit(int exitCode)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// EnterNestedPrompt
        /// </summary>
        public override void EnterNestedPrompt()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ExitNestedPrompt
        /// </summary>
        public override void ExitNestedPrompt()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// NotifyBeginApplication
        /// </summary>
        public override void NotifyBeginApplication()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// NotifyEndApplication
        /// </summary>
        public override void NotifyEndApplication()
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    /// <summary>
    /// ThreadJobQueue
    /// </summary>
    internal sealed class ThreadJobQueue
    {
        #region Private members

        // Private members
        ConcurrentQueue<ThreadJob> _jobQueue = new ConcurrentQueue<ThreadJob>();
        object _syncObject = new object();
        int _throttleLimit = 5;
        int _currentJobs;
        bool _haveRunningJobs;
        private ManualResetEvent _processJobsHandle = new ManualResetEvent(true);

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public ThreadJobQueue()
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="throttleLimit"></param>
        public ThreadJobQueue(int throttleLimit)
        {
            _throttleLimit = throttleLimit;
        }

        #endregion

        #region Public properties

        /// <summary>
        /// ThrottleLimit
        /// </summary>
        public int ThrottleLimit
        {
            get { return _throttleLimit; }
            set
            {
                if (value > 0)
                {
                    lock (_syncObject)
                    {
                        _throttleLimit = value;
                        if (_currentJobs < _throttleLimit)
                        {
                            _processJobsHandle.Set();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// CurrentJobs
        /// </summary>
        public int CurrentJobs
        {
            get { return _currentJobs; }
        }

        /// <summary>
        /// Count
        /// </summary>
        public int Count
        {
            get { return _jobQueue.Count; }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// EnqueueJob
        /// </summary>
        /// <param name="job"></param>
        /// <param name="throttleLimit"></param>
        public void EnqueueJob(ThreadJob job, int throttleLimit)
        {
            if (job == null)
            {
                throw new ArgumentNullException("job");
            }

            ThrottleLimit = throttleLimit;
            job.StateChanged += new EventHandler<JobStateEventArgs>(HandleJobStateChanged);

            lock (_syncObject)
            {
                _jobQueue.Enqueue(job);

                if (_haveRunningJobs)
                {
                    return;
                }

                if (_jobQueue.Count > 0)
                {
                    _haveRunningJobs = true;
                    System.Threading.ThreadPool.QueueUserWorkItem(new WaitCallback(ServiceJobs));
                }
            }
        }

        #endregion

        #region Private methods

        private void HandleJobStateChanged(object sender, JobStateEventArgs e)
        {
            ThreadJob job = sender as ThreadJob;
            JobState state = e.JobStateInfo.State;
            if (state == JobState.Completed ||
                state == JobState.Stopped ||
                state == JobState.Failed)
            {
                job.StateChanged -= new EventHandler<JobStateEventArgs>(HandleJobStateChanged);
                DecrementCurrentJobs();
            }
        }

        private void IncrementCurrentJobs()
        {
            lock (_syncObject)
            {
                if (++_currentJobs >= _throttleLimit)
                {
                    _processJobsHandle.Reset();
                }
            }
        }

        private void DecrementCurrentJobs()
        {
            lock (_syncObject)
            {
                if ((_currentJobs > 0) &&
                    (--_currentJobs < _throttleLimit))
                {
                    _processJobsHandle.Set();
                }
            }
        }

        private void ServiceJobs(object toProcess)
        {
            while (true)
            {
                lock (_syncObject)
                {
                    if (_jobQueue.Count == 0)
                    {
                        _haveRunningJobs = false;
                        return;
                    }
                }

                _processJobsHandle.WaitOne();

                ThreadJob job;
                if (_jobQueue.TryDequeue(out job))
                {
                    try
                    {
                        // Start job running on its own thread/runspace.
                        IncrementCurrentJobs();
                        job.StartJob();
                    }
                    catch (Exception e)
                    {
                        DecrementCurrentJobs();
                        job.ReportError(e);
                    }
                }
            }
        }

        #endregion
    }
}
