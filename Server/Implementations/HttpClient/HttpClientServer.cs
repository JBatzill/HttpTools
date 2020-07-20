using System;
using Batzill.Server.Core.Settings;
using System.Net;
using Batzill.Server.Core.Logging;
using System.Threading.Tasks;
using Batzill.Server.Core.ObjectModel;
using Batzill.Server.Core;
using Batzill.Server.Implementations.HttpClient;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Batzill.Server.Core.SSLBindingHelper;
using System.Threading;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Reflection;
using Batzill.Server.Core.Authentication;

namespace Batzill.Server
{
    public class HttpClientServer : HttpServer
    {
        private static int InitialConcurrentConnections = 1;

        private static List<string> AlternateHostNames = new List<string>()
        {
            "127.0.0.1",
            "+",
            "*"
        };

        private HttpListener listener;
        private ISSLBindingHelper sslBindingHelper;

        private Semaphore semaphore;

        public HttpClientServer(Logger logger, IOperationFactory operationFactory, IAuthenticationManager authManager, HttpServerSettings settings, ISSLBindingHelper sslBindingHelper)
            : base(logger, operationFactory, authManager)
        {
            this.listener = new HttpListener();
            this.sslBindingHelper = sslBindingHelper;
            this.semaphore = new Semaphore(HttpClientServer.InitialConcurrentConnections, HttpClientServer.InitialConcurrentConnections);

            this.ApplySettings(settings);
        }

        public override bool IsRunning
        {
            get
            {
                return this.listener.IsListening;
            }
        }

        protected override void StartInternal()
        {
            this.listener.Start();

            Task.Run(() =>
            {
                while(this.IsRunning)
                {
                    this.semaphore.WaitOne();
                    this.listener.BeginGetContext(new AsyncCallback(ListenerCallback), this.listener);
                }
            });
        }

        private void ListenerCallback(IAsyncResult result)
        {
            HttpListenerContext httpListenerContext = (result.AsyncState as HttpListener).EndGetContext(result);

            // realease semaphore after ending getcontext operation and before proceeding with request
            this.semaphore.Release();

            // Prepare Context
            HttpClientContext context = new HttpClientContext(httpListenerContext.Request, httpListenerContext.Response);

            this.HandleRequest(context);
        }

        protected override void StopInternal()
        {
            this.listener.Stop();
            this.semaphore.Close();
        }

        protected override void ApplySettingsInternal(HttpServerSettings settings)
        {
            this.ApplyEndpoints(settings);
            this.ApplyLimits(settings);
        }

        private void ApplyEndpoints(HttpServerSettings settings)
        {
            this.logger?.Log(EventType.ServerSetup, "Set endpoints passed in settings.");

            this.listener.Prefixes.Clear();
            foreach (Core.Settings.EndPoint ep in settings.Core.EndPoints)
            {
                this.logger?.Log(EventType.ServerSetup, "Try to add endpoint '{0}'.", JsonConvert.SerializeObject(ep, Formatting.Indented));

                if (Uri.CheckHostName(ep.HostName) == UriHostNameType.Dns || HttpClientServer.AlternateHostNames.Contains(ep.HostName))
                {
                    if (ep.Protocol == Protocol.HTTPS)
                    {
                        string bindingHost = HttpClientServer.AlternateHostNames.Contains(ep.HostName) ? this.sslBindingHelper.DefaultEndpointHost : ep.HostName;
                        if (!this.sslBindingHelper.TryAddOrUpdateCertBinding(ep.CertificateThumbPrint, HttpClientServer.GetApplicationId(), ep.Port.ToString(), bindingHost))
                        {
                            this.logger?.Log(EventType.SettingInvalid, "Skipping endpoint, unable to bind certificate '{0}'.", ep.CertificateThumbPrint);
                            continue;
                        }
                    }

                    string prefix = string.Format(
                        "{0}://{1}:{2}/",
                        ep.Protocol.ToString().ToLower(),
                        ep.HostName,
                        ep.Port);

                    this.listener.Prefixes.Add(prefix);

                    this.logger?.Log(EventType.ServerSetup, "Added prefix '{0}'.", prefix);
                }
                else
                {
                    this.logger?.Log(EventType.SettingInvalid, "Skipping endpoint, invalid host '{0}'.", ep.HostName);
                }
            }

            if (this.listener.Prefixes.Count == 0)
            {
                this.logger?.Log(EventType.SettingInvalid, "Settings file did not contain any valid endpoints!");
                throw new ArgumentException("Settings file did not contain any valid endpoints!");
            }
        }

        private void ApplyLimits(HttpServerSettings settings)
        {
            /* IdleTimeout */
            this.listener.TimeoutManager.IdleConnection = new TimeSpan(0, 0, 0, 0, settings.Core.IdleTimeout);

            this.logger?.Log(EventType.ServerSetup, "Set idle timeout to {0}s.", settings.Core.IdleTimeout);

            /* ConnectionLimit */
            /* (server got stopped before settings update, save to reinitialize the semaphore.) */
            this.semaphore = new Semaphore(settings.Core.ConnectionLimit, settings.Core.ConnectionLimit);
            ServicePointManager.DefaultConnectionLimit = settings.Core.ConnectionLimit;

            this.logger?.Log(EventType.ServerSetup, "Set ConnectionLimit to '{0}'.", settings.Core.ConnectionLimit);
        }

        private static string GetApplicationId()
        {
            return ((GuidAttribute)Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(GuidAttribute), true)[0]).Value;
        }
    }
}
