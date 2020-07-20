using Batzill.Server.Core.Authentication;
using Batzill.Server.Core.Exceptions;
using Batzill.Server.Core.Logging;
using Batzill.Server.Core.ObjectModel;
using Batzill.Server.Core.Settings;
using System;
using System.Threading.Tasks;

namespace Batzill.Server.Core
{
    public abstract class HttpServer
    {
        public abstract bool IsRunning
        {
            get;
        }

        private IOperationFactory operationFactory;
        private IAuthenticationManager authManager;

        private bool correctConfigured = false;

        protected Logger logger;
        protected HttpServerSettings settings;

        protected HttpServer(Logger logger, IOperationFactory operationFactory, IAuthenticationManager authManager)
        {
            this.logger = logger;
            this.operationFactory = operationFactory;
            this.authManager = authManager;
        }

        protected abstract void StartInternal();
        protected abstract void StopInternal();
        protected abstract void ApplySettingsInternal(HttpServerSettings settings);

        public bool Restart()
        {
            this.logger?.Log(EventType.SystemInformation, "Attempting to restart the HttpClientServer.");

            return this.Stop() && this.Start();
        }

        public bool Start()
        {
            if (this.correctConfigured)
            {
                try
                {
                    this.logger?.Log(EventType.SystemInformation, "Attempting to start the HttpServer.");

                    this.logger?.Log(EventType.SystemInformation, "Step 1: Load operations.");
                    this.operationFactory.LoadOperations();

                    this.logger?.Log(EventType.SystemInformation, "Step 2: Start internal server.");
                    this.StartInternal();

                    this.logger?.Log(EventType.SystemInformation, "Successfully started the HttpServer.");

                    return true;
                }
                catch (Exception ex)
                {
                    this.logger?.Log(EventType.SystemError, "Unable to start HttpServer: {0}", ex.ToString());
                    return false;
                }
            }
            else
            {
                this.logger?.Log(EventType.SystemInformation, "Settings are in an invalid state, update settings and try again.");
                return false;
            }
        }

        public bool Stop()
        {
            try
            {
                this.logger?.Log(EventType.SystemInformation, "Attempting to stop the HttpServer.");

                this.StopInternal();
                
                this.logger?.Log(EventType.SystemInformation, "Successfully stopped the HttpServer.");

                return true;
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "Unable to stop HttpServer: {0}", ex.ToString());
                return false;
            }
        }

        public bool ApplySettings(HttpServerSettings settings)
        {
            this.logger?.Log(EventType.ServerSetup, "Attempting to update server settings.");

            if (settings == null)
            {
                this.logger?.Log(EventType.ServerSetup, "Provided settings are null, skip update.");
                return false;
            }

            bool startStopServer = this.IsRunning;
            this.settings = settings;

            if (startStopServer)
            {
                this.logger?.Log(EventType.ServerSetup, "HttpServer is running, attempting to stop the gateway for the settings update.");

                if (!this.Stop())
                {
                    this.logger?.Log(EventType.ServerSetup, "HttpServer is still running, applying settings failed.");
                    return false;
                }
            }

            try
            {
                // call update method of child 
                this.ApplySettingsInternal(settings);

                this.correctConfigured = true;
            }
            catch(Exception ex)
            {
                this.logger?.Log(EventType.SystemError, ex.ToString());
                this.logger?.Log(EventType.SystemError, "Error occured while applying settings, please check logs for more information.");
                this.correctConfigured = false;
            }

            if (this.correctConfigured && startStopServer)
            {
                this.logger?.Log(EventType.ServerSetup, "HttpServer was running before, attempting to start the gateway after the settings update.");
                if (!this.Start())
                {
                    this.logger?.Log(EventType.ServerSetup, "Unable sto start server, applying settings failed.");
                    return false;
                }
            }
            else
            {
                this.logger?.Log(EventType.ServerSetup, "HttpServer is in stopped state.");
            }

            return true;
        }

