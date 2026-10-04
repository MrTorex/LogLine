using System;
using LogLine.Core;

namespace LogLine.Config
{
    /// <summary>
    /// Represents an override rule that sets a specific minimum log level for a category prefix.
    /// </summary>
    [Serializable]
    public struct CategoryRule
    {
        public string CategoryPrefix;
        public LogLevel MinimumLevel;

        public CategoryRule(string categoryPrefix, LogLevel minimumLevel)
        {
            CategoryPrefix = categoryPrefix;
            MinimumLevel = minimumLevel;
        }
    }
}
