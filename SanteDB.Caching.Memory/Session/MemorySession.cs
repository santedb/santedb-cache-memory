using SanteDB.Core.Security;
using SanteDB.Core.Security.Claims;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;

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
