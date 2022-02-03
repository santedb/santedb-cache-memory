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
using SanteDB.Caching.Memory;
using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core.Configuration;
using SanteDB.Core.Exceptions;
using SanteDB.Docker.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace SanteDB.Caching.Memory.Docker
{
    /// <summary>
    /// Exposes the memory cache to the docker configuration
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class RamCachingDockerFeature : IDockerFeature
    {
        /// <summary>
        /// The name of the maximum age setting
        /// </summary>
        public const string MaxAgeSetting = "TTL";

        /// <summary>
        /// Get the id of this feature
        /// </summary>
        public string Id => "RAMCACHE";

        /// <summary>
        /// Settings for the caching feature
        /// </summary>
        /// <remarks>
        /// <list type="table">
        ///     <item><term>SDB_RAMCACHE_TTL</term><description>The maximum time to live of all cache objects</description></item>
        /// </list>
        /// </remarks>
        public IEnumerable<string> Settings => new String[] { MaxAgeSetting };

        /// <summary>
        /// Configure this service
        /// </summary>
        public void Configure(SanteDBConfiguration configuration, IDictionary<string, string> settings)
        {
            // The type of service to add
            Type[] serviceTypes = serviceTypes = new Type[] {
                            typeof(MemoryCacheService),
                            typeof(MemoryAdhocCacheService),
                            typeof(MemoryQueryPersistenceService)
                        };

            // Age
            if (!settings.TryGetValue(MaxAgeSetting, out string maxAge))
            {
                maxAge = "0.1:0:0";
            }

            // Parse
            if (!TimeSpan.TryParse(maxAge, out TimeSpan maxAgeTs))
            {
                throw new ConfigurationException($"{maxAge} is not understood as a timespan", configuration);
            }

            var memSetting = configuration.GetSection<MemoryCacheConfigurationSection>();
            if (memSetting == null)
            {
                memSetting = new MemoryCacheConfigurationSection()
                {
                    MaxCacheSize = 10000
                };
                configuration.AddSection(memSetting);
            }
            memSetting.MaxQueryAge = memSetting.MaxCacheAge = (long)maxAgeTs.TotalSeconds;

            // Add services
            var serviceConfiguration = configuration.GetSection<ApplicationServiceContextConfigurationSection>().ServiceProviders;
            serviceConfiguration.AddRange(serviceTypes.Where(t => !serviceConfiguration.Any(c => c.Type == t)).Select(t => new TypeReferenceConfiguration(t)));
        }
    }
}