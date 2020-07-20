using System;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Batzill.Server.Core.Authentication;
using Batzill.Server.Core.Exceptions;
using Batzill.Server.Core.Logging;
using Batzill.Server.Core.ObjectModel;

namespace Batzill.Server.Core.Operations
{
    public class DownloadOperation : Operation
    {
        /// <summary>
        /// Group[2]: size
        /// Group[3]: unit
        /// Group[6]: chunksize
        /// </summary>
        private const string InputRegex = @"^\/download\/?(([0-9]+)(b|kb|mb|gb|)?)$";
        private static Random Random = new Random((int)(DateTime.Now.Ticks % int.MaxValue));

        private const int DefaultBufferSize = 8192;
        private const int DefaultWaitTimeInMs = -1;

        public override string Name => "Download";

        public DownloadOperation(Logger logger = null) : base(logger)
        {
        }

        protected override void ExecuteInternal(HttpContext context, IAuthenticationManager authManager)
        {
            context.Response.SetDefaultValues();
            context.Response.SetHeaderValue("RemoteEndpointIp", context.Request.RemoteEndpoint.Address.ToString());

            this.logger?.Log(EventType.OperationInformation, "Got request to download data, try parsing size passed by client.");

            Match result = Regex.Match(context.Request.RawUrl, DownloadOperation.InputRegex, RegexOptions.IgnoreCase);
            if (result.Success && !string.IsNullOrEmpty(result.Groups[1].Value))
            {
                if (Int64.TryParse(result.Groups[2].Value, out long inputNumber))
                {
                    /* Parse download size in path */
                    string requestedUnit = string.IsNullOrEmpty(result.Groups[3].Value) ? "b" : result.Groups[3].Value;
                    long requestedDownloadSize = inputNumber;
                    switch(requestedUnit.ToLowerInvariant())
                    {
                        case "b":
                            break;
                        case "kb":
                            requestedDownloadSize *= 1 << 10;
                            break;
                        case "mb":
                            requestedDownloadSize *= 1 << 20;
                            break;
                        case "gb":
                            requestedDownloadSize *= 1 << 30;
                            break;
                        default:
                            this.logger?.Log(EventType.OperationError, "Unknown size unit '{0}'", requestedUnit);

                            throw new BadRequestException("Unknown size unit '{0}'", requestedUnit);
                    }

                    if (requestedDownloadSize > 34359738368) // = 32g
                    {
                        this.logger?.Log(EventType.OperationError, "File is to big '{0}'{1}, only supported up to 32GB.", inputNumber, requestedUnit);

                        throw new BadRequestException("File is to big '{0}'{1}, only supported up to 32GB.", inputNumber, requestedUnit);
                    }

                    /* Parse input parameters in query */
                    NameValueCollection parameters = System.Web.HttpUtility.ParseQueryString(context.Request.Url.Query);

                    int bufferSize = DownloadOperation.DefaultBufferSize;
                    if (!string.IsNullOrEmpty(parameters["bufferSize"]))
                    {
                        if(!Int32.TryParse(parameters["bufferSize"], out bufferSize) || bufferSize < 1 || bufferSize > 33554432)
                        {
                            this.logger?.Log(EventType.OperationError, "Unable to parse chunk size '{0}' to long.", parameters["bufferSize"]);

                            throw new BadRequestException("Unable to parse chunk size '{0}' to long.  Please provide the chunk size in number of bytes within [1, 32MB]", parameters["bufferSize"]);
                        }
                    }

                    int waitTime = DownloadOperation.DefaultWaitTimeInMs;
                    if (!string.IsNullOrEmpty(parameters["wait"]))
                    {
                        if (!Int32.TryParse(parameters["wait"], out waitTime) || waitTime < 1 || waitTime > 30000)
                        {
                            this.logger?.Log(EventType.OperationError, "Invalid value for wait time: '{0}'.", parameters["wait"]);

                            throw new BadRequestException("Invalid value for wait time: '{0}'. Please provide a value in ms within [1, 30000].", parameters["wait"]);
                        }
                    }

                    bool chunked = string.Equals("true", parameters["chunked"], StringComparison.InvariantCultureIgnoreCase);

                    this.logger?.Log(EventType.OperationInformation, "Will return file of size: {0}{1}, bufferSize: '{2}', chunked: '{3}', wait: '{4}'.", inputNumber, requestedUnit, bufferSize , chunked, waitTime);

                    context.Response.SetHeaderValue("Content-Type", "application/octet-stream");
                    context.Response.SetHeaderValue("Content-Disposition", String.Format("attachment;filename=\"TestFile_{0}{1}.file\"", inputNumber, requestedUnit));

                    if (chunked)
                    {
                        context.Response.SendChuncked = true;
                    }
                    else
                    {
                        context.Response.ContentLength = requestedDownloadSize;
                    }

                    // sync response before sending
                    context.SyncResponse();
                    
                    long bytesSend = 0;
                    byte[] data = Encoding.ASCII.GetBytes(new string('a', (int)bufferSize));
                    while (bytesSend < requestedDownloadSize)
                    {
                        context.Response.Stream.Write(data, 0, (int)Math.Min(bufferSize, requestedDownloadSize - bytesSend));
                        context.FlushResponse();
                        bytesSend += bufferSize;

                        if(waitTime > 0)
                        {
                            Thread.Sleep(waitTime);
                        }
                    }
                }
                else
                {
                    this.logger?.Log(EventType.OperationError, "Unable to parse '{0}' to long.", result.Groups[1].Value);

                    throw new BadRequestException("Unable to parse '{0}' to long.", result.Groups[1].Value);
                }
            }
            else
            {
                this.logger?.Log(EventType.OperationInformation, "No number passed, return info page.");

                context.Response.WriteContent("Call '/download/[number](unit)' to have the server return [number] (unit=b/kb/mb/gb, default b) of data. (Max 32GB)");
            }
        }

        public override bool Match(HttpContext context)
        {
            return Regex.IsMatch(context.Request.RawUrl, DownloadOperation.InputRegex, RegexOptions.IgnoreCase);
        }
    }
}
