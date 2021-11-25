/*
 * Copyright (C) 2021 - 2021, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
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
 * Date: 2021-8-5
 */

using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Model;
using SanteDB.Core.Model.Acts;
using SanteDB.Core.Model.Attributes;
using SanteDB.Core.Model.Entities;
using SanteDB.Core.Model.Interfaces;
using SanteDB.Core.Model.Map;
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Xml.Serialization;

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
    public class MemoryCacheService : IDataCachingService, IDaemonService
    {
        /// <summary>
        /// Gets the service name
        /// </summary>
        public string ServiceName => "Memory Caching Service";

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
                    MaxCacheAge = 60000,
                    MaxCacheSize = 1024,
                    MaxQueryAge = 60000
                };
            }
            var config = new NameValueCollection();
            config.Add("cacheMemoryLimitMegabytes", this.m_configuration?.MaxCacheSize.ToString());
            config.Add("pollingInterval", "00:05:00");

            this.m_cache = new MemoryCache("santedb", config);
        }

        /// <inheritdoc/>
        public bool Start()
        {
            this.m_tracer.TraceInfo("Starting Memory Caching Service...");

            this.Starting?.Invoke(this, EventArgs.Empty);

            // subscribe to events
            this.Added += (o, e) => this.EnsureCacheConsistency(e);
            this.Updated += (o, e) => this.EnsureCacheConsistency(e);
            this.Removed += (o, e) => this.EnsureCacheConsistency(e);

            // Look for non-cached types
            foreach (var itm in typeof(IdentifiedData).Assembly.GetTypes().Where(o => o.GetCustomAttribute<NonCachedAttribute>() != null || o.GetCustomAttribute<XmlRootAttribute>() == null))
                this.m_nonCached.Add(itm);

            this.Started?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Ensure cache consistency
        /// </summary>
        /// <remarks>This method ensures that referenced objects (objects which are stored or updated which
        /// are associative in nature) have their source and target objects evicted from cache.</remarks>
        private void EnsureCacheConsistency(DataCacheEventArgs e)
        {
            //// Relationships should always be clean of source/target so the source/target will load the new relationship
            if (e.Object is ActParticipation)
            {
                var ptcpt = (e.Object as ActParticipation);

                this.Remove(ptcpt.SourceEntityKey.GetValueOrDefault());
                this.Remove(ptcpt.PlayerEntityKey.GetValueOrDefault());
                //MemoryCache.Current.RemoveObject(ptcpt.PlayerEntity?.GetType() ?? typeof(Entity), ptcpt.PlayerEntityKey);
            }
            else if (e.Object is ActRelationship)
            {
                var rel = (e.Object as ActRelationship);
                this.Remove(rel.SourceEntityKey.GetValueOrDefault());
                this.Remove(rel.TargetActKey.GetValueOrDefault());
            }
            else if (e.Object is EntityRelationship)
            {
                var rel = (e.Object as EntityRelationship);
                this.Remove(rel.SourceEntityKey.GetValueOrDefault());
                this.Remove(rel.TargetEntityKey.GetValueOrDefault());
            }
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
        public TData GetCacheItem<TData>(Guid key) where TData : IdentifiedData
        {
            var retVal = this.m_cache.Get(key.ToString());
            if (retVal is TData dat)
                return (TData)dat.Clone();
            else
            {
                this.Remove(key); // wrong type -
                return default(TData);
            }
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public IdentifiedData GetCacheItem(Guid key)
        {
            var retVal = this.m_cache.Get(key.ToString());
            if (retVal is IdentifiedData id)
            {
                return id.Clone();
            }
            else
                return retVal as IdentifiedData;
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public void Add(IdentifiedData data)
        {
            // if the data is null, continue
            if (data == null || !data.Key.HasValue ||
                    (data as BaseEntityData)?.ObsoletionTime.HasValue == true ||
                    this.m_nonCached.Contains(data.GetType()))
            {
                return;
            }

            var exist = this.m_cache.Get(data.Key.ToString());

            var dataClone = data.Clone();
            dataClone.BatchOperation = Core.Model.DataTypes.BatchOperationType.Auto;
            if (dataClone is ITaggable taggable)
            {
                // TODO: Put this as a constant
                // Don't cache generated data
                if (taggable.GetTag("$generated") == "true")
                {
                    return;
                }

                foreach (var tag in taggable.Tags.Where(o => o.TagKey.StartsWith("$")).ToArray())
                {
                    taggable.RemoveTag(tag.TagKey);
                }
            }

            this.m_cache.Set(data.Key.ToString(), dataClone, DateTimeOffset.Now.AddSeconds(this.m_configuration.MaxCacheAge));

            // If this is a relationship class we remove the source entity from the cache
            if (data is ITargetedAssociation targetedAssociation)
            {
                this.m_cache.Remove(targetedAssociation.SourceEntityKey.ToString());
                this.m_cache.Remove(targetedAssociation.TargetEntityKey.ToString());
            }
            else if (data is ISimpleAssociation simpleAssociation)
                this.m_cache.Remove(simpleAssociation.SourceEntityKey.ToString());

            if (exist != null)
                this.Updated?.Invoke(this, new DataCacheEventArgs(data));
            else
                this.Added?.Invoke(this, new DataCacheEventArgs(data));
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
            this.m_cache.Remove(entry.Key.ToString());
            if (entry is ISimpleAssociation sa)
            {
                this.Remove(sa.SourceEntityKey.GetValueOrDefault());
                if (sa is ITargetedAssociation ta)
                {
                    this.Remove(ta.TargetEntityKey.GetValueOrDefault());
                }
            }
            this.Removed?.Invoke(this, new DataCacheEventArgs(entry));
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
        public bool Exists<T>(Guid id)
        {
            return this.m_cache.Get(id.ToString()) is T;
        }

        /// <inheritdoc/>
        /// <threadsafety static="true" instance="true"/>
        public long Size
        { get { return this.m_cache.GetLastSize(); } }
    }
}