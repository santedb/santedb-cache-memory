/*
 * Copyright (C) 2021 - 2025, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
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
 * Date: 2023-6-21
 */
using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Model;
using SanteDB.Core.Model.Attributes;
using SanteDB.Core.Model.Collection;
using SanteDB.Core.Model.Constants;
using SanteDB.Core.Model.Interfaces;
using SanteDB.Core.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SanteDB.Core.Model.Acts;
using SanteDB.Core.Model.Entities;
using SanteDB.Core.Model.DataTypes;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// Implementation of the <see cref="IDataCachingService"/> which uses the in-process memory to cache objects
    /// </summary>
    /// <remarks>
    /// <para>The memory cache service uses the <see cref="System.Runtime.Caching.MemoryCache"/> class as a backing cache
    /// for the SanteDB host instance. This caching provider provides benefits over a common, shared cache like REDIS in that:</para>
    /// <list type="bullet">
    ///     <item>It does not require the setup of a third-party service to operate</item>
    ///     <item>The cache objects are directly accessed and not serialized</item>
    ///     <item>The cache objects are protected within the host process memory</item>
    ///     <item>The access is very fast - there is no interconnection with another process</item>
    /// </list>
    /// <para>This cache service should only be used in cases when there is a single SanteDB server and there is no need
    /// for sharing cache objects between application services.</para>
    /// <para>This class uses the TTL setting from the <see cref="MemoryCacheConfigurationSection"/> to determine the length of time
    /// that cache entries are valid</para>
    /// </remarks>
    [ServiceProvider("Memory Cache Service", Configuration = typeof(MemoryCacheConfigurationSection))]
    public class MemoryCacheService : IDataCachingService, IDaemonService, IMemoryCache
    {

        /// <summary>
        /// Consistent indicator
        /// </summary>
        private struct CacheConsistentIndicator { }

        /// <summary>
        /// Gets the service name
        /// </summary>
        public string ServiceName => "Memory Caching Service";

        /// <inheritdoc/>
        public string CacheName => "Data";


        // Memory cache configuration
        private MemoryCacheConfigurationSection m_configuration;

        // Tracer
        private readonly Tracer m_tracer = new Tracer(MemoryCacheConstants.TraceSourceName);

        // Private cache
        private MemoryCache m_cache;

        // Non cached types
        private HashSet<Type> m_nonCached = new HashSet<Type>();

        /// <summary>
        /// True when the memory cache is running
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return this.m_cache != null;
            }
        }

        /// <inheritdoc/>
        public event EventHandler Started;

        /// <inheritdoc/>
        public event EventHandler Starting;

        /// <inheritdoc/>
        public event EventHandler Stopped;

        /// <inheritdoc/>
        public event EventHandler Stopping;

        /// <inheritdoc/>
        public event EventHandler<DataCacheEventArgs> Added;

        /// <inheritdoc/>
        public event EventHandler<DataCacheEventArgs> Updated;

        /// <inheritdoc/>
        public event EventHandler<DataCacheEventArgs> Removed;

        /// <summary>
        /// Creates a new memory cache service
        /// </summary>
        public MemoryCacheService(IConfigurationManager configurationManager)
        {
            this.m_configuration = configurationManager.GetSection<MemoryCacheConfigurationSection>();

            if (this.m_configuration == null)
            {
                this.m_configuration = new MemoryCacheConfigurationSection()
                {
                    MaxCacheAge = 600,
                    MaxCacheSize = 1024,
                    MaxQueryAge = 3600
                };
            }
            var config = new NameValueCollection();
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (isWindows)
            {
                config.Add("PhysicalMemoryLimitPercentage", "50"); // Windows
            }
            config.Add("CacheMemoryLimitMegabytes", Math.Truncate((this.m_configuration?.MaxCacheSize ?? 512) * 0.5).ToString());
            config.Add("PollingInterval", "00:01:00");

            this.m_cache = new MemoryCache("santedb", config);

        }

        /// <inheritdoc/>
        public bool Start()
        {
            this.m_tracer.TraceInfo("Starting Memory Caching Service...");

            this.Starting?.Invoke(this, EventArgs.Empty);

            // Look for non-cached types
            foreach (var itm in typeof(IdentifiedData).Assembly.GetTypes().Where(o => o.GetCustomAttribute<NonCachedAttribute>() != null || o.GetCustomAttribute<XmlRootAttribute>() == null))
            {
                this.m_nonCached.Add(itm);
            }

            this.Started?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Ensure cache consistency
        /// </summary>
        /// <remarks>This method ensures that referenced objects (objects which are stored or updated which
        /// are associative in nature) have their source and target objects evicted from cache.</remarks>
        private void EnsureCacheConsistency(IdentifiedData data)
        {
            // No data - no consistency needed
            if (data == null || data.GetAnnotations<CacheConsistentIndicator>().Any()) { return; }
            data.AddAnnotation(new CacheConsistentIndicator());

            // If it is a bundle we want to process the bundle
            switch (data)
            {
                case Bundle bundle:
                    foreach (var itm in bundle.Item)
                    {
                        if (itm.BatchOperation == Core.Model.DataTypes.BatchOperationType.Delete)
                        {
                            this.Remove(itm);
                        }
                        else
                        {
                            this.Add(itm);
                        }
                    }
                    break;
                case ISimpleAssociation sa:
                    if (sa is IdentifiedData id && id.BatchOperation != BatchOperationType.Ignore && id.BatchOperation != BatchOperationType.Auto)
                    {
                        this.Remove(sa.SourceEntityKey.GetValueOrDefault()); // force a reload of the source object from disk
                    }
                    break;
            }
            //data.BatchOperation = Core.Model.DataTypes.BatchOperationType.Auto;
            //data.BatchOperation = Core.Model.DataTypes.BatchOperationType.Auto;

        }

        /// <inheritdoc/>
        public bool Stop()
        {
            this.Stopping?.Invoke(this, EventArgs.Empty);

            this.m_cache.Dispose();
            this.m_cache = null;
            this.Stopped?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <inheritdoc/>
        public TData GetCacheItem<TData>(Guid key) where TData : IdentifiedData => this.GetCacheItem(key) as TData;

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public IdentifiedData GetCacheItem(Guid key)
        {
            var retVal = this.m_cache?.Get(key.ToString());
            if (retVal is IdentifiedData id)
            {
                var cloned = id.DeepCopy() as IdentifiedData;
                cloned.AddAnnotation(id.GetAnnotations<LoadMode>().FirstOrDefault());
                return cloned;
            }
            else
            {
                return null;
            }
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public void Add(IdentifiedData data)
        {
            try
            {
                this.EnsureCacheConsistency(data);
                // if the data is null, continue
                if (data == null ||
                    !data.Key.HasValue ||
                    (data as BaseEntityData)?.ObsoletionTime.HasValue == true ||
                    this.m_nonCached.Contains(data.GetType()))
                {
                    return;
                }
                else if (data is IHasPolicies ihp && ihp.Policies?.Any() == true)
                {
                    return;
                }
                else if (data.GetType().GetCustomAttribute<NonCachedAttribute>() != null)
                {
                    this.m_nonCached.Add(data.GetType());
                    return;
                }

                var exist = this.m_cache.Get(data.Key.ToString());

                var dataClone = data.DeepCopy() as IdentifiedData;
                dataClone.BatchOperation = Core.Model.DataTypes.BatchOperationType.Auto;
                dataClone.AddAnnotation(data.GetAnnotations<LoadMode>().FirstOrDefault());

                if (dataClone is ITaggable taggable)
                {
                    // TODO: Put this as a constant
                    // Don't cache generated data
                    if (taggable.GetTag("$generated") == "true")
                    {
                        return;
                    }

                    taggable.RemoveAllTags(o => o.TagKey.StartsWith("$") && o.TagKey != SystemTagNames.DcdrRefetchTag);

                }

                var cacheItem = new CacheItem(data.Key.ToString());
                cacheItem.Value = dataClone;

                this.m_cache.Set(cacheItem, new CacheItemPolicy()
                {
                    AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(this.m_configuration.MaxCacheAge),
                    Priority = CacheItemPriority.Default
                });

                // If this is a relationship class we remove the source entity from the cache
                if (data is ITargetedAssociation targetedAssociation)
                {
                    this.m_cache.Remove(targetedAssociation.SourceEntityKey.ToString());
                    this.m_cache.Remove(targetedAssociation.TargetEntityKey.ToString());
                }
                else if (data is ISimpleAssociation simpleAssociation)
                {
                    this.m_cache.Remove(simpleAssociation.SourceEntityKey.ToString());
                }

                if (exist != null)
                {
                    this.Updated?.Invoke(this, new DataCacheEventArgs(data));
                }
                else
                {
                    this.Added?.Invoke(this, new DataCacheEventArgs(data));
                }
            }
            catch (Exception ex)
            {
                this.m_tracer.TraceWarning("Could not cache object {0} - {1}", data, ex);
            }
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public void Remove(Guid key)
        {
            var exist = this.m_cache.Get(key.ToString());
            if (exist != null)
            {
                this.Remove(exist as IdentifiedData);
            }
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public void Remove(IdentifiedData entry)
        {
            if (entry != null)
            {
                this.m_cache?.Remove(entry.Key.ToString());
                entry.BatchOperation = Core.Model.DataTypes.BatchOperationType.Delete;
                this.EnsureCacheConsistency(entry);
                this.Removed?.Invoke(this, new DataCacheEventArgs(entry));
            }
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public void Clear()
        {
            this.m_cache.Trim(100);
        }

        /// <summary>
        /// Determines if the object exists
        /// </summary>
        public bool Exists<T>(Guid id) where T : IdentifiedData
        {
            return this.m_cache.Get(id.ToString()) is T;
        }

        /// <inheritdoc/>
        public void Trim()
        {
            this.m_cache.Trim(50);
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public long Size
        { get { return this.m_cache.GetLastSize(); } }


        /// <inheritdoc/>
        long IMemoryCache.Size() => this.m_cache.GetLastSize();

        /// <inheritdoc/>
        public long Count() => this.m_cache.GetCount();

    }
}