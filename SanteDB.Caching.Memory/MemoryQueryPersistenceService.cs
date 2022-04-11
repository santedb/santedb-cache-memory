/*
 * Copyright (C) 2021 - 2022, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
 * Copyright (C) 2019 - 2021, Fyfe Software Inc. and the SanteSuite Contributors
 * Portions Copyright (C) 2015-2018 Mohawk College of Applied Arts and Technology
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you 
 * may not use this file except in compliance with the License. You may 
 * obtain a copy of the License at 
 * 
 * http://www.apache.org/licenses/LICENSE-2.0 
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations under 
 * the License.
 * 
 * User: fyfej
 * Date: 2021-10-21
 */
using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Jobs;
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Timers;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// An implementation of the <see cref="IQueryPersistenceService"/> which uses in-process memory to store query result sets
    /// </summary>
    /// <remarks>
    /// <para>This implementation of the query persistence service uses the <see cref="System.Runtime.Caching.MemoryCache"/> implementation to store
    /// stateful query results (for consistent pagination) for a period of time in transient place.</para>
    /// </remarks>
    [ServiceProvider("Memory-Based Query Persistence Service", Configuration = typeof(MemoryCacheConfigurationSection))]
    public class MemoryQueryPersistenceService : SanteDB.Core.Services.IQueryPersistenceService
    {
        /// <inheritdoc/>
        public string ServiceName => "Memory-Based Query Persistence / Continuation Service";

        /// <summary>
        /// Memory based query information - metadata about the query stored in the cache
        /// </summary>
        public class MemoryQueryInfo
        {
            /// <summary>
            /// Query info ctor
            /// </summary>
            public MemoryQueryInfo()
            {
                this.CreationTime = DateTime.Now;
            }

            /// <summary>
            /// Total results
            /// </summary>
            public int TotalResults { get; set; }

            /// <summary>
            /// Results in the result set
            /// </summary>
            public List<Guid> Results { get; set; }

            /// <summary>
            /// The query tag
            /// </summary>
            public object QueryTag { get; set; }

            /// <summary>
            /// Get or sets the creation time
            /// </summary>
            public DateTime CreationTime { get; private set; }

            /// <summary>
            /// Get or sets the key
            /// </summary>
            public Guid Key { get; set; }
        }

        //  trace source
        private Tracer m_tracer = new Tracer(MemoryCacheConstants.TraceSourceName);

        // Configuration
        private MemoryCacheConfigurationSection m_configuration = ApplicationServiceContext.Current.GetService<IConfigurationManager>().GetSection<MemoryCacheConfigurationSection>();

        // Cache backing
        private MemoryCache m_cache;

        /// <summary>
        /// Create new persistence
        /// </summary>
        public MemoryQueryPersistenceService()
        {
            var config = new NameValueCollection();
            config.Add("CacheMemoryLimitMegabytes", this.m_configuration?.MaxCacheSize.ToString() ?? "512");
            config.Add("PollingInterval", "00:05:00");
            this.m_cache = new MemoryCache("santedb.query", config);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            this.m_cache.Trim(100);
        }

        /// <inheritdoc/>
        public void AddResults(Guid queryId, IEnumerable<Guid> results, int totalResults)
        {
            var cacheResult = this.m_cache.GetCacheItem($"qry.{queryId}");
            if (cacheResult == null)
                return; // no item
            else if (cacheResult.Value is MemoryQueryInfo retVal)
            {
                this.m_tracer.TraceVerbose("Updating query {0} ({1} results)", queryId, results.Count());
                lock (retVal.Results)
                    retVal.Results.AddRange(results.Where(o => !retVal.Results.Contains(o)).Select(o => o));
                retVal.TotalResults = totalResults;
                this.m_cache.Set(cacheResult.Key, cacheResult.Value, DateTimeOffset.Now.AddSeconds(this.m_configuration.MaxQueryAge));
                //retVal.TotalResults = retVal.Results.Count();
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Guid> GetQueryResults(Guid queryId, int startRecord, int nRecords)
        {
            var cacheResult = this.m_cache.Get($"qry.{queryId}");
            if (cacheResult is MemoryQueryInfo retVal)
                lock (retVal.Results)
                    return retVal.Results.ToArray().Distinct().Skip(startRecord).Take(nRecords).OfType<Guid>().ToArray();
            return null;
        }

        /// <inheritdoc/>
        public object GetQueryTag(Guid queryId)
        {
            var cacheResult = this.m_cache.Get($"qry.{queryId}");
            if (cacheResult is MemoryQueryInfo retVal)
                return retVal.QueryTag;
            return null;
        }

        /// <inheritdoc/>
        public bool IsRegistered(Guid queryId)
        {
            return this.m_cache.Contains($"qry.{queryId}");
        }

        /// <inheritdoc/>
        public long QueryResultTotalQuantity(Guid queryId)
        {
            var cacheResult = this.m_cache.Get($"qry.{queryId}");
            if (cacheResult is MemoryQueryInfo retVal)
                return retVal.TotalResults;
            return 0;
        }

        /// <inheritdoc/>
        public bool RegisterQuerySet(Guid queryId, IEnumerable<Guid> results, object tag, int totalResults)
        {
            this.m_cache.Set($"qry.{queryId}", new MemoryQueryInfo()
            {
                QueryTag = tag,
                Results = results.Select(o => o).ToList(),
                TotalResults = totalResults,
                Key = queryId
            }, DateTimeOffset.Now.AddSeconds(this.m_configuration.MaxQueryAge));
            return true;
        }

        /// <inheritdoc/>
        public Guid FindQueryId(object queryTag)
        {
            return this.m_cache.Select(o => o.Value).OfType<MemoryQueryInfo>().FirstOrDefault(o => o.QueryTag.Equals(queryTag))?.Key ?? Guid.Empty;
        }

        /// <inheritdoc/>
        public void SetQueryTag(Guid queryId, object tagValue)
        {
            var cacheResult = this.m_cache.Get($"qry.{queryId}");
            if (cacheResult is MemoryQueryInfo retVal)
                retVal.QueryTag = tagValue;
        }
    }
}