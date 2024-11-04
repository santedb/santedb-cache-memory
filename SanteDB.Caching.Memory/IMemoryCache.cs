using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// Represents a memory cache
    /// </summary>
    internal interface IMemoryCache 
    {

        /// <summary>
        /// Get the name for the memory cache
        /// </summary>
        String CacheName { get; }

        /// <summary>
        /// Trim the memory cache
        /// </summary>
        void Trim();

        /// <summary>
        /// Get the size of the memory cache in entries
        /// </summary>
        long Size();

        /// <summary>
        /// Get the count of items in the cache
        /// </summary>
        long Count();
    }
}