        protected void HandleRequest(HttpContext context)
        {
            string operationId = Guid.NewGuid().ToString();

            this.logger?.Log(
                EventType.SystemInformation,
                "Recieved new request {0} {1} HTTP{2}/{3} => id: {4}",
                context.Request.HttpMethod,
                context.Request.RawUrl,
                (context.Request.IsSecureConnection ? "s" : ""),
                context.Request.ProtocolVersion,
                operationId);

            this.logger?.Log(
                EventType.ConnectionInformation,
                "(OperationId: {0}) Remote: {1}:{2} Local: {3}:{4}",
                operationId,
                context.Request.RemoteEndpoint.Address,
                context.Request.RemoteEndpoint.Port,
                context.Request.LocalEndpoint.Address,
                context.Request.LocalEndpoint.Port);

            /* 
             * Inner try catches OperationException (might get rethrown)
             * Outer try catches All.
             * To avoid duplication in case it's not possible to set the response to the operationexception.
             */

            try
            {
                this.ApplySettingsToRequest(context);

                try
                {
                    this.ProcessRequest(operationId, context);

                    this.logger?.Log(EventType.SystemInformation, "(OperationId: {0}) Operation finished successfully.", operationId);
                }
                catch (OperationException ex)
                {
                    this.logger?.Log(
                        EventType.SystemError,
                        "(OperationId: {0}) Operation failed with an operation exception. StatusCode: '{1}', StatusDescription: '{2}', Message: '{3}'. Exception: '{4}'.",
                        operationId,
                        ex.StatusCode,
                        ex.StatusDescription,
                        ex.Message,
                        ex);

                    if (context.SyncAllowed)
                    {
                        context.Response.Reset();

                        this.ApplySettingsToRequest(context);

                        context.Response.StatusCode = ex.StatusCode;
                        context.Response.StatusDescription = ex.StatusDescription;
                        context.Response.WriteContent(ex.Message);
                    }
                    else
                    {
                        throw ex;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger?.Log(EventType.SystemError, "(OperationId: {0}) Error executing operation: {1}", operationId, ex);

                context.Response.Reset();

                this.ApplySettingsToRequest(context);

                context.Response.StatusCode = 500;
                context.Response.StatusDescription = "Internal Server Error";
                context.Response.WriteContent("500 - Internal Server Error occured.");


            }
            finally
            {
                try
                {
                    this.logger?.Log(EventType.SystemInformation, "(OperationId: {0}) Do a final header sync, stream flush and close the connection.", operationId);

                    if (context.SyncAllowed)
                    {
                        context.SyncResponse();
                    }

                    if (context.FlushAllowed)
                    {
                        context.FlushResponse();
                    }

                    context.CloseResponse();
                }
                catch (Exception ex)
                {
                    this.logger?.Log(EventType.SystemError, "(OperationId: {0}) Error finishing up operation: {1}", operationId, ex);
                }
            }
        }

        private void ApplySettingsToRequest(HttpContext context)
        {
            context.Response.KeepAlive = this.settings.Core.HttpKeepAlive;
        }

        private void ProcessRequest(string operationId, HttpContext context)
        {
            this.logger?.Log(EventType.OperationLoading, "(OperationId: {0}) Find matching operation for request.", operationId);

            // create matching operation
            Operation operation = this.operationFactory.CreateMatchingOperation(context);

            if(operation == null)
            {
                this.logger?.Log(EventType.OperationLoadingError, "(OperationId: {0}) No matching operation was found for Url '{1}'.", operationId, context.Request.Url);

                context.Response.SetDefaultValues();
                context.Response.StatusCode = 404;
                context.Response.WriteContent("No matching operation was found :(");

                return;
            }

            // initialize logger
            OperationLogger operationLogger = new OperationLogger(
                logWriter: this.logger?.LogWriter,
                clientIp: context.Request.RemoteEndpoint.Address.ToString(),
                localPort: context.Request.LocalEndpoint.Port.ToString(),
                operationId: operationId,
                operationName: operation.Name,
                url: context.Request.Url.PathAndQuery);

            // initialize operation
            operation.Initialize(operationLogger, operationId);

            operationLogger?.Log(
                EventType.OperationLoading, 
                "Start executing operation for client '{0}: name: '{1}', id: '{2}', request: '{3}'", 
                context.Request.RemoteEndpoint.Address, 
                operation.Name, 
                operation.ID, 
                context.Request.RawUrl);

            DateTime startTime = DateTime.Now;

            // execute operation
            operation.Execute(context, this.authManager);

            operationLogger?.Log(EventType.OperationLoading, "Successfully finished operation '{0}' with id '{1}' after {2}s.", operation.Name, operation.ID, (DateTime.Now - startTime).TotalSeconds);
        }
    }
}
