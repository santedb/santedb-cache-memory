/*
 * Copyright (C) 2021 - 2024, SanteSuite Inc. and the SanteSuite Contributors (See NOTICE.md for full copyright notices)
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
using SanteDB.Core.Configuration;
using SanteDB.Core.Exceptions;
using SanteDB.Core.i18n;
using SanteDB.Core.Model.Constants;
using SanteDB.Core.Security;
using SanteDB.Core.Security.Claims;
using SanteDB.Core.Security.Configuration;
using SanteDB.Core.Security.Principal;
using SanteDB.Core.Security.Services;
using SanteDB.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace SanteDB.Caching.Memory.Session
{
    /// <summary>
    /// Represents a <see cref="ISessionProviderService"/> which uses RAM caching
    /// </summary>
    public class MemorySessionManagerService : ISessionProviderService, ISessionIdentityProviderService
    {
        /// <summary>
        /// Sessions
        /// </summary>
        private readonly MemoryCache m_session = new MemoryCache("$sdb-ade.session");
        private readonly ISessionTokenEncodingService m_sessionTokenEncoder;

        // Security configuration section
        private readonly SecurityConfigurationSection m_securityConfig;
        private readonly ILocalizationService m_localizationService;
        private readonly IPolicyDecisionService m_pdpService;
        private readonly IPolicyEnforcementService m_pepService;
        private readonly IPolicyInformationService m_pipService;
        private readonly IIdentityProviderService m_identityProvider;

        /// <summary>
        /// In-Memory Session Management
        /// </summary>
        public string ServiceName => "Memory Based Session Management";

        /// <summary>
        /// DI Constructor
        /// </summary>
        public MemorySessionManagerService(IConfigurationManager configurationManager,
            ISessionTokenEncodingService sessionTokenEncodingService,
            ILocalizationService localizationService,
            IPolicyDecisionService pdpService,
            IPolicyEnforcementService pepService,
            IPolicyInformationService pipService,
            IIdentityProviderService identityProviderService)
        {
            this.m_sessionTokenEncoder = sessionTokenEncodingService;
            this.m_securityConfig = configurationManager.GetSection<SecurityConfigurationSection>();
            this.m_localizationService = localizationService;
            this.m_pdpService = pdpService;
            this.m_pepService = pepService;
            this.m_pipService = pipService;
            this.m_identityProvider = identityProviderService;
        }

        /// <inheritdoc/>
        public event EventHandler<SessionEstablishedEventArgs> Established;
        /// <inheritdoc/>
        public event EventHandler<SessionEstablishedEventArgs> Abandoned;
        /// <inheritdoc/>
        public event EventHandler<SessionEstablishedEventArgs> Extended;

        /// <inheritdoc/>
        public void Abandon(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session), ErrorMessages.ARGUMENT_NULL);
            }

            var memSession = this.m_session.Get(session.Id.HexEncode()) as MemorySession;
            if (memSession != null)
            {
                this.m_session.Remove(session.Id.HexEncode());
                this.m_session.Remove(session.RefreshToken.HexEncode());
                this.m_pdpService?.ClearCache(memSession.Principal);
                this.Abandoned?.Invoke(this, new SessionEstablishedEventArgs(memSession.Principal, memSession, true, false, null, null));
            }
        }

        /// <inheritdoc/>
        public IPrincipal Authenticate(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session), ErrorMessages.ARGUMENT_NULL);
            }

            var memSession = this.m_session.Get(session.Id.HexEncode()) as MemorySession;
            if (memSession == null)
            {
                throw new KeyNotFoundException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_TOKEN_INVALID));
            }
            else if (memSession.NotAfter < DateTimeOffset.Now)
            {
                throw new SecuritySessionException(SessionExceptionType.Expired, this.m_localizationService.GetString(ErrorMessageStrings.SESSION_EXPIRE), null);
            }

            return memSession?.Principal;
        }

        /// <inheritdoc/>
        public ISession Establish(IPrincipal principal, string remoteEp, bool isOverride, string purpose, string[] scope, string lang)
        {
            if (principal == null)
            {
                throw new ArgumentNullException(nameof(principal), ErrorMessages.ARGUMENT_NULL);
            }
            else if (!principal.Identity.IsAuthenticated)
            {
                throw new SecurityException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_NOT_AUTH_PRINCIPAL));
            }
            else if (isOverride && (String.IsNullOrEmpty(purpose) || scope == null || scope.Length == 0))
            {
                throw new InvalidOperationException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_OVERRIDE_WITH_INSUFFICIENT_DATA));
            }
            else if (scope == null || scope.Length == 0)
            {
                scope = new string[] { "*" };
            }

            if (principal is ITokenPrincipal tokenPrincipal) // We got this from the upstream so we want to just store it
            {
                // Try to hext decode 
                var decodedSessionToken = this.m_sessionTokenEncoder.ExtractSessionIdentity(tokenPrincipal.AccessToken);
                var session = new MemorySession(decodedSessionToken, DateTimeOffset.Now, tokenPrincipal.ExpiresAt, tokenPrincipal.RefreshToken != null ? Encoding.UTF8.GetBytes(tokenPrincipal.RefreshToken) : null, tokenPrincipal.Claims.ToArray(), principal);
                this.m_session.Add(session.Id.HexEncode(), session, tokenPrincipal.ExpiresAt.ToLocalTime());
                if (session.RefreshToken != null)
                {
                    this.m_session.Add(session.RefreshTokenString, session.Id.HexEncode(), tokenPrincipal.ExpiresAt);
                }

                this.Established?.Invoke(this, new SessionEstablishedEventArgs(principal, session, true, isOverride, purpose, scope));
                return session;
            }
            else if (principal is IClaimsPrincipal claimsPrincipal)
            {

                try
                {
                    // Claims principals may set override and scope which trumps the user provided ones
                    if (claimsPrincipal.HasClaim(o => o.Type == SanteDBClaimTypes.SanteDBScopeClaim))
                    {
                        scope = claimsPrincipal.FindAll(SanteDBClaimTypes.SanteDBScopeClaim).Select(o => o.Value).ToArray();
                    }
                    if (claimsPrincipal.HasClaim(o => o.Type == SanteDBClaimTypes.PurposeOfUse))
                    {
                        purpose = claimsPrincipal.FindFirst(SanteDBClaimTypes.PurposeOfUse).Value;
                    }

                    // Validate override permission for the user
                    if (isOverride)
                    {
                        this.m_pepService.Demand(PermissionPolicyIdentifiers.OverridePolicyPermission, principal);
                    }

                    // Validate scopes are valid or can be overridden
                    if (scope != null && !scope.Contains("*")) // If the principal came from the upstream we will trust its judgement on access
                    {
                        foreach (var pol in scope.Select(o => this.m_pipService.GetPolicy(o)))
                        {
                            var grant = this.m_pdpService.GetPolicyOutcome(principal, pol.Oid);
                            switch (grant)
                            {
                                case Core.Model.Security.PolicyGrantType.Deny:
                                    throw new PolicyViolationException(principal, pol, grant);
                                case Core.Model.Security.PolicyGrantType.Elevate: // validate override
                                    if (!pol.CanOverride)
                                    {
                                        throw new PolicyViolationException(principal, pol, Core.Model.Security.PolicyGrantType.Deny);
                                    }
                                    break;
                            }
                        }
                    }

                    // Establish time limit
                    var expiration = DateTimeOffset.Now.Add(this.m_securityConfig.GetSecurityPolicy<TimeSpan>(SecurityPolicyIdentification.SessionLength, new TimeSpan(1, 0, 0)));
                    // User is not really logging in, they are attempting to change their password only
                    if (scope?.Contains(PermissionPolicyIdentifiers.LoginPasswordOnly) == true &&
                        (purpose?.Equals(PurposeOfUseKeys.SecurityAdmin.ToString(), StringComparison.OrdinalIgnoreCase) == true ||
                        claimsPrincipal.FindFirst(SanteDBClaimTypes.PurposeOfUse)?.Value.Equals(PurposeOfUseKeys.SecurityAdmin.ToString(), StringComparison.OrdinalIgnoreCase) == true))
                    {
                        expiration = DateTimeOffset.Now.AddSeconds(120);
                    }
                    else if (claimsPrincipal.FindFirst(SanteDBClaimTypes.TemporarySession)?.Value == "true")
                    {
                        expiration = DateTimeOffset.Now.AddSeconds(60); //TODO: Need to set this somewhere as a configuration setting. This means they have ~2 minutes to click on a password reset.
                    }

                    // Get the effective scopes 
                    var sessionScopes = new List<string>();
                    if (scope == null || scope.Contains("*"))
                    {
                        sessionScopes.AddRange(this.m_pdpService.GetEffectivePolicySet(principal).Where(o => o.Rule == Core.Model.Security.PolicyGrantType.Grant).Select(c => c.Policy.Oid));
                    }

                    // Explicitly set scopes
                    sessionScopes.AddRange(scope.Where(s => !"*".Equals(s)));

                    var claims = new List<IClaim>(claimsPrincipal.Claims);
                    // Add claims
                    claims.AddRange(sessionScopes.Distinct().Select(o => new SanteDBClaim(SanteDBClaimTypes.SanteDBScopeClaim, o)));

                    if (!String.IsNullOrEmpty(remoteEp))
                    {
                        claims.Add(new SanteDBClaim(SanteDBClaimTypes.RemoteEndpointClaim, remoteEp));
                    }

                    // Attempt to grab the audience from the authenticated application
                    var app = claimsPrincipal.Identities.OfType<IApplicationIdentity>().FirstOrDefault();
                    if (app != null)
                    {
                        claims.Add(new SanteDBClaim(SanteDBClaimTypes.AudienceClaim, app.Name));
                    }
                    // Override?
                    if (isOverride)
                    {
                        claims.Add(new SanteDBClaim(SanteDBClaimTypes.SanteDBOverrideClaim, "true"));
                    }
                    // POU?
                    if (!String.IsNullOrEmpty(purpose))
                    {
                        claims.Add(new SanteDBClaim(SanteDBClaimTypes.PurposeOfUse, purpose));
                    }

                    // Specialized language for this user?
                    if (!String.IsNullOrEmpty(lang))
                    {
                        claims.Add(new SanteDBClaim(SanteDBClaimTypes.Language, lang));
                    }

                    var session = new MemorySession(Guid.NewGuid().ToByteArray(), DateTimeOffset.Now, expiration, Guid.NewGuid().ToByteArray(), claims.ToArray(), principal);
                    this.m_session.Add(session.Id.HexEncode(), session, expiration);

                    if (session.RefreshToken != null)
                    {
                        this.m_session.Add(session.RefreshTokenString, session.Id.HexEncode(), expiration);
                    }

                    this.Established?.Invoke(this, new SessionEstablishedEventArgs(principal, session, true, isOverride, purpose, scope));
                    return session;
                }
                catch (Exception e)
                {
                    this.Established?.Invoke(this, new SessionEstablishedEventArgs(principal, null, false, isOverride, purpose, scope));
                    throw new SecuritySessionException(SessionExceptionType.NotEstablished, this.m_localizationService.GetString(ErrorMessageStrings.SESSION_GEN_ERR), e);
                }
            }
            else
            {
                throw new SecurityException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_NOT_CLAIMS_PRINCIPAL));
            }
        }

        /// <inheritdoc/>
        public ISession Extend(byte[] refreshToken)
        {
            if (refreshToken == null)
            {
                throw new ArgumentNullException(nameof(refreshToken), this.m_localizationService.GetString(ErrorMessageStrings.ARGUMENT_NULL));
            }

            var sessionIdObject = this.m_session.Get(refreshToken.HexEncode());
            if (sessionIdObject is String sessionId)
            {
                var session = this.m_session.Get(sessionId) as MemorySession;

                if (session == null)
                {
                    throw new KeyNotFoundException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_TOKEN_INVALID));
                }
                else if (session.NotAfter < DateTimeOffset.Now)
                {
                    throw new SecuritySessionException(SessionExceptionType.Expired, this.m_localizationService.GetString(ErrorMessageStrings.SESSION_REFRESH_EXPIRE), null);
                }

                // Validate the session is not a special session 
                if (session.Claims.Any(c => c.Type == SanteDBClaimTypes.SanteDBOverrideClaim && c.Value == "true" || c.Type == SanteDBClaimTypes.PurposeOfUse && c.Value == PurposeOfUseKeys.SecurityAdmin.ToString()))
                {
                    throw new SecurityException(this.m_localizationService.GetString(ErrorMessageStrings.ELEVATED_SESSION_NO_EXTENSION));
                }

                try
                {
                    var newPrincipal = this.m_identityProvider.ReAuthenticate(session.Principal);
                    this.Abandon(session);

                    // Extend 
                    var expiration = DateTimeOffset.Now.Add(this.m_securityConfig.GetSecurityPolicy<TimeSpan>(SecurityPolicyIdentification.SessionLength, new TimeSpan(1, 0, 0)));
                    session = new MemorySession(Guid.NewGuid().ToByteArray(), DateTimeOffset.Now, expiration, Guid.NewGuid().ToByteArray(), session.Claims, newPrincipal);

                    this.Extended?.Invoke(this, new SessionEstablishedEventArgs(null, session, true, false,
                                session.Claims.FirstOrDefault(o => o.Type == SanteDBClaimTypes.PurposeOfUse)?.Value,
                                session.Claims.Where(o => o.Type == SanteDBClaimTypes.SanteDBScopeClaim).Select(o => o.Value).ToArray()));

                    this.m_session.Add(session.Id.HexEncode(), session, expiration);
                    if (session.RefreshToken != null)
                    {
                        this.m_session.Add(session.RefreshTokenString, session.Id.HexEncode(), expiration);
                    }

                    return session;
                }
                catch (Exception e)
                {
                    throw new SecuritySessionException(SessionExceptionType.Other, this.m_localizationService.GetString(ErrorMessageStrings.SESSION_GEN_ERR), e);
                }
            }
            else
            {
                throw new KeyNotFoundException(this.m_localizationService.GetString(ErrorMessageStrings.SESSION_TOKEN_INVALID));

            }
        }

        /// <inheritdoc/>
        public ISession Get(byte[] sessionId, bool allowExpired = false)
        {
            var session = this.m_session.Get(sessionId.HexEncode()) as ISession;
            if (allowExpired ^ (session != null && session.NotAfter > DateTimeOffset.Now))
            {
                return session;
            }
            return null;
        }

        /// <inheritdoc/>
        public IIdentity[] GetIdentities(ISession session)
        {
            if (session is MemorySession memorySession)
            {
                if (memorySession.Principal is IClaimsPrincipal cprincipal)
                {
                    return cprincipal.Identities;
                }
                else
                {
                    return new IIdentity[] { memorySession.Principal.Identity };
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(session), String.Format(ErrorMessages.ARGUMENT_INCOMPATIBLE_TYPE, typeof(MemorySession), session.GetType()));
            }
        }

        /// <inheritdoc/>
        public ISession[] GetUserSessions(Guid userKey)
        {
            var identity = this.m_identityProvider.GetIdentity(userKey);
            return this.m_session.OfType<MemorySession>().Where(o => o.Principal.Identity.Name == identity.Name && o.NotAfter > DateTimeOffset.Now).OfType<ISession>().ToArray();
        }

        /// <inheritdoc/>
        public ISession[] GetActiveSessions()
        {
            return this.m_session.OfType<MemorySession>().Where(o => o.NotAfter > DateTimeOffset.Now).OfType<ISession>().ToArray();
        }
    }
}
