using System;
using System.Collections.Generic;
using LogLine.Core;
using UnityEngine;

namespace LogLine.Config
{
    /// <summary>
    /// Serializable Inspector configuration for building a <see cref="CategoryFilter"/>.
    /// </summary>
    [Serializable]
    public struct CategoryFilterConfig
    {
        #region Fields

        [Tooltip("None disables filtering; AllowList accepts only matching prefixes; DenyList blocks matching prefixes.")]
        public CategoryFilterMode Mode;

        [Tooltip("Category prefixes matched case-insensitively (e.g. 'Combat', 'Combat.AI', 'Network.').")]
        public List<string> Prefixes;

        #endregion

        #region Factory

        /// <summary>
        /// Builds an immutable <see cref="CategoryFilter"/> from this configuration,
        /// or returns <see langword="null"/> when filtering is disabled.
        /// </summary>
        /// <returns>A configured <see cref="CategoryFilter"/> or <see langword="null"/>.</returns>
        public CategoryFilter Build()
        {
            if (Mode == CategoryFilterMode.None)
            {
                return null;
            }

            return new CategoryFilter(Mode, Prefixes);
        }

        #endregion
    }
}
