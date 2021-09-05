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
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

namespace SanteDB.Caching.Memory
{
    /// <summary>
    /// REDIS ad-hoc cache
    /// </summary>
    [ServiceProvider("Memory Ad-Hoc Cache Service", Configuration = typeof(MemoryCacheConfigurationSection))]
    public class MemoryAdhocCacheService : IAdhocCacheService
    {

        /// <summary>
        /// Gets the service name
        /// </summary>
        public string ServiceName => "Memory Ad-Hoc Caching Service";

        //  trace source
        private Tracer m_tracer = new Tracer(MemoryCacheConstants.TraceSourceName);
        private MemoryCacheConfigurationSection m_configuration = ApplicationServiceContext.Current.GetService<IConfigurationManager>().GetSection<MemoryCacheConfigurationSection>();
        private MemoryCache m_cache;

        /// <summary>
        /// Adhoc cache init
        /// </summary>
        public MemoryAdhocCacheService()
        {
            var config = new NameValueCollection();
            config.Add("cacheMemoryLimitMegabytes", this.m_configuration.MaxCacheSize.ToString());
            config.Add("pollingInterval", "00:05:00");

            this.m_cache = new MemoryCache("santedb.adhoc", config);
        }
       

        /// <summary>
        /// Add the specified data to the cache
        /// </summary>
        public void Add<T>(string key, T value, TimeSpan? timeout = null)
        {
            try
            {
                if (Object.Equals(value, default(T))) return;
                this.m_cache.Set(key, value, DateTimeOffset.Now.AddSeconds(timeout?.TotalSeconds ?? this.m_configuration.MaxCacheAge));
            }
            catch (Exception e)
            {
                this.m_tracer.TraceError("Error adding {0} to cache", value);
                //throw new Exception($"Error adding {value} to cache", e);
            }
        }

        /// <summary>
        /// Gets the specified value from the cache
        /// </summary>
        public T Get<T>(string key)
        {
            try
            {
                var data = this.m_cache.Get(key);
                if(data == null)
                    return default(T);
                return (T)data;
            }
            catch (Exception e)
            {
                this.m_tracer.TraceError("Error fetch {0} from cache", key);
                //throw new Exception($"Error fetching {key} ({typeof(T).FullName}) from cache", e);
                return default(T);
            }
        }

        /// <summary>
        /// Remove the specified key
        /// </summary>
        public bool Remove(string key)
        {
            return this.m_cache.Remove(key) != null;
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
