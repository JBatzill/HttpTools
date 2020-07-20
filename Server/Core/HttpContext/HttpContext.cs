using System;

namespace Batzill.Server.Core.ObjectModel
{
    public abstract class HttpContext
    {
        public HttpRequest Request
        {
            get;
            private set;
        }

        public HttpResponse Response
        {
            get;
            private set;
        }

        public bool SyncAllowed
        {
            get;
            private set;
        }

        public bool FlushAllowed
        {
            get;
            private set;
        }

        public HttpContext(Version protocolVersion) : this(new HttpRequest(protocolVersion), new HttpResponse(protocolVersion)) { }

        public HttpContext(HttpRequest request) : this(request, new HttpResponse(request.ProtocolVersion)) {
        }
        public HttpContext(HttpResponse response) : this(new HttpRequest(response.ProtocolVersion), response) { }

        public HttpContext(HttpRequest request, HttpResponse response)
        {
            this.Request = request;
            this.Response = response;

            this.SyncAllowed = true;
            this.FlushAllowed = true;
        }

        public void SyncResponse()
        {
            if (!this.SyncAllowed)
            {
                throw new InvalidOperationException("Not allowed to sync response, either Flush or Close was already invoked.");
            }

            this.SyncResponseInternal();
        }

        public void FlushResponse()
        {
            if (!this.FlushAllowed)
            {
                throw new InvalidOperationException("Not allowed to flush response, Close was already invoked.");
            }

            this.SyncAllowed = false;
            this.FlushResponseInternal();
        }

        public void CloseResponse()
        {
            this.SyncAllowed = false;
            this.FlushAllowed = false;
            this.CloseResponseInternal();
        }

        public void ResetResponse()
        {
            if (!this.SyncAllowed)
            {
                throw new InvalidOperationException("Not allowed to reset response, either Flush or Close was already invoked.");
            }

            this.Response.Reset();
        }

        public void SyncRequest()
        {
            if (!this.SyncAllowed)
            {
                throw new InvalidOperationException("Not allowed to sync request, either Flush or Close was already invoked.");
            }

            this.SyncRequestInternal();
        }

        public void FlushRequest()
        {
            if (!this.FlushAllowed)
            {
                throw new InvalidOperationException("Not allowed to flush request, Close was already invoked.");
            }

            this.SyncAllowed = false;
            this.FlushRequestInternal();
        }

        public void CloseRequest()
        {
            this.SyncAllowed = false;
            this.FlushAllowed = false;
            this.CloseRequestInternal();
        }

        public void ResetRequest()
        {
            if(!this.SyncAllowed)
            {
                throw new InvalidOperationException("Not allowed to reset request, either Flush or Close was already invoked.");
            }

            this.Request.Reset();
        }

        protected abstract void SyncResponseInternal();
        protected abstract void FlushResponseInternal();
        protected abstract void CloseResponseInternal();

        protected abstract void SyncRequestInternal();
        protected abstract void FlushRequestInternal();
        protected abstract void CloseRequestInternal();
    }
}
