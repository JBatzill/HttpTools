using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Batzill.Server.Core.Logging
{
    /// <summary>
    /// Contains the list of different EventTypes for logging
    /// 
    ///                 bit 0-20: index within group
    ///     Class        bit 21-24: 1 = Information, 2 = Warning, 3 = Error
    ///     Source      bit 25-28: 1 = System, 2 = Operation
    /// </summary>
    public enum EventType
    {
        #region Source
        
        System = 0x1000000,
        Operation = 0x2000000,

        #endregion

        #region Class

        Information = 0x0100000,
        Warning = 0x0200000,
        Error = 0x0300000,

        #endregion


        #region System


        // Class for up to 0x11fffff
        SystemInformation = 0x1100000,
        SystemSettings = 0x1100001,
        ServerSetup = 0x1100002,
        OperationLoading = 0x1100003,
        OperationClassInitalization = 0x1100004,
        ConnectionInformation = 0x1100005,

        // Class for up to 0x12fffff
        SytemWarning = 0x1200000,

        // Class for up to 0x13fffff
        SystemError = 0x1300000,
        SettingsParsingError = 0x1300001,
        OperationLoadingError = 0x1300002,
        SettingInvalid = 0x1300003,

        #endregion



        #region Operation

        // Class for up to 0x21fffff
        OperationInformation = 0x2100000,
        OperationAuthentication = 0x2100001,

        // Class for up to 0x22fffff
        OperationWarning = 0x2200000,

        // Class for up to 0x23fffff
        OperationError = 0x2300000,
        OperationAuthenticationError = 0x2300001,
        OperationClassInitializationError = 0x2300001

        #endregion
    }

    public static class EventTypeExtension
    {
        private static int SourceMask = 0xf000000;
        private static int TypeMask = 0x0f00000;
        
        public static EventType Class(this EventType eventType)
        {
            return (EventType)((int)eventType & (int)EventTypeExtension.TypeMask);
        }

        public static EventType Source(this EventType eventType)
        {
            return (EventType)((int)eventType & (int)EventTypeExtension.SourceMask);
        }
    }
}
