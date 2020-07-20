using System;
using System.Collections.Concurrent;
using Batzill.Server.Core.Authentication;
using Batzill.Server.Core.Exceptions;
using Batzill.Server.Core.Logging;
using Batzill.Server.Core.ObjectModel;
using Batzill.Server.Core.Settings;
using Newtonsoft.Json;

namespace Batzill.Server.Core
{
    public abstract class Operation
    {
        public const string AccessTokenName = "AccessToken";

        private static ConcurrentDictionary<Type, AuthenticationConfiguration> AuthConfigs = new ConcurrentDictionary<Type, AuthenticationConfiguration>();

        public abstract string Name
        {
            get;
        }

        public string ID
        {
            get; private set;
        }

        protected Logger logger
        {
            get; private set;
        }

        protected Operation(Logger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Used to initialize the operation before Executing.
        /// </summary>
        /// <param name="logger">The operation logger</param>
        /// <param name="operationId">The Id of the operation</param>
        public void Initialize(Logger logger, string operationId)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.ID = operationId;
        }

        public void InitializeClass(OperationSettings settings, IAuthenticationManager authManager)
        {
            Operation.AuthConfigs[this.GetType()] = new AuthenticationConfiguration()
            {
                AuthenticationRequired = settings.AuthenticationRequired,
                HttpsOnly = settings.HttpsOnly
            };
            
            try
            {
                this.InitializeClassInternal(settings, authManager);
            }
            catch (Exception ex)
            {
                /* Log exception in operation logs */
                this.logger?.Log(EventType.OperationClassInitializationError, $"Operation initialization failed with exception '{ex}'.");
                throw ex;
            }
        }

        public void Execute(HttpContext context, IAuthenticationManager authManager)
        {
            AuthenticationConfiguration authConfig = Operation.AuthConfigs[this.GetType()];

            if(authConfig == null)
            {
                this.logger?.Log(EventType.OperationAuthenticationError, "Unable to find authentication configuration for '{0}'.", this.Name);

                throw new InternalServerErrorException("Unlucky.");
            }

            this.logger?.Log(EventType.OperationAuthentication, "Authentication configuration: '{0}'.", JsonConvert.SerializeObject(authConfig));


            // Check for https, if configured

            if (authConfig.HttpsOnly)
            {
                if (!context.Request.IsSecureConnection)
                {
                    this.logger?.Log(EventType.OperationAuthenticationError, "'HttpsOnly' is set for operation '{0}' but connection to client is 'http'.", this.Name);

                    throw new UnauthorizedException("Operation is available via https only.");
                }
            }

            // check authentication, if configured

            if (authConfig.AuthenticationRequired)
            {

                // First check in cookies
                string accessToken = context.Request.Cookies[Operation.AccessTokenName]?.Value;

                // Check in headers
                if (string.IsNullOrEmpty(accessToken))
                {
                    accessToken = context.Request.Headers[Operation.AccessTokenName];
                }

                // check in query
                if (string.IsNullOrEmpty(accessToken))
                {
                    var parameters = System.Web.HttpUtility.ParseQueryString(System.Web.HttpUtility.UrlDecode(context.Request.Url.Query));
                    accessToken = parameters[Operation.AccessTokenName];
                }

                this.logger?.Log(EventType.OperationAuthentication, "Found accesstoken: {0}.", string.IsNullOrEmpty(accessToken) ? "false" : "true");

                if (!authManager.IsValidAccessToken(accessToken))
                {
                    this.logger?.Log(EventType.OperationAuthenticationError, "No valid access token passed.");

                    throw new UnauthorizedException("Authentication required! Please authenticate at '/auth/user?username={USERNAME}&key={KEY}'.");
                }

                this.logger?.Log(EventType.OperationAuthentication, "Authentication was successful.");
            }

            try
            {
                this.ExecuteInternal(context, authManager);
            }
            catch(Exception ex)
            {
                /* Log exception in operation logs */
                this.logger?.Log(EventType.OperationError, $"Operation failed with exception '{ex}'.");
                throw ex;
            }
        }

        /// <summary>
        /// Used to initialize static properties at the beginning of the operation.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="settings">The settings.</param>
        protected virtual void InitializeClassInternal(OperationSettings settings, IAuthenticationManager authManager) { }

        protected abstract void ExecuteInternal(HttpContext context, IAuthenticationManager authManager);

        public abstract bool Match(HttpContext context);

        private class AuthenticationConfiguration
        {
            public bool AuthenticationRequired
            {
                get; set;
            }

            public bool HttpsOnly
            {
                get; set;
            }
        }
    }
}
