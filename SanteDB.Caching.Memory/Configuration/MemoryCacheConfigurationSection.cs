/*
 * Portions Copyright 2019-2020, Fyfe Software Inc. and the SanteSuite Contributors (See NOTICE)
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
 * User: fyfej (Justin Fyfe)
 * Date: 2019-11-27
 */
using SanteDB.Core.Configuration;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace SanteDB.Caching.Memory.Configuration
{
    /// <summary>
    /// Create memory cache configuration
    /// </summary>
    [XmlType(nameof(MemoryCacheConfigurationSection), Namespace = "http://santedb.org/configuration")]
    public class MemoryCacheConfigurationSection: IConfigurationSection
    {

        /// <summary>
        /// Memory type configuration
        /// </summary>
        public MemoryCacheConfigurationSection()
        {
            this.MaxCacheSize = 10;
            this.MaxCacheAge = 600;
            this.MaxQueryAge = 3600;
        }

        /// <summary>
        /// Gets or sets the items in the cache
        /// </summary>
        [XmlAttribute("maxSize"), DisplayName("Max Cache Size (MB)"), Description("Sets the maximum size of the in-process memory cache")]
        public int MaxCacheSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum age of items in the cache
        /// </summary>
        [XmlAttribute("maxAge"), DisplayName("Max Cache Age (S)"), Description("Sets the maximum length of time that an object may remain in the in-process memory cache before it is unoladed")]
        public long MaxCacheAge { get; set; }

        /// <summary>
        /// Max query age
        /// </summary>
        [XmlAttribute("maxQueryAge"), DisplayName("Max Query Age (S)"), Description("Sets the maximum length of time (in seconds) a stateful query is retained in the in-process memory cache")]
        public long MaxQueryAge { get; set; }

    }
}
