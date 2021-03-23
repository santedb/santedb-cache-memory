using SanteDB.Caching.Memory;
using SanteDB.Caching.Memory.Configuration;
using SanteDB.Core.Configuration;
using SanteDB.Core.Exceptions;
using SanteDB.Docker.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SanteDB.Caching.Memory.Docker
{
    /// <summary>
    /// Caching feature
    /// </summary>
    public class RamCachingFeature : IDockerFeature
    {

        public const string MaxAgeSetting = "EXPIRE";

        /// <summary>
        /// Get the id of this feature
        /// </summary>
        public string Id => "RAMCACHE";

        /// <summary>
        /// Settings for the caching feature
        /// </summary>
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
                maxAge = "PT1H";
            }

            // Parse
            if (!TimeSpan.TryParse(maxAge, out TimeSpan maxAgeTs))
            {
                throw new ConfigurationException($"{maxAge} is not understood as a timespan", configuration);
            }


            var memSetting = configuration.GetSection<MemoryCacheConfigurationSection>();
            if (memSetting == null)
            {
                memSetting = DockerFeatureUtils.LoadConfigurationResource<MemoryCacheConfigurationSection>("SanteDB.Caching.Memory.Docker.MemCacheFeature.xml");
                configuration.AddSection(memSetting);
            }
            memSetting.MaxQueryAge = memSetting.MaxCacheAge = (long)maxAgeTs.TotalSeconds;
            
            // Add services
            var serviceConfiguration = configuration.GetSection<ApplicationServiceContextConfigurationSection>().ServiceProviders;
            serviceConfiguration.AddRange(serviceTypes.Where(t => !serviceConfiguration.Any(c => c.Type == t)).Select(t => new TypeReferenceConfiguration(t)));
        }
}
}
