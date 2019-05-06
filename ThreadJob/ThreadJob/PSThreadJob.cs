// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Security;
using System.Text;
using System.Threading;

namespace ThreadJob
{
    [Cmdlet(VerbsLifecycle.Start, "ThreadJob")]
    [OutputType(typeof(ThreadJob))]
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
        public Object[] ArgumentList { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        [ValidateRange(1, 1000000)]
        public int ThrottleLimit { get; set; }

        [Parameter(ParameterSetName = ScriptBlockParameterSet)]
        [Parameter(ParameterSetName = FilePathParameterSet)]
        public PSHost StreamingHost { get; set; }

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
                if (StreamingHost != null)
                {
                    _threadJob = new ThreadJob(Name, _command, ScriptBlock, FilePath, InitializationScript, ArgumentList,
                                               InputObject, this, StreamingHost);
                }
                else
                {
                    _threadJob = new ThreadJob(Name, _command, ScriptBlock, FilePath, InitializationScript, ArgumentList,
                                               InputObject, this);
                }

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

    public sealed class ThreadJobSourceAdapter : JobSourceAdapter
    {
        #region Members

        private ConcurrentDictionary<Guid, Job2> _repository;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public ThreadJobSourceAdapter()
        {
            Name = "ThreadJobSourceAdapter";
            _repository = new ConcurrentDictionary<Guid, Job2>();
        }

        #endregion

        #region JobSourceAdapter Implementation

        /// <summary>
        /// NewJob
        /// </summary>
        public override Job2 NewJob(JobInvocationInfo specification)
        {
            var job = specification.Parameters[0][0].Value as ThreadJob;
            if (job != null)
            {
                _repository.TryAdd(job.InstanceId, job);
            }
            return job;
        }

        /// <summary>
        /// GetJobs
        /// </summary>
        public override IList<Job2> GetJobs()
        {
            return _repository.Values.ToArray();
        }

        /// <summary>
        /// GetJobsByName
        /// </summary>
        public override IList<Job2> GetJobsByName(string name, bool recurse)
        {
            List<Job2> rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobsByCommand
        /// </summary>
        public override IList<Job2> GetJobsByCommand(string command, bool recurse)
        {
            List<Job2> rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.Command.Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobByInstanceId
        /// </summary>
        public override Job2 GetJobByInstanceId(Guid instanceId, bool recurse)
        {
            Job2 job;
            if (_repository.TryGetValue(instanceId, out job))
            {
                return job;
            }
            return null;
        }

        /// <summary>
        /// GetJobBySessionId
        /// </summary>
        public override Job2 GetJobBySessionId(int id, bool recurse)
        {
            foreach (var job in _repository.Values)
            {
                if (job.Id == id)
                {
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// GetJobsByState
        /// </summary>
        public override IList<Job2> GetJobsByState(JobState state, bool recurse)
        {
            List<Job2> rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.JobStateInfo.State == state)
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobsByFilter
        /// </summary>
        public override IList<Job2> GetJobsByFilter(Dictionary<string, object> filter, bool recurse)
        {
            throw new PSNotSupportedException();
        }

        /// <summary>
        /// RemoveJob
        /// </summary>
        public override void RemoveJob(Job2 job)
        {
            Job2 removeJob;
            if (_repository.TryGetValue(job.InstanceId, out removeJob))
            {
                removeJob.StopJob();
                _repository.TryRemove(job.InstanceId, out removeJob);
            }
        }

        #endregion
    }

    /// <summary>
    /// ThreadJob
    /// </summary>
    public sealed class ThreadJob : Job2
    {
        #region Private members

        private ScriptBlock _sb;
        private string _filePath;
        private ScriptBlock _initSb;
        private object[] _argumentList;
        private object[] _usingValuesArray;
        private Dictionary<string, object> _usingValuesMap;
        private PSDataCollection<object> _input;
        private Runspace _rs;
        private PowerShell _ps;
        private PSDataCollection<PSObject> _output;
        private bool _runningInitScript;
        private PSHost _streamingHost;

        private const string VERBATIM_ARGUMENT = "--%";

        private static ThreadJobQueue s_JobQueue;

        #endregion

        #region Properties

        /// <summary>
        /// Specifies the job definition for the JobManager
        /// </summary>
        public JobDefinition ThreadJobDefinition
        {
            get;
            private set;
        }

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
            : this(name, command, sb, filePath, initSb, argumentList, inputObject, psCmdlet, null)
        {
        }

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
        /// <param name="streamingHost"></param>
        public ThreadJob(
            string name,
            string command,
            ScriptBlock sb,
            string filePath,
            ScriptBlock initSb,
            object[] argumentList,
            PSObject inputObject,
            PSCmdlet psCmdlet,
            PSHost streamingHost)
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
            _streamingHost = streamingHost;

            this.PSJobTypeName = "ThreadJob";

            // Get script block to run.
            if (!string.IsNullOrEmpty(_filePath))
            {
                _sb = GetScriptBlockFromFile(_filePath, psCmdlet);
                if (_sb == null)
                {
                    throw new InvalidOperationException(Properties.Resources.ResourceManager.GetString("CannotParseScriptFile"));
                }
            }
            else if (_sb == null)
            {
                throw new PSArgumentNullException(Properties.Resources.ResourceManager.GetString("NoScriptToRun"));
            }

            // Create Runspace/PowerShell object and state callback.
            // The job script/command will run in a separate thread associated with the Runspace.
            var iss = InitialSessionState.CreateDefault2();

            // Determine session language mode for Windows platforms
            WarningRecord lockdownWarning = null;
            if (Environment.OSVersion.Platform.ToString().Equals("Win32NT", StringComparison.OrdinalIgnoreCase))
            {
                bool enforceLockdown = (SystemPolicy.GetSystemLockdownPolicy() == SystemEnforcementMode.Enforce);
                if (enforceLockdown && !string.IsNullOrEmpty(_filePath))
                {
                    // If script source is a file, check to see if it is trusted by the lock down policy
                    enforceLockdown = (SystemPolicy.GetLockdownPolicy(_filePath, null) == SystemEnforcementMode.Enforce);

                    if (!enforceLockdown && (_initSb != null))
                    {
                        // Even if the script file is trusted, an initialization script cannot be trusted, so we have to enforce
                        // lock down.  Otherwise untrusted script could be run in FullLanguage mode along with the trusted file script.
                        enforceLockdown = true;
                        lockdownWarning = new WarningRecord(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                Properties.Resources.ResourceManager.GetString("CannotRunTrustedFileInFL"),
                                _filePath));
                    }
                }

                iss.LanguageMode = enforceLockdown ? PSLanguageMode.ConstrainedLanguage : PSLanguageMode.FullLanguage;
            }

            if (_streamingHost != null)
            {
                _rs = RunspaceFactory.CreateRunspace(_streamingHost, iss);
            }
            else
            {
                _rs = RunspaceFactory.CreateRunspace(iss);
            }
            _ps = PowerShell.Create();
            _ps.Runspace = _rs;
            _ps.InvocationStateChanged += (sender, psStateChanged) =>
            {
                var newStateInfo = psStateChanged.InvocationStateInfo;

                // Update Job state.
                switch (newStateInfo.State)
                {
                    case PSInvocationState.Running:
                        SetJobState(JobState.Running);
                        break;

                    case PSInvocationState.Stopped:
                        SetJobState(JobState.Stopped, newStateInfo.Reason, disposeRunspace:true);
                        break;

                    case PSInvocationState.Failed:
                        SetJobState(JobState.Failed, newStateInfo.Reason, disposeRunspace:true);
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
                            SetJobState(JobState.Completed, newStateInfo.Reason, disposeRunspace:true);
                        }
                        break;
                }
            };

            // Get any using variables.
            var usingAsts = _sb.Ast.FindAll(ast => ast is UsingExpressionAst, searchNestedScriptBlocks: true).Cast<UsingExpressionAst>();
            if (usingAsts != null &&
                usingAsts.FirstOrDefault() != null)
            {
                var psVersion = GetPSVersion();

                // Get using variables as an array or dictionary, depending on PowerShell version.
                if (psVersion.Major >= 5)
                {
                    _usingValuesMap = GetUsingValuesAsDictionary(usingAsts, psCmdlet);
                }
                else if (psVersion.Major == 3 || psVersion.Major == 4)
                {
                    _usingValuesArray = GetUsingValuesAsArray(usingAsts, psCmdlet);
                }
            }

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
            if (lockdownWarning != null)
            {
                this.Warning.Add(lockdownWarning);
            }

            this.Debug = _ps.Streams.Debug;
            this.Debug.EnumeratorNeverBlocks = true;

            this.Information = _ps.Streams.Information;
            this.Information.EnumeratorNeverBlocks = true;

            // Create the JobManager job definition and job specification, and add to the JobManager.
            ThreadJobDefinition = new JobDefinition(typeof(ThreadJobSourceAdapter), "", Name);
            Dictionary<string, object> parameterCollection = new Dictionary<string, object>();
            parameterCollection.Add("NewJob", this);
            var jobSpecification = new JobInvocationInfo(ThreadJobDefinition, parameterCollection);
            var newJob = psCmdlet.JobManager.NewJob(jobSpecification);
            System.Diagnostics.Debug.Assert(newJob == this, "JobManager must return this job");
        }

        #endregion

        #region Public methods

        /// <summary>
        /// StartJob
        /// </summary>
        public override void StartJob()
        {
            if (this.JobStateInfo.State != JobState.NotStarted)
            {
                throw new Exception(Properties.Resources.ResourceManager.GetString("CannotStartJob"));
            }

            // Initialize Runspace state
            _rs.Open();

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
            if (disposing)
            {
                if (_ps.InvocationStateInfo.State == PSInvocationState.Running)
                {
                    _ps.Stop();
                }
                _ps.Dispose();

                _input.Complete();
                _output.Complete();
            }

            base.Dispose(disposing);
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

        #region Base class overrides

        /// <summary>
        /// OnStartJobCompleted
        /// </summary>
        /// <param name="eventArgs"></param>
        protected override void OnStartJobCompleted(AsyncCompletedEventArgs eventArgs)
        {
            base.OnStartJobCompleted(eventArgs);
        }

        /// <summary>
        /// StartJobAsync
        /// </summary>
        public override void StartJobAsync()
        {
            this.StartJob();
            this.OnStartJobCompleted(
                new AsyncCompletedEventArgs(null, false, this));
        }

        /// <summary>
        /// StopJob
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void StopJob(bool force, string reason)
        {
            _ps.Stop();
        }

        /// <summary>
        /// OnStopJobCompleted
        /// </summary>
        /// <param name="eventArgs"></param>
        protected override void OnStopJobCompleted(AsyncCompletedEventArgs eventArgs)
        {
            base.OnStopJobCompleted(eventArgs);
        }

        /// <summary>
        /// StopJobAsync
        /// </summary>
        public override void StopJobAsync()
        {
            _ps.BeginStop((iasync) => { OnStopJobCompleted(new AsyncCompletedEventArgs(null, false, this)); }, null);
        }

        /// <summary>
        /// StopJobAsync
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void StopJobAsync(bool force, string reason)
        {
            _ps.BeginStop((iasync) => { OnStopJobCompleted(new AsyncCompletedEventArgs(null, false, this)); }, null);
        }

        #region Not implemented

        /// <summary>
        /// SuspendJob
        /// </summary>
        public override void SuspendJob()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJob
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void SuspendJob(bool force, string reason)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ResumeJobAsync
        /// </summary>
        public override void ResumeJobAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ResumeJob
        /// </summary>
        public override void ResumeJob()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJobAsync
        /// </summary>
        public override void SuspendJobAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJobAsync
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void SuspendJobAsync(bool force, string reason)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// UnblockJobAsync
        /// </summary>
        public override void UnblockJobAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// UnblockJob
        /// </summary>
        public override void UnblockJob()
        {
            throw new NotImplementedException();
        }

        #endregion

        #endregion

        #region Private methods

        // Private methods
        private void RunScript()
        {
            _ps.Commands.Clear();
            _ps.AddScript(_sb.ToString());

            if (_argumentList != null)
            {
                foreach (var arg in _argumentList)
                {
                    _ps.AddArgument(arg);
                }
            }

            // Using variables
            if (_usingValuesMap != null && _usingValuesMap.Count > 0)
            {
                _ps.AddParameter(VERBATIM_ARGUMENT, _usingValuesMap);
            }
            else if (_usingValuesArray != null && _usingValuesArray.Length > 0)
            {
                _ps.AddParameter(VERBATIM_ARGUMENT, _usingValuesArray);
            }

            _ps.BeginInvoke<object, PSObject>(_input, _output);
        }

        private ScriptBlock GetScriptBlockFromFile(string filePath, PSCmdlet psCmdlet)
        {
            if (WildcardPattern.ContainsWildcardCharacters(filePath))
            {
                throw new ArgumentException(Properties.Resources.ResourceManager.GetString("FilePathWildcards"));
            }

            if (!filePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(Properties.Resources.ResourceManager.GetString("FilePathExt"));
            }

            ProviderInfo provider = null;
            string resolvedPath = psCmdlet.GetResolvedProviderPathFromPSPath(filePath, out provider).FirstOrDefault();
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

        private void SetJobState(JobState jobState, Exception reason, bool disposeRunspace = false)
        {
            base.SetJobState(jobState, reason);
            if (disposeRunspace)
            {
                _rs.Dispose();
            }
        }

        private static object[] GetUsingValuesAsArray(IEnumerable<UsingExpressionAst> usingAsts, PSCmdlet psCmdlet)
        {
            return GetUsingValuesAsDictionary(usingAsts, psCmdlet).Values.ToArray();
        }

        private static Dictionary<string, object> GetUsingValuesAsDictionary(IEnumerable<UsingExpressionAst> usingAsts, PSCmdlet psCmdlet)
        {
            Dictionary<string, object> usingValues = new Dictionary<string, object>();

            foreach (var usingAst in usingAsts)
            {
                var varAst = usingAst.SubExpression as VariableExpressionAst;
                if (varAst == null)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.ResourceManager.GetString("UsingNotVariableExpression"), 
                        new object[] { usingAst.Extent.Text });
                    throw new PSInvalidOperationException(msg);
                }

                try
                {
                    var usingValue = psCmdlet.GetVariableValue(varAst.VariablePath.UserPath);
                    var usingKey = GetUsingExpressionKey(usingAst);
                    if (!usingValues.ContainsKey(usingKey))
                    {
                        usingValues.Add(usingKey, usingValue);
                    }
                }
                catch (Exception ex)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.ResourceManager.GetString("UsingVariableNotFound"), 
                        new object[] { usingAst.Extent.Text });
                    throw new PSInvalidOperationException(msg, ex);
                }
            }

            return usingValues;
        }

        /// <summary>
        /// This method creates a dictionary key for a Using expression value that is bound to
        /// a thread job script block parameter.  PowerShell version 5.0+ recognizes this and performs
        /// the correct Using parameter argument binding.
        /// </summary>
        /// <param name="usingAst">A using expression</param>
        /// <returns>Base64 encoded string as the key of the UsingExpressionAst</returns>
        private static string GetUsingExpressionKey(UsingExpressionAst usingAst)
        {
            string usingAstText = usingAst.ToString();
            if (usingAst.SubExpression is VariableExpressionAst)
            {
                usingAstText = usingAstText.ToLowerInvariant();
            }

            return Convert.ToBase64String(Encoding.Unicode.GetBytes(usingAstText.ToCharArray()));
        }

        private static Version GetPSVersion()
        {
            using (var ps = PowerShell.Create())
            {
                try
                {
                    var results = ps.AddScript("$PSVersionTable").Invoke<System.Collections.Hashtable>();
                    if (results.Count == 1)
                    {
                        var versionString = (results[0]["PSVersion"]).ToString();
                        if (Version.TryParse(versionString, out Version version))
                        {
                            return version;
                        }
                    }
                }
                catch (Exception) { }
            }

            return new Version(3, 0);
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
