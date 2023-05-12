﻿/*
 * Copyright (C) 2021 - 2023, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
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
 * Date: 2023-3-10
 */
using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Model.Interfaces;
using SanteDB.Core.Services;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Caching;
using System.Text.RegularExpressions;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// An implementation of <see cref="IAdhocCacheService"/> which uses the in-process memory cache
    /// </summary>
    /// <remarks>
    /// <para>This implementation of the adhoc caching service uses in-process memory to store unstructured data
    /// which is commonly used in the application.</para>
    /// </remarks>
    /// <seealso cref="IAdhocCacheService"/>
    [ServiceProvider("Memory Ad-Hoc Cache Service", Configuration = typeof(MemoryCacheConfigurationSection))]
    public class MemoryAdhocCacheService : IAdhocCacheService
    {
        /// <summary>
        /// Gets the service name
        /// </summary>
        public string ServiceName => "Memory Ad-Hoc Caching Service";

        //  trace source
        private readonly Tracer m_tracer = new Tracer(MemoryCacheConstants.TraceSourceName);

        private readonly MemoryCacheConfigurationSection m_configuration;

        // The backing cache
        private MemoryCache m_cache;

        /// <summary>
        /// Ad-hoc cache initialization
        /// </summary>
        public MemoryAdhocCacheService(IConfigurationManager configurationManager)
        {
            this.m_configuration = configurationManager.GetSection<MemoryCacheConfigurationSection>();
            if (this.m_configuration == null)
            {
                this.m_configuration = new MemoryCacheConfigurationSection()
                {
                    MaxCacheAge = 60,
                    MaxCacheSize = 512,
                    MaxQueryAge = 3600
                };
            }
            var config = new NameValueCollection();
            config.Add("CacheMemoryLimitMegabytes", Math.Truncate((this.m_configuration?.MaxCacheSize ?? 512) * 0.25).ToString());
            config.Add("PhysicalMemoryLimitPercentage", "20");
            config.Add("PollingInterval", "00:01:00");
            this.m_cache = new MemoryCache("santedb.adhoc", config, true);



        }

        /// <summary>
        /// Add the specified data to the cache
        /// </summary>
        public void Add<T>(string key, T value, TimeSpan? timeout = null)
        {
            try
            {
                if (value is ICanDeepCopy icdc)
                {
                    this.m_cache.Set(key, icdc.DeepCopy(), DateTimeOffset.Now.AddSeconds(timeout?.TotalSeconds ?? this.m_configuration.MaxCacheAge));
                }
                else
                {
                    this.m_cache.Set(key, (object)value ?? DBNull.Value, DateTimeOffset.Now.AddSeconds(timeout?.TotalSeconds ?? this.m_configuration.MaxCacheAge));
                }
            }
            catch (Exception e)
            {
                this.m_tracer.TraceError("Error adding {0} to cache - {1}", value, e.Message);
                //throw new Exception($"Error adding {value} to cache", e);
            }
        }

        /// <summary>
        /// Gets the specified value from the cache
        /// </summary>
        public T Get<T>(string key)
        {
            if (this.TryGet<T>(key, out T value))
            {
                return value;
            }
            else
            {
                return default(T);
            }
        }

        /// <inheritdoc/>
        public bool TryGet<T>(String key, out T value)
        {
            try
            {
                var data = this.m_cache.Get(key);
                if (data == null || data == DBNull.Value)
                {
                    value = default(T);
                }
                else
                {
                    value = (T)data;
                }
                return this.m_cache.Contains(key);
            }
            catch (Exception e)
            {
                this.m_tracer.TraceError("Error fetch {0} from cache - {1}", key, e.Message);
                //throw new Exception($"Error fetching {key} ({typeof(T).FullName}) from cache", e);
                value = default(T);
                return false;
            }
        }

        /// <summary>
        /// Remove the specified key
        /// </summary>
        public bool Remove(string key)
        {
            return this.m_cache.Remove(key) != null;
        }

        /// <inheritdoc/>
        public void RemoveAll(string pattern)
        {
            var regex = new Regex(pattern);
            var keys = this.m_cache.Where(o => regex.IsMatch(o.Key)).Select(o => o.Key).ToList();
            foreach (var k in keys)
            {
                this.Remove(k);
            }
        }

        /// <summary>
        /// Return true if the cache item exists in cache
        /// </summary>
        public bool Exists(string key)
        {
            return this.m_cache.Contains(key);
        }
    }
}