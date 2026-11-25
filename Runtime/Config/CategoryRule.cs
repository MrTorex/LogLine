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
        public string categoryPrefix;
        public LogLevel minimumLevel;

        public CategoryRule(string categoryPrefix, LogLevel minimumLevel)
        {
            this.categoryPrefix = categoryPrefix;
            this.minimumLevel = minimumLevel;
        }
    }
}
