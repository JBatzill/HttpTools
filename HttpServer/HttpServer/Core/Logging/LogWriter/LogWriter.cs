﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HttpServer.Core.Logging
{
    public interface ILogWriter
    {
        void WriteLog(LogEntry log);
    }
}
