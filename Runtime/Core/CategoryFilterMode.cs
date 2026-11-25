namespace LogLine.Core
{
    /// <summary>
    /// Defines how a <see cref="CategoryFilter"/> evaluates matching logger category prefixes.
    /// </summary>
    public enum CategoryFilterMode : byte
    {
        /// <summary>
        /// Filtering is disabled; all categories are accepted.
        /// </summary>
        None = 0,

        /// <summary>
        /// Only categories matching at least one configured prefix are accepted.
        /// </summary>
        AllowList = 1,

        /// <summary>
        /// Categories matching any configured prefix are rejected; all others are accepted.
        /// </summary>
        DenyList = 2
    }
}
