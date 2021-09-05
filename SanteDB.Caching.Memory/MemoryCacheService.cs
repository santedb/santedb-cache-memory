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
using SanteDB.Core;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Jobs;
using SanteDB.Core.Model;
using SanteDB.Core.Model.Acts;
using SanteDB.Core.Model.Attributes;
using SanteDB.Core.Model.Constants;
using SanteDB.Core.Model.Entities;
using SanteDB.Core.Model.Interfaces;
using SanteDB.Core.Model.Map;
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Xml.Serialization;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// Memory cache service
    /// </summary>
    [ServiceProvider("Memory Cache Service", Configuration = typeof(MemoryCacheConfigurationSection))]
    public class MemoryCacheService : IDataCachingService
    {
        /// <summary>
        /// Gets the service name
        /// </summary>
        public string ServiceName => "Memory Caching Service";

        // Memory cache configuration
        private MemoryCacheConfigurationSection m_configuration;
        private Tracer m_tracer = new Tracer(MemoryCacheConstants.TraceSourceName);
	    private static object s_lock = new object();
        private MemoryCache m_cache;

        // Non cached types
        private HashSet<Type> m_nonCached = new HashSet<Type>();

        /// <summary>
        /// Service is starting
        /// </summary>
        public event EventHandler Started;
        /// <summary>
        /// Service has started
        /// </summary>
        public event EventHandler Starting;
        /// <summary>
        /// Service is stopping
        /// </summary>
        public event EventHandler Stopped;
        /// <summary>
        /// Service has stopped
        /// </summary>
        public event EventHandler Stopping;
        public event EventHandler<DataCacheEventArgs> Added;
        public event EventHandler<DataCacheEventArgs> Updated;
        public event EventHandler<DataCacheEventArgs> Removed;

        /// <summary>
        /// Creates a new memory cache service
        /// </summary>
        public MemoryCacheService(IConfigurationManager configurationManager)
        {
            this.m_configuration = configurationManager.GetSection<MemoryCacheConfigurationSection>();

            if(this.m_configuration == null)
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


            // Look for non-cached types
            foreach (var itm in typeof(IdentifiedData).Assembly.GetTypes().Where(o => o.GetCustomAttribute<NonCachedAttribute>() != null || o.GetCustomAttribute<XmlRootAttribute>() == null))
                this.m_nonCached.Add(itm);

            this.m_cache = new MemoryCache("santedb", config);
        }


        /// <summary>
        /// Gets the specified cache item
        /// </summary>
        /// <returns></returns>
        public TData GetCacheItem<TData>(Guid key) where TData : IdentifiedData
        {
            var retVal = this.GetCacheItem(key);
            if (retVal is TData dat)
                return (TData)dat.Clone();
            else
            {
                this.Remove(key); // wrong type - 
                return default(TData);
            }
        }

        /// <summary>
        /// Get cache key regardless of type
        /// </summary>
        public object GetCacheItem(Guid key) { 
            return this.m_cache.Get(key.ToString());
        }

        /// <summary>
        /// Add the specified item to the memory cache
        /// </summary>
        public void Add(IdentifiedData data) 
        {
			// if the data is null, continue
	        if (data == null || !data.Key.HasValue ||
                    (data as BaseEntityData)?.ObsoletionTime.HasValue == true ||
                    this.m_nonCached.Contains(data.GetType()))
	        {
		        return;
	        }

            var cacheKey = data.Key.Value.ToString();
            var exist = this.m_cache.Get(cacheKey);

            var dataClone = data.Clone();
            dataClone.BatchOperation = Core.Model.DataTypes.BatchOperationType.Auto;
            if (dataClone is ITaggable taggable)
            {
                // TODO: Put this as a constant
                // Don't cache generated data
                if(taggable.GetTag("$generated") == "true")
                {
                    return;
                }

                foreach (var tag in taggable.Tags.Where(o => o.TagKey.StartsWith("$")).ToArray())
                {
                    taggable.RemoveTag(tag.TagKey);
                }
            }

            this.m_cache.Set(cacheKey, dataClone, DateTimeOffset.Now.AddSeconds(this.m_configuration.MaxCacheAge));

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

        /// <summary>
        /// Remove the object from the cache
        /// </summary>
        public void Remove(Guid key)
        {
            var exist = this.m_cache.Get(key.ToString());
            if (exist != null)
            {
                this.m_cache.Remove(key.ToString());
                this.Removed?.Invoke(this, new DataCacheEventArgs(exist));
            }
        }

        /// <summary>
        /// Clear the memory cache
        /// </summary>
        public void Clear()
        {
            this.m_cache.Trim(100);
            
        }

        /// <summary>
        /// Returns true if the object exists in the cache
        /// </summary>
        public bool Exists<T>(Guid id)
        {
            return this.m_cache.Contains(id.ToString());
        }

        /// <summary>
        /// Get the size of the cache in entries
        /// </summary>
        public long Size {  get { return this.m_cache.GetLastSize(); } }
    }
}
