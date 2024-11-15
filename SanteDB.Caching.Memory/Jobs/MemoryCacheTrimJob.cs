using SanteDB.Core.Data.Quality;
using SanteDB.Core.Jobs;
using SanteDB.Core.Services;
using SharpCompress;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SanteDB.Caching.Memory.Jobs
{
    /// <summary>
    /// Represents a job which will trim the memory cache
    /// </summary>
    public class MemoryCacheTrimJob : IJob
    {

        /// <summary>
        /// Memory caches
        /// </summary>
        private readonly IMemoryCache[] m_cacheServices;
        private readonly IJobStateManagerService m_stateManager;

        /// <summary>
        /// JOB ID
        /// </summary>
        public static readonly Guid JOB_ID = Guid.Parse("E8F3CDFA-3288-41E2-9BD2-BD8E59ABD76E");

        /// <summary>
        /// DI Constructor
        /// </summary>
        public MemoryCacheTrimJob(IServiceManager serviceManager, IJobStateManagerService stateManagerService)
        {
            m_cacheServices = serviceManager.GetServices().OfType<IMemoryCache>().ToArray();
            this.m_stateManager = stateManagerService;
        }

        /// <inheritdoc/>
        public Guid Id => JOB_ID;

        /// <inheritdoc/>
        public string Name => "Memory Cache Trim";

        /// <inheritdoc/>
        public string Description => "Trims & cleans the memory caches which are running on this device";

        /// <inheritdoc/>
        public bool CanCancel => false;

        /// <inheritdoc/>
        public IDictionary<string, Type> Parameters => new Dictionary<String, Type>();

        /// <inheritdoc/>
        public void Cancel()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public void Run(object sender, EventArgs e, object[] parameters)
        {
            this.m_stateManager.SetState(this, JobStateType.Running);

            try
            {
                int i = 0;
                foreach (var itm in this.m_cacheServices)
                {
                    this.m_stateManager.SetProgress(this, $"Trimming {itm.CacheName}", (float)i++ / (float)this.m_cacheServices.Length);
                    itm.Trim();
                }
                this.m_stateManager.SetState(this, JobStateType.Completed);
            }
            catch (Exception ex)
            {
                this.m_stateManager.SetState(this, JobStateType.Aborted, ex.ToHumanReadableString());
            }
        }
    }
}
