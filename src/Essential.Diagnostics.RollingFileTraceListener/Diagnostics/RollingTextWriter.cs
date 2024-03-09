using Essential.IO;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Essential.Diagnostics
{
    class RollingTextWriter : IDisposable
    {
        const int _maxStreamRetries = 5;

        private string _currentPath;
        private string _rollingPath;
        private TextWriter _currentWriter;
        private object _fileLock = new object();
        private string _currentPathTemplate;
        private string _rollingPathTemplate;
        private IFileSystem _fileSystem = new FileSystem();
        TraceFormatter traceFormatter = new TraceFormatter();

        public RollingTextWriter(string currentPathTemplate, string rollingPathTemplate)
        {
            _currentPathTemplate = currentPathTemplate;
            _rollingPathTemplate = rollingPathTemplate;
        }

        /// <summary>
        /// Create RollingTextWriter with rollingPathTemplate which might contain 1 environment variable in front.
        /// </summary>
        /// <param name="currentPathTemplate"></param>
        /// <param name="rollingPathTemplate"></param>
        /// <returns></returns>
        public static RollingTextWriter Create(string currentPathTemplate, string rollingPathTemplate)
        {
            return new RollingTextWriter(
                GetTemplatePathWithEnvironmentVariables(currentPathTemplate),
                GetTemplatePathWithEnvironmentVariables(rollingPathTemplate));
        }

        private static string GetTemplatePathWithEnvironmentVariables(string path)
        {
            var segments = path.Split('%');
            if (segments.Length > 3)
            {
                throw new ArgumentException("InitializeData should contain maximum 1 environment variable.", "filePathTemplate");
            }
            else if (segments.Length == 3)
            {
                var variableName = segments[1];
                var rootFolder = Environment.GetEnvironmentVariable(variableName);
                if (String.IsNullOrEmpty(rootFolder))
                {
                    if (variableName.Equals("ProgramData", StringComparison.CurrentCultureIgnoreCase)
                        && (Environment.OSVersion.Version.Major <= 5)) // XP or below: https://msdn.microsoft.com/en-us/library/windows/desktop/ms724832%28v=vs.85%29.aspx
                    {
                        // So the host program could run well in XP and Windows 7 without changing the config file.
                        rootFolder = Path.Combine(Environment.GetEnvironmentVariable("AllUsersProfile"), "Application Data");
                    }
                    else
                    {
                        throw new ArgumentException("Environment variable is not recognized in InitializeData.", "filePathTemplate");
                    }
                }
                var expandedPath = rootFolder + segments[2];
                return expandedPath;
            }
            else
            {
                return path;
            }
        }

        public string FilePathTemplate
        {
            get { return _rollingPathTemplate; }
        }

        public IFileSystem FileSystem
        {
            get { return _fileSystem; }
            set
            {
                lock (_fileLock)
                {
                    _fileSystem = value;
                }
            }
        }

        public void Flush()
        {
            lock (_fileLock)
            {
                if (_currentWriter != null)
                {
                    _currentWriter.Flush();
                }
            }
        }

        public void Write(TraceEventCache eventCache, string value)
        {
            string rollingPath = GetExpandedPath(eventCache, FilePathTemplate);
            lock (_fileLock)
            {
                EnsureCurrentWriter(eventCache, rollingPath);
                _currentWriter.Write(value);
            }
        }

        public void WriteLine(TraceEventCache eventCache, string value)
        {
            string rollingPath = GetExpandedPath(eventCache, FilePathTemplate);
            lock (_fileLock)
            {
                EnsureCurrentWriter(eventCache, rollingPath);
                _currentWriter.WriteLine(value);
            }
        }

        private string RetryOperation(string path, Action<string> operation)
        {
            string retryPath = path;
            int retries = 0;
            Exception exception;
            do
            {
                retryPath = retries > 0 ? GetRetryPath(retryPath, retries) : retryPath;

                try
                {
                    operation(retryPath);
                    exception = null;
                }
                catch (IOException ex)
                {
                    exception = ex;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            while (exception != null && ++retries < _maxStreamRetries);

            if (exception != null)
            {
                throw new InvalidOperationException(Resource_RollingFile.RollingTextWriter_ExhaustedLogfileNames, exception);
            }

            return retryPath;
        }

        private void EnsureCurrentWriter(TraceEventCache eventCache, string rollingPath)
        {
            // NOTE: This is called inside lock(_fileLock)
            if (_currentWriter == null ||
                !string.Equals(_rollingPath, rollingPath, StringComparison.CurrentCultureIgnoreCase))
            {
                if (_currentWriter != null)
                {
                    _currentWriter.Flush();
                    _currentWriter.Close();
                    _currentWriter.Dispose();
                    _currentWriter = null;
                }

                if (!string.IsNullOrEmpty(_currentPath) &&
                    !string.IsNullOrEmpty(_rollingPath) &&
                    !string.Equals(_currentPath, _rollingPath, StringComparison.CurrentCultureIgnoreCase))
                {
                    RetryOperation(_rollingPath, (path) => File.Move(_currentPath, path));
                }

                Stream stream = default(Stream);
                this._currentPath = RetryOperation(
                    GetExpandedPath(eventCache, _currentPathTemplate),
                    (path) => stream = FileSystem.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read));
                this._currentWriter = new StreamWriter(stream);
                this._rollingPath = rollingPath;
            }
        }

        static string GetRetryPath(string path, int num)
        {
            var extension = Path.GetExtension(path);
            return path.Insert(path.Length - extension.Length, "-" + num.ToString(CultureInfo.InvariantCulture));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Portability", "CA1903:UseOnlyApiFromTargetedFramework", MessageId = "System.DateTimeOffset", Justification = "Deliberate dependency, .NET 2.0 SP1 required.")]
        private string GetExpandedPath(TraceEventCache eventCache, string pathTemplate)
        {
            var result = StringTemplate.Format(CultureInfo.CurrentCulture, pathTemplate,
                delegate (string name, out object value)
                {
                    switch (name.ToUpperInvariant())
                    {
                        case "ACTIVITYID":
                            value = Trace.CorrelationManager.ActivityId;
                            break;
                        case "APPDATA":
                            value = traceFormatter.HttpTraceContext.AppDataPath;
                            break;
                        case "APPDOMAIN":
                            value = AppDomain.CurrentDomain.FriendlyName;
                            break;
                        case "APPLICATIONNAME":
                            value = traceFormatter.FormatApplicationName();
                            break;
                        case "DATETIME":
                        case "UTCDATETIME":
                            value = TraceFormatter.FormatUniversalTime(eventCache);
                            break;
                        case "LOCALDATETIME":
                            value = TraceFormatter.FormatLocalTime(eventCache);
                            break;
                        case "MACHINENAME":
                            value = Environment.MachineName;
                            break;
                        case "PROCESSID":
                            value = traceFormatter.FormatProcessId(eventCache);
                            break;
                        case "PROCESSNAME":
                            value = traceFormatter.FormatProcessName();
                            break;
                        case "USER":
                            value = Environment.UserDomainName + "-" + Environment.UserName;
                            break;
                        default:
                            if (name.ToUpperInvariant().Contains("DATETIME"))
                            {
                                string timeZoneId = name.ToUpperInvariant().Replace("DATETIME", string.Empty).Replace("_", " ").Trim();
                                value = TraceFormatter.FormatSpecificTimeZone(eventCache, timeZoneId);
                            }
                            else
                            {
                                value = "{" + name + "}";
                            }
                            return true;
                    }
                    return true;
                });
            return result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_currentWriter != null)
                {
                    _currentWriter.Dispose();
                }
            }
        }
    }
}
