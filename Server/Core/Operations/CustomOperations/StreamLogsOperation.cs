using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Batzill.Server.Core.Logging;
using Batzill.Server.Core.ObjectModel;
using System.Threading;
using Batzill.Server.Core.Settings;
using Batzill.Server.Core.Settings.Custom.Operations;
using Batzill.Server.Core.Authentication;
using Batzill.Server.Core.Exceptions;

namespace Batzill.Server.Core.Operations
{
    public class StreamLogsOperation : Operation
    {
        private const string InputParameterClient = "client";
        private const string InputParameterPort = "port";
        private const string InputParameterOperation = "operation";
        private const string InputParameterUrl = "url";
        private const string InputParameterType = "type";
        private const string InputParameterMessage = "message";

        private DateTime lastLogTime;
        private bool failed = true;

        public override string Name => "StreamLogs";

        public StreamLogsOperation(Logger logger = null) : base(logger)
        {
        }

        protected override void ExecuteInternal(HttpContext context, IAuthenticationManager authManager)
        {
            if (!(this.logger?.LogWriter is MultiLogWriter))
            {
                this.logger?.Log(EventType.OperationError, "Unable to attach EventLogWriter (Passed logwriter is no MultiLogWriter).");

                throw new InternalServerErrorException();
            }

            this.logger?.Log(EventType.OperationInformation, "Parse filter (if exist).");

            List<Func<Log, bool>> filters = new List<Func<Log, bool>>()
            {
                (log) => (log is OperationLog) == false || !string.Equals((log as OperationLog).OperationName, this.Name)
            };

            var parameters = System.Web.HttpUtility.ParseQueryString(System.Web.HttpUtility.UrlDecode(context.Request.Url.Query));
            foreach(string parameter in parameters)
            {
                this.logger?.Log(EventType.OperationInformation, "Found filter for '{0}': '{1}'", parameter, parameters[parameter]);

                // skip empty filters
                if(string.IsNullOrEmpty(parameters[parameter]))
                {
                    continue;
                }

                switch(parameter)
                {
                    case StreamLogsOperation.InputParameterClient:
                        filters.Add((log) =>
                        {
                            return log is OperationLog && Regex.IsMatch((log as OperationLog).ClientIp, parameters[parameter], RegexOptions.IgnoreCase);
                        });
                        break;
                    case StreamLogsOperation.InputParameterPort:
                        filters.Add((log) =>
                        {
                            return log is OperationLog && Regex.IsMatch((log as OperationLog).LocalPort, parameters[parameter], RegexOptions.IgnoreCase);
                        });
                        break;
                    case StreamLogsOperation.InputParameterOperation:
                        filters.Add((log) =>
                        {
                            return log is OperationLog && Regex.IsMatch((log as OperationLog).OperationName, parameters[parameter], RegexOptions.IgnoreCase);
                        });
                        break;
                    case StreamLogsOperation.InputParameterUrl:
                        filters.Add((log) =>
                        {
                            return log is OperationLog && Regex.IsMatch((log as OperationLog).Url, parameters[parameter], RegexOptions.IgnoreCase);
                        });
                        break;
                    case StreamLogsOperation.InputParameterType:
                        filters.Add((log) =>
                        {
                            return log != null && Regex.IsMatch(log.EventType.ToString(), parameters[parameter], RegexOptions.IgnoreCase);
                        });
                        break;
                    case StreamLogsOperation.InputParameterMessage:
                        filters.Add((log) =>
                        {
                            return log != null && log.ExtendedData.Any(x => x != null && Regex.IsMatch(x, parameters[parameter], RegexOptions.IgnoreCase));
                        });
                        break;
                    default:
                        this.logger?.Log(EventType.OperationError, "Unknown filter '{0}'.", parameter);

                        throw new BadRequestException("Unknown filter '{0}'.", parameter);
                }
            }

            this.logger?.Log(EventType.OperationInformation, "Set response headers.");

            context.Response.SetDefaultValues();
            context.Response.SendChuncked = true;
            context.Response.SetHeaderValue("Content-Type", "text/event-stream");
            context.SyncResponse();

            this.logger?.Log(EventType.OperationInformation, "Setup event writer.");

            this.failed = false;

            MultiLogWriter logWriter = this.logger?.LogWriter as MultiLogWriter;
            EventLogWriter eventWriter = new EventLogWriter();
            eventWriter.LogWritten += (sender, log) =>
            {
                lock (context)
                {
                    try
                    {
                        if (filters.Any(filter => !filter(log)))
                        {
                            return;
                        }

                        this.lastLogTime = DateTime.Now;
                        context.Response.WriteContent(string.Format(
                            "[{0} | {1}] {2} {3}{4}",
                            log.Timestamp,
                            log.EventType,
                            log.ExtendedData.Length > 1 ? $"('{string.Join("', '", log.ExtendedData, 0, log.ExtendedData.Length - 1)}')" : "",
                            log.ExtendedData[log.ExtendedData.Length - 1],
                            Environment.NewLine));
                        
                        context.FlushResponse();
                    }
                    catch(Exception ex)
                    {
                        this.logger?.Log(EventType.OperationError, "Failed to write log to stream, stop operation: {0}", ex.Message);
                        this.failed = true;
                    }
                }
            };


            this.logger?.Log(EventType.OperationInformation, "Add event writer to global log writer.");

            try
            {
                this.lastLogTime = DateTime.Now;
                logWriter.Add(eventWriter);
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.OperationError, "Unable to attach EventLogWriter (Adding failed): {0}", ex.Message);
                return;
            }

            int idleTimeoutinMs = 300000;
            int idleTimeinMs = 0;
            int sleepTimeInMs = 1000;

            do
            {
                Thread.Sleep(sleepTimeInMs);

                idleTimeinMs = (int)(DateTime.Now - this.lastLogTime).TotalMilliseconds;

            } while (!failed && idleTimeinMs <= idleTimeoutinMs);

            // only output this information when stream didn't stop because flushing log data to stream failed.
            if(idleTimeinMs > idleTimeoutinMs)
            {
                this.logger?.Log(EventType.OperationInformation, "No log event for {0}s, stopping the log stream.", idleTimeoutinMs);
            }

            this.logger?.Log(EventType.OperationInformation, "Remove event writer to global log writer.");

            try
            {
                logWriter.Remove(eventWriter);
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "Unable to remove EventLogWriter: {0}", ex.Message);
            }

            return;
        }

        public override bool Match(HttpContext context)
        {
            return Regex.IsMatch(context.Request.RawUrl, "^/stream/logs/?$", RegexOptions.IgnoreCase);
        }
    }
}
