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
 * Date: 2024-10-18
 */
using SanteDB.Core.Security.Claims;
using SanteDB.Core.Security.Principal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace SanteDB.Caching.Memory.Session
{
    /// <summary>
    /// Represents a <see cref="IPrincipal"/>
    /// </summary>
    internal class MemorySessionPrincipal : IClaimsPrincipal
    {
        private readonly MemorySession m_session;
        private readonly IClaimsPrincipal m_claimsPrincipal;

        /// <summary>
        /// Create a new memory session principal
        /// </summary>
        internal static MemorySessionPrincipal Create(MemorySession session)
        {
            switch (session.Principal)
            {
                case ITokenPrincipal itp:
                    return new MemorySessionTokenPrincipal(session);
                case IClaimsPrincipal icp:
                    return new MemorySessionPrincipal(session);
                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Private ctor
        /// </summary>
        protected MemorySessionPrincipal(MemorySession session)
        {
            this.m_session = session;
            this.m_claimsPrincipal = session.Principal as IClaimsPrincipal;
        }

        /// <summary>
        /// Get all claims for this principal
        /// </summary>
        public IEnumerable<IClaim> Claims => this.m_session.Claims;

        /// <summary>
        /// Get all identities associated with this session
        /// </summary>
        public IClaimsIdentity[] Identities => this.m_claimsPrincipal.Identities;

        /// <summary>
        /// Get the primary identity
        /// </summary>
        public IIdentity Identity => this.Identities[0];

        /// <summary>
        /// Add an identity to this sesison
        /// </summary>
        public void AddIdentity(IIdentity identity)
        {
            throw new NotSupportedException("You cannot add an identity to an existing session. Create a new session and then call Authenticate on the ISessionIdentityProviderService.");
        }

        /// <summary>
        /// Find all claims 
        /// </summary>
        public IEnumerable<IClaim> FindAll(string claimType)
        {
            return this.Claims.Where(o => o.Type == claimType);
        }

        /// <summary>
        /// Find the first object
        /// </summary>
        public IClaim FindFirst(string claimType) => this.FindAll(claimType).FirstOrDefault();

        /// <summary>
        /// True if the object has the specified claim
        /// </summary>
        public bool HasClaim(Func<IClaim, bool> predicate) => this.Claims.Any(predicate);

        /// <summary>
        /// True if the session has a role
        /// </summary>
        public bool IsInRole(string role) => this.Claims.Any(o => o.Type == SanteDBClaimTypes.DefaultRoleClaimType && o.Value.Equals(role, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Try to get the specified claim value
        /// </summary>
        public bool TryGetClaimValue(string claimType, out string value)
        {
            value = this.Claims.FirstOrDefault(o => o.Type == claimType)?.Value;
            return String.IsNullOrEmpty(claimType);
        }
    }

    /// <summary>
    /// Token principal 
    /// </summary>
    internal class MemorySessionTokenPrincipal : MemorySessionPrincipal, ITokenPrincipal
    {
        private readonly ITokenPrincipal m_tokenPrincipal;

        public MemorySessionTokenPrincipal(MemorySession session) : base(session)
        {
            this.m_tokenPrincipal = session.Principal as ITokenPrincipal;
        }

        /// <inheritdoc/>
        public string AccessToken => this.m_tokenPrincipal.AccessToken;

        /// <inheritdoc/>
        public string TokenType => this.m_tokenPrincipal.TokenType;

        /// <inheritdoc/>
        public DateTimeOffset ExpiresAt => this.m_tokenPrincipal.ExpiresAt;

        /// <inheritdoc/>
        public string IdentityToken => this.m_tokenPrincipal.IdentityToken;

        /// <inheritdoc/>
        public string RefreshToken => this.m_tokenPrincipal.RefreshToken;
    }
}