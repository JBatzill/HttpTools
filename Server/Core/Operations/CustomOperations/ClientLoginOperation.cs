using System;
using Batzill.Server.Core.Logging;
using Batzill.Server.Core.ObjectModel;
using Batzill.Server.Core.Settings;
using Batzill.Server.Core.Exceptions;
using System.Text.RegularExpressions;
using Batzill.Server.Core.Settings.Custom.Operations;
using Batzill.Server.Core.Authentication;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;

namespace Batzill.Server.Core.Operations
{
    public class ClientLoginOperation : Operation
    {
        private static ConcurrentDictionary<string, ClientLoginOperation.Client> ClientMappings;
        private static bool HttpsOnly;

        public override string Name => "ClientLogin";

        public ClientLoginOperation(Logger logger = null) : base(logger)
        {
        }

        protected override void InitializeClassInternal(OperationSettings settings, IAuthenticationManager authManager)
        {
            if (!(settings is ClientLoginOperationSettings))
            {
                throw new ArgumentException($"Type '{settings.GetType()}' is invalid for this operation.");
            }

            ClientLoginOperationSettings internalSettings = (settings as ClientLoginOperationSettings);

            ClientLoginOperation.ClientMappings = new ConcurrentDictionary<string, ClientLoginOperation.Client>();
            foreach(ClientLoginOperation.Client client in internalSettings.Clients)
            {
                this.logger?.Log(EventType.OperationClassInitalization, "Adding user '{0}' to white list.", client.UserId);

                if (!authManager.UserExists(client.UserId))
                {
                    this.logger?.Log(EventType.OperationClassInitalization, "User '{0}' isn't known.", client.UserId);
                    continue;
                }

                foreach(string ip in client.Addresses)
                {
                    if (!ClientLoginOperation.ClientMappings.TryAdd(ip, client))
                    {
                        this.logger?.Log(EventType.OperationClassInitalization, "Failed to whitelist '{0}' for user '{1}'.", ip, client.UserId);
                        continue;
                    }
                }
            }

            ClientLoginOperation.HttpsOnly = internalSettings.HttpsOnly;
            this.logger?.Log(EventType.OperationClassInitalization, "Use HttpsOnly for ClientLogin: '{0}'.", ClientLoginOperation.HttpsOnly);
        }

        protected override void ExecuteInternal(HttpContext context, IAuthenticationManager authManager)
        {
            context.Response.SetDefaultValues();

            // Create response content

            string clientIp = context.Request.RemoteEndpoint.Address.ToString();

            this.logger?.Log(EventType.OperationAuthentication, "Check whitelist for clientIp '{0}'.", clientIp);

            if (string.IsNullOrEmpty(clientIp))
            {
                this.logger?.Log(EventType.OperationAuthenticationError, "ClientIp is null or empty.", clientIp);

                throw new InternalServerErrorException();
            }

            if(!ClientLoginOperation.ClientMappings.TryGetValue(clientIp, out Client client))
            {
                this.logger?.Log(EventType.OperationAuthenticationError, "Unknown client: '{0}'.", clientIp);

                throw new UnauthorizedException("Unknown client.");
            }

            this.logger?.Log(EventType.OperationAuthentication, "Found client '{0}' as user '{1}'.", clientIp, client.UserId);

            string accessToken = null;
            DateTime expirationDate = new DateTime();
            try
            {
                (accessToken, expirationDate) = authManager.GetAccessToken(client.UserId, true);
            }
            catch(Exception ex)
            {
                this.logger?.Log(EventType.OperationAuthenticationError, "Exception occured while trying to get access token for client '{0}': '{1}'.", clientIp, ex);

                throw new UnauthorizedException("Unknown client.");
            }

            this.logger?.Log(EventType.OperationAuthentication, "Authentication was successful, returning access token.");

            context.Response.Cookies.Add(new System.Net.Cookie(Operation.AccessTokenName, accessToken)
            {
                Expires = expirationDate,
                Path = "/"
            });

            context.Response.WriteContent($"Authentication was successful. AccessToken: '{accessToken}', ValidUntil: '{expirationDate}'.");

            return;
        }

        public override bool Match(HttpContext context)
        {
            return Regex.IsMatch(context.Request.Url.AbsolutePath, @"^/auth/client$", RegexOptions.IgnoreCase);
        }

        public class Client
        {
            public string UserId
            {
                get; set;
            }

            public List<string> Addresses
            {
                get; set;
            }

            public void Validate()
            {
                if (string.IsNullOrEmpty(this.UserId))
                {
                    throw new NullReferenceException($"'{nameof(this.UserId)}' can't be null or empty!");
                }

                if (this.Addresses == null)
                {
                    throw new NullReferenceException($"'{nameof(this.Addresses)}' can't be null!");
                }

                this.Addresses.ForEach((c) =>
                {
                    if (string.IsNullOrEmpty(c))
                    {
                        throw new NullReferenceException($"'{nameof(this.Addresses)}' can't contain null or empty entries!");
                    }
                });
            }
        }
    }
}
