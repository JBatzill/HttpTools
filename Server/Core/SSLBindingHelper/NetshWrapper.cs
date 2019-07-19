using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Batzill.Server.Core.Logging;

namespace Batzill.Server.Core.SSLBindingHelper
{
    public class NetshWrapper : ISSLBindingHelper
    {
        /// <summary>
        /// Group[1]: certHash
        /// </summary>
        private const string NetShShowCertHash = @"Certificate Hash *\: ([0-9a-fA-F]*)";
        /// <summary>
        /// Group[1]: appId
        /// </summary>
        private const string NetShShowAppId = @"Application ID *\: \{([0-9a-fA-F\-]*)\}";

        /// <summary>
        /// Netsh error message when deletion failed because there was no binding in the first place.
        /// </summary>
        private const string NetShDeleteFailedFileNotFound = "The system cannot find the file specified";

        private const int NetshIdleTimeoutInMs = 5000;
        
        public string DefaultEndpointHost
        {
            get
            {
                return "0.0.0.0";
            }
        }

        private Logger logger;

        public NetshWrapper(Logger logger)
        {
            this.logger = logger;
        }

        public bool TryAddOrUpdateCertBinding(string certThumbprint, string appId, string port, string host = "0.0.0.0")
        {
            this.logger?.Log(EventType.ServerSetup, "Try add or update SSL Certificate binding for host:port '{0}:{1}' to cert: '{2}', appId: '{3}'.", host, port, certThumbprint, appId);

            // First check if binding already exists
            if (!this.TryGetExistingBinding(out string existingCertThumprint, out string existingAppId, port, host))
            {
                this.logger?.Log(EventType.SystemError, "Unable to get current SSL Certificate binding informations, stop operation.");
                return false;
            }

            if (string.Equals(existingCertThumprint, certThumbprint, StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(existingAppId, appId, StringComparison.InvariantCultureIgnoreCase))
            {
                this.logger?.Log(EventType.ServerSetup, "Binding already exists, nothing to do here.");
                return true;
            }


            // If there is a different binding on the same port, remove it
            if (!string.IsNullOrEmpty(existingCertThumprint) || !string.IsNullOrEmpty(existingAppId))
            {
                this.logger?.Log(EventType.ServerSetup, "A different SSL cert binding exists for that endpoint, delete it first.");
                if (!this.TryDeleteExistingBinding(port, host))
                {
                    logger?.Log(EventType.SystemError, "Unable to delete existing SSL Certificate binding, stop operation.");
                    return false;
                }
            }

            // Try to add the binding
            if (!this.TryAddCertBinding(certThumbprint, appId, port, host))
            {
                logger?.Log(EventType.SystemError, "Unable to add new SSL Certificate binding, stop operation.");
                return false;
            }

            this.logger?.Log(EventType.ServerSetup, "Successfully changed SSL cert binding for host:port '{0}:{1}' to cert: '{2}', appId: '{3}'.", host, port, certThumbprint, appId);

            return true;
        }

        public bool TryAddCertBinding(string certThumbprint, string appId, string port, string host = "0.0.0.0")
        {
            this.logger?.Log(EventType.ServerSetup, "Attempting to add a SSL cert binding for '{0}:{1}' with certHash: '{2}', appId: '{3}'", host, port, NetShShowCertHash, appId);

            string argument = string.Format(@"http add sslcert {0}={1}:{2} certhash={3} appid={{{4}}} certstorename=my", this.GetEndpointType(host), host, port, certThumbprint, appId);
            Process process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    FileName = "netsh.exe",
                    Arguments = argument
                }
            };

            try
            {
                if (!process.Start() || !process.WaitForExit(NetshWrapper.NetshIdleTimeoutInMs) || process.ExitCode != 0)
                {
                    this.logger?.Log(EventType.SystemError, "Unable to add new SSL Certificate binding!");
                    this.logger?.Log(EventType.SystemError, "netsh ExitCode: {0}", process.ExitCode);
                    this.logger?.Log(EventType.SystemError, "netsh OutputStream: {0}", process.StandardOutput.ReadToEnd());
                    this.logger?.Log(EventType.SystemError, "netsh ErrorStream: {0}", process.StandardError.ReadToEnd());

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "Unable to delete current SSL Certificate binding!");
                this.logger?.Log(EventType.SystemError, ex.ToString());

                return false;
            }
        }

