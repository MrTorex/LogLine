namespace LogLine.Layouts
{
    /// <summary>
    /// Selects the built-in <see cref="ILogLayout"/> implementation used by a configured sink.
    /// </summary>
    public enum LogLayoutType : byte
    {
        /// <summary>
        /// Human-readable pattern formatting via <see cref="PatternLayout"/>.
        /// </summary>
        Pattern = 0,

        /// <summary>
        /// Structured flat JSON formatting via <see cref="JsonLayout"/>.
        /// </summary>
        Json = 1
    }
}
