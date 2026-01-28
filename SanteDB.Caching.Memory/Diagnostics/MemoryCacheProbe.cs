/*
 * Copyright (C) 2021 - 2026, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
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
 * Date: 2024-12-12
 */
using SanteDB.Caching.Memory.Session;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SanteDB.Caching.Memory.Diagnostics
{
    /// <summary>
    /// Represents a diagnostics probe which is for the memory caches registered on this server
    /// </summary>
    public class MemoryCacheProbe : ICompositeDiagnosticsProbe
    {
        
        // Performance counters
        private readonly IDiagnosticsProbe[] m_childProbes;
        private readonly Type[] m_probeTypes = new Type[]
        {
            typeof(MemoryCacheService),
            typeof(MemoryAdhocCacheService),
            typeof(MemorySessionManagerService),
            typeof(MemoryQueryPersistenceService)
        };

        /// <summary>
        /// Memory cache size
        /// </summary>
        private class MemoryCacheSizeProbe : DiagnosticsProbeBase<double>
        {
            // Mem cache
            private readonly IMemoryCache m_memoryCache;
            private readonly Guid m_uuid = Guid.NewGuid();

            /// <summary>
            /// Create a new memory cache probe
            /// </summary>
            public MemoryCacheSizeProbe(IMemoryCache memoryCacheToMonitor) : base($"{memoryCacheToMonitor.CacheName} Size", $"Reports the size used for {memoryCacheToMonitor.CacheName}")
            {
                this.m_memoryCache = memoryCacheToMonitor;
            }

            public override double Value => this.m_memoryCache.Size() / 1024f;

            public override Guid Uuid => this.m_uuid;

            public override string Unit => "k";
        }

        /// <summary>
        /// Memory cache size
        /// </summary>
        private class MemoryCacheCountProbe : DiagnosticsProbeBase<long>
        {
            // Mem cache
            private readonly IMemoryCache m_memoryCache;
            private readonly Guid m_uuid = Guid.NewGuid();

            /// <summary>
            /// Create a new memory cache probe
            /// </summary>
            public MemoryCacheCountProbe(IMemoryCache memoryCacheToMonitor) : base($"{memoryCacheToMonitor.CacheName} Count", $"Reports the number of cache objects in {memoryCacheToMonitor.CacheName}")
            {
                this.m_memoryCache = memoryCacheToMonitor;
            }

            public override long Value => this.m_memoryCache.Count();

            public override Guid Uuid => this.m_uuid;

            public override string Unit => null;
        }

        /// <summary>
        /// Memory cache probe
        /// </summary>
        public MemoryCacheProbe(IServiceProvider serviceProvider)
        {
            this.m_childProbes = this.m_probeTypes.Select(s=>serviceProvider.GetService(s)).OfType<IMemoryCache>().SelectMany(c => this.CreateProbesFor(c)).ToArray();
        }

        /// <summary>
        /// Create probes for the <paramref name="cache"/>
        /// </summary>
        private IEnumerable<IDiagnosticsProbe> CreateProbesFor(IMemoryCache cache)
        {
            yield return new MemoryCacheCountProbe(cache);
            //yield return new MemoryCacheSizeProbe(cache);
        }

        /// <summary>
        /// Probe identifier
        /// </summary>
        public static readonly Guid PROBE_ID = Guid.Parse("41ECEC33-3E6B-4FEE-A4F6-055E9C2FCE07");

        /// <inheritdoc/>
        public IEnumerable<IDiagnosticsProbe> Value => this.m_childProbes;

        /// <inheritdoc/>
        public Guid Uuid => PROBE_ID;

        /// <inheritdoc/>
        public string Name => "Memory Cache Services";

        /// <inheritdoc/>
        public string Description => "Shows statistics about registered memory caching services";

        /// <inheritdoc/>
        public Type Type => typeof(Array);

        /// <inheritdoc/>
        public string Unit => null;

        /// <inheritdoc/>
        object IDiagnosticsProbe.Value => this.Value;
    }
}