        public bool TryGetExistingBinding(out string certThumbprint, out string appId, string port, string host = "0.0.0.0")
        {
            this.logger?.Log(EventType.ServerSetup, "Attempting to get the SSL cert binding information for '{0}:{1}'", host, port);

            certThumbprint = string.Empty;
            appId = string.Empty;

            // Check if cert binding already exists!
            string argument = string.Format("http show sslcert {0}={1}:{2}", this.GetEndpointType(host), host, port);
            Process process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    FileName = "netsh.exe",
                    Arguments = argument
                }
            };

            try
            {
                if (!process.Start() || !process.WaitForExit(NetshWrapper.NetshIdleTimeoutInMs))
                {
                    this.logger?.Log(EventType.SystemError, "Unable to get current SSL Certificate binding informations!");
                    this.logger?.Log(EventType.SystemError, "netsh ExitCode: {0}", process.ExitCode);
                    this.logger?.Log(EventType.SystemError, "netsh OutputStream: {0}", process.StandardOutput.ReadToEnd());
                    this.logger?.Log(EventType.SystemError, "netsh ErrorStream: {0}", process.StandardError.ReadToEnd());

                    return false;
                }

                string netshOutput = process.StandardOutput.ReadToEnd();
                if (string.IsNullOrEmpty(netshOutput))
                {
                    logger?.Log(EventType.SystemError, "Empty output by netsh!");
                    return false;
                }

                Match certMatch = Regex.Match(netshOutput, NetshWrapper.NetShShowCertHash, RegexOptions.IgnoreCase);
                Match appIdMatch = Regex.Match(netshOutput, NetshWrapper.NetShShowAppId, RegexOptions.IgnoreCase);
                if (certMatch.Success)
                {
                    if (appIdMatch.Success)
                    {
                        certThumbprint = certMatch.Groups[1].Value;
                        appId = appIdMatch.Groups[1].Value;

                        this.logger?.Log(EventType.ServerSetup, "Found existing SSL cert binding for '{0}:{1}': certHash: '{2}', appId: '{3}'", host, port, certThumbprint, appId);

                        return true;
                    }

                    this.logger?.Log(EventType.SystemError, "Found cert hash but no app id!");
                    return false;
                }
                else if (appIdMatch.Success)
                {
                    this.logger?.Log(EventType.SystemError, "Found appId but no cert hash!");
                    return false;
                }

                this.logger?.Log(EventType.ServerSetup, "No SSL cert binding exists yet for '{0}:{1}'", host, port);

                return true;
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "Unable to get current SSL Certificate binding informations!");
                this.logger?.Log(EventType.SystemError, ex.ToString());

                return false;
            }
        }

        public bool TryDeleteExistingBinding(string port, string host = "0.0.0.0")
        {
            this.logger?.Log(EventType.ServerSetup, "Attempting to delete the SSL cert binding for '{0}:{1}'", host, port);

            string argument = string.Format("http delete sslcert {0}={1}:{2}", this.GetEndpointType(host), host, port);
            Process process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    FileName = "netsh.exe",
                    Arguments = argument
                }
            };

            try
            {
                if (!process.Start() || !process.WaitForExit(NetshWrapper.NetshIdleTimeoutInMs) || process.ExitCode != 0)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!string.IsNullOrEmpty(output) && output.Contains(NetshWrapper.NetShDeleteFailedFileNotFound))
                    {
                        this.logger?.Log(EventType.ServerSetup, "Deleting SSL cert binding for ipport={0}:{1} failed because binding didn't exist, return success.", host, port);
                        return true;
                    }

                    this.logger?.Log(EventType.SystemError, "Unable to delete existing SSL Certificate binding!");
                    this.logger?.Log(EventType.SystemError, "netsh ExitCode: {0}", process.ExitCode);
                    this.logger?.Log(EventType.SystemError, "netsh OutputStream: {0}", process.StandardOutput.ReadToEnd());
                    this.logger?.Log(EventType.SystemError, "netsh ErrorStream: {0}", process.StandardError.ReadToEnd());

                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "Unable to delete current SSL Certificate binding!");
                this.logger?.Log(EventType.SystemError, ex.ToString());

                return false;
            }
        }

        private string GetEndpointType(string host)
        {
            return Uri.CheckHostName(host) == UriHostNameType.IPv4 ? "ipport" : "hostnameport";
        }
    }
}
