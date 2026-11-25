using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace LogLine.Core
{
    /// <summary>
    /// Immutable, thread-safe category prefix filter for sink-level log routing.
    /// Assigned atomically by reference swap to avoid torn reads and heap allocations on the hot path.
    /// </summary>
    public sealed class CategoryFilter
    {
        #region Private Fields

        private readonly string[] _prefixes;
        private readonly ReadOnlyCollection<string> _readOnlyPrefixes;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the evaluation mode for this filter.
        /// </summary>
        public CategoryFilterMode Mode { get; }

        /// <summary>
        /// Gets the read-only list of active category prefixes.
        /// </summary>
        public IReadOnlyList<string> Prefixes => _readOnlyPrefixes;

        /// <summary>
        /// Gets a value indicating whether no valid prefixes are configured.
        /// </summary>
        public bool IsEmpty => _prefixes.Length == 0;

        #endregion

        #region Construction & Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CategoryFilter"/> class.
        /// </summary>
        /// <param name="mode">The filter evaluation mode.</param>
        /// <param name="prefixes">The collection of category prefixes to match against.</param>
        public CategoryFilter(CategoryFilterMode mode, IReadOnlyList<string> prefixes)
        {
            Mode = mode;
            _prefixes = SanitizePrefixes(prefixes);
            _readOnlyPrefixes = Array.AsReadOnly(_prefixes);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CategoryFilter"/> class.
        /// </summary>
        /// <param name="mode">The filter evaluation mode.</param>
        /// <param name="prefixes">The category prefixes to match against.</param>
        public CategoryFilter(CategoryFilterMode mode, params string[] prefixes)
            : this(mode, (IReadOnlyList<string>)prefixes)
        {
        }

        /// <summary>
        /// Creates an allowlist filter that accepts only categories starting with any of the specified prefixes.
        /// </summary>
        /// <param name="prefixes">The permitted category prefixes.</param>
        /// <returns>An immutable <see cref="CategoryFilter"/> instance.</returns>
        public static CategoryFilter Allow(params string[] prefixes) =>
            new(CategoryFilterMode.AllowList, prefixes);

        /// <summary>
        /// Creates an allowlist filter that accepts only categories starting with any of the specified prefixes.
        /// </summary>
        /// <param name="prefixes">The permitted category prefixes.</param>
        /// <returns>An immutable <see cref="CategoryFilter"/> instance.</returns>
        public static CategoryFilter Allow(IReadOnlyList<string> prefixes) =>
            new(CategoryFilterMode.AllowList, prefixes);

        /// <summary>
        /// Creates a denylist filter that rejects categories starting with any of the specified prefixes.
        /// </summary>
        /// <param name="prefixes">The blocked category prefixes.</param>
        /// <returns>An immutable <see cref="CategoryFilter"/> instance.</returns>
        public static CategoryFilter Deny(params string[] prefixes) =>
            new(CategoryFilterMode.DenyList, prefixes);

        /// <summary>
        /// Creates a denylist filter that rejects categories starting with any of the specified prefixes.
        /// </summary>
        /// <param name="prefixes">The blocked category prefixes.</param>
        /// <returns>An immutable <see cref="CategoryFilter"/> instance.</returns>
        public static CategoryFilter Deny(IReadOnlyList<string> prefixes) =>
            new(CategoryFilterMode.DenyList, prefixes);

        #endregion

        #region Matching API

        /// <summary>
        /// Evaluates whether the specified category name passes this filter without heap allocations.
        /// </summary>
        /// <param name="category">The logger category name.</param>
        /// <returns><see langword="true"/> if the event should be emitted; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMatch(string category)
        {
            if (Mode == CategoryFilterMode.None)
            {
                return true;
            }

            if (_prefixes.Length == 0)
            {
                return Mode == CategoryFilterMode.DenyList;
            }

            ReadOnlySpan<char> categorySpan = category.AsSpan();
            bool matched = false;

            for (int i = 0; i < _prefixes.Length; i++)
            {
                if (categorySpan.StartsWith(_prefixes[i].AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            return Mode == CategoryFilterMode.AllowList ? matched : !matched;
        }

        #endregion

        #region Helpers

        private static string[] SanitizePrefixes(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<string>();
            }

            int validCount = 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrEmpty(source[i]))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return Array.Empty<string>();
            }

            string[] result = new string[validCount];
            int writeIndex = 0;

            for (int i = 0; i < source.Count; i++)
            {
                string entry = source[i];
                if (!string.IsNullOrEmpty(entry))
                {
                    result[writeIndex++] = entry;
                }
            }

            return result;
        }

        #endregion
    }
}
