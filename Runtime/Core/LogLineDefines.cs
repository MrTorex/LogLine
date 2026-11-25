namespace LogLine.Core
{
    /// <summary>
    /// Compile-time scripting define symbol constants and active compilation threshold metadata.
    /// </summary>
    public static class LogLineDefines
    {
        #region Constants

        /// <summary>Compiles Trace and all higher severity levels.</summary>
        public const string MinTrace = "LOGLINE_MIN_TRACE";

        /// <summary>Compiles Debug and all higher severity levels; strips Trace call sites.</summary>
        public const string MinDebug = "LOGLINE_MIN_DEBUG";

        /// <summary>Compiles Info and all higher severity levels; strips Trace and Debug call sites.</summary>
        public const string MinInfo = "LOGLINE_MIN_INFO";

        /// <summary>Compiles Warn and all higher severity levels; strips Trace through Info call sites.</summary>
        public const string MinWarn = "LOGLINE_MIN_WARN";

        /// <summary>Compiles Error and Fatal levels; strips Trace through Warn call sites.</summary>
        public const string MinError = "LOGLINE_MIN_ERROR";

        /// <summary>Compiles Fatal level only; strips Trace through Error call sites.</summary>
        public const string MinFatal = "LOGLINE_MIN_FATAL";

        /// <summary>Strips all LogLine call sites at compile time.</summary>
        public const string MinNone = "LOGLINE_MIN_NONE";

        /// <summary>
        /// Compile-time minimum severity level resolved from active scripting define symbols.
        /// </summary>
#if LOGLINE_MIN_TRACE
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Trace;
#elif LOGLINE_MIN_DEBUG
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Debug;
#elif LOGLINE_MIN_INFO
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Info;
#elif LOGLINE_MIN_WARN
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Warn;
#elif LOGLINE_MIN_ERROR
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Error;
#elif LOGLINE_MIN_FATAL
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.Fatal;
#else
        public static readonly LogLevel CompiledMinimumLevel = LogLevel.None;
#endif

        #endregion
    }
}
