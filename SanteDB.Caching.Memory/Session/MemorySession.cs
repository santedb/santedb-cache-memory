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
 * Date: 2023-6-21
 */
using SanteDB.Core.Security;
using SanteDB.Core.Security.Claims;
using System;
using System.Collections.Generic;
using System.Security.Principal;

namespace SanteDB.Caching.Memory.Session
{
    /// <summary>
    /// A <see cref="ISession"/> which is stored only in memory
    /// </summary>
    internal class MemorySession : ISession
    {
        // Claims for this object
        private readonly List<IClaim> m_claims = new List<IClaim>();

        /// <summary>
        /// Create a new memory session
        /// </summary>
        internal MemorySession(byte[] id, DateTimeOffset notBefore, DateTimeOffset notAfter, byte[] refreshToken, IClaim[] claims, IPrincipal principal)
        {
            this.m_claims = new List<IClaim>(claims);
            this.Id = id;
            this.NotBefore = notBefore;
            this.NotAfter = notAfter;
            this.RefreshToken = refreshToken;
            this.Principal = principal;

        }

        /// <summary>
        /// Get the refresh token
        /// </summary>
        internal String RefreshTokenString => this.RefreshToken?.HexEncode();

        /// <summary>
        /// Gets the identifier of the session
        /// </summary>
        public byte[] Id { get; private set; }

        /// <summary>
        /// The session is not valid before this time
        /// </summary>
        public DateTimeOffset NotBefore { get; private set; }

        /// <summary>
        /// The session is not valid after this time
        /// </summary>
        public DateTimeOffset NotAfter { get; private set; }

        /// <summary>
        /// The session refresh token
        /// </summary>
        public byte[] RefreshToken { get; private set; }

        /// <summary>
        /// Claims for this session
        /// </summary>
        public IClaim[] Claims => this.m_claims.ToArray();

        /// <summary>
        /// The princpal which this session wraps
        /// </summary>
        internal IPrincipal Principal { get; private set; }
    }
}
