﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenIdConnect.Server
 * for more information concerning the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNet.Authentication;
using Microsoft.AspNet.Http.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AspNet.Security.OpenIdConnect.Extensions {
    /// <summary>
    /// Provides extension methods to make <see cref="OpenIdConnectMessage"/>
    /// easier to work with, specially in server-side scenarios.
    /// </summary>
    public static class OpenIdConnectExtensions {
        /// <summary>
        /// Extracts the resources from an <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        public static IEnumerable<string> GetResources(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return message.Resource?.Split(' ') ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Extracts the scopes from an <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        public static IEnumerable<string> GetScopes(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return message.Scope?.Split(' ') ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the token parameter from an <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        public static string GetToken(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return message.GetParameter(OpenIdConnectConstants.Parameters.Token);
        }

        /// <summary>
        /// Gets the token type hint from an <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        public static string GetTokenTypeHint(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return message.GetParameter(OpenIdConnectConstants.Parameters.TokenTypeHint);
        }

        /// <summary>
        /// Extracts the unique identifier associated with an <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        public static string GetUniqueIdentifier(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return message.GetParameter(OpenIdConnectConstants.Parameters.UniqueId);
        }

        /// <summary>
        /// Determines whether the response_type exposed by the
        /// <paramref name="message"/> contains the given <paramref name="component"/> or not.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        /// <param name="component">The component to look for in the parameter.</param>
        public static bool HasResponseType(this OpenIdConnectMessage message, string component) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return HasValue(message.ResponseType, component);
        }

        /// <summary>
        /// Determines whether the scope exposed by the <paramref name="message"/>
        /// contains the given <paramref name="component"/> or not.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        /// <param name="component">The component to look for in the parameter.</param>
        public static bool HasScope(this OpenIdConnectMessage message, string component) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return HasValue(message.Scope, component);
        }

        /// <summary>
        /// True if the "response_type" parameter corresponds to the "none" response type.
        /// See http://openid.net/specs/oauth-v2-multiple-response-types-1_0.html#none
        /// </summary>
        public static bool IsNoneFlow(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.None, StringComparison.Ordinal);
        }

        /// <summary>
        /// True if the "response_type" parameter
        /// corresponds to the authorization code flow.
        /// See http://tools.ietf.org/html/rfc6749#section-4.1.1
        /// </summary>
        public static bool IsAuthorizationCodeFlow(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.Code, StringComparison.Ordinal);
        }

        /// <summary>
        /// True if the "response_type" parameter
        /// corresponds to the implicit flow.
        /// See http://tools.ietf.org/html/rfc6749#section-4.2.1 and
        /// http://openid.net/specs/openid-connect-core-1_0.html
        /// </summary>
        public static bool IsImplicitFlow(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            // Note: while the OIDC specs do not reuse the OAuth2-inherited response_type=token,
            // it is considered as a valid response_type for the implicit flow, even for pure OIDC requests.
            return SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.IdToken) ||

                   SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.Token) ||

                   SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.IdToken,
                                                   OpenIdConnectConstants.ResponseTypes.Token);
        }

        /// <summary>
        /// True if the "response_type" parameter
        /// corresponds to the hybrid flow.
        /// See http://tools.ietf.org/html/rfc6749#section-4.2.1 and
        /// http://openid.net/specs/openid-connect-core-1_0.html
        /// </summary>
        public static bool IsHybridFlow(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.Code,
                                                   OpenIdConnectConstants.ResponseTypes.IdToken) ||

                   SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.Code,
                                                   OpenIdConnectConstants.ResponseTypes.Token) ||
                                                   
                   SetEquals(message.ResponseType, OpenIdConnectConstants.ResponseTypes.Code,
                                                   OpenIdConnectConstants.ResponseTypes.IdToken,
                                                   OpenIdConnectConstants.ResponseTypes.Token);
        }

        /// <summary>
        /// True if the "response_mode" parameter is "fragment" or if
        /// fragment is the default mode for the response_type received.
        /// See http://openid.net/specs/oauth-v2-multiple-response-types-1_0.html
        /// </summary>
        public static bool IsFragmentResponseMode(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.Equals(message.ResponseMode, OpenIdConnectConstants.ResponseModes.Fragment, StringComparison.Ordinal)) {
                return true;
            }

            // Don't guess the response_mode value
            // if an explicit value has ben provided.
            if (!string.IsNullOrEmpty(message.ResponseMode)) {
                return false;
            }

            // Both the implicit and the hybrid flows
            // use response_mode=fragment by default.
            return message.IsImplicitFlow() || message.IsHybridFlow();
        }

        /// <summary>
        /// True if the "response_mode" parameter is "query" or if
        /// query is the default mode for the response_type received.
        /// See http://openid.net/specs/oauth-v2-multiple-response-types-1_0.html
        /// </summary>
        public static bool IsQueryResponseMode(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.Equals(message.ResponseMode, OpenIdConnectConstants.ResponseModes.Query, StringComparison.Ordinal)) {
                return true;
            }

            // Don't guess the response_mode value
            // if an explicit value has ben provided.
            if (!string.IsNullOrEmpty(message.ResponseMode)) {
                return false;
            }

            // Code flow and "response_type=none" use response_mode=query by default.
            return message.IsAuthorizationCodeFlow() || message.IsNoneFlow();
        }

        /// <summary>
        /// True if the "response_mode" parameter is "form_post".
        /// See http://openid.net/specs/oauth-v2-form-post-response-mode-1_0.html
        /// </summary>
        public static bool IsFormPostResponseMode(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.ResponseMode, OpenIdConnectConstants.ResponseModes.FormPost, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the "grant_type" is "authorization_code".
        /// See also http://tools.ietf.org/html/rfc6749#section-4.1.3
        /// </summary>    
        public static bool IsAuthorizationCodeGrantType(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.GrantType, OpenIdConnectConstants.GrantTypes.AuthorizationCode, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the "grant_type" is "client_credentials".
        /// See also http://tools.ietf.org/html/rfc6749#section-4.4.2
        /// </summary>  
        public static bool IsClientCredentialsGrantType(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.GrantType, OpenIdConnectConstants.GrantTypes.ClientCredentials, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the "grant_type" is "refresh_token".
        /// See also http://tools.ietf.org/html/rfc6749#section-6
        /// </summary>    
        public static bool IsRefreshTokenGrantType(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.GrantType, OpenIdConnectConstants.GrantTypes.RefreshToken, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the "grant_type" is "password".
        /// See also http://tools.ietf.org/html/rfc6749#section-4.3.2
        /// </summary>    
        public static bool IsPasswordGrantType(this OpenIdConnectMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            return string.Equals(message.GrantType, OpenIdConnectConstants.GrantTypes.Password, StringComparison.Ordinal);
        }

        /// <summary>
        /// Sets the token for a message.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        /// <param name="token">The token to store in the OpenID Connect message.</param>
        public static void SetToken(this OpenIdConnectMessage message, string token) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrWhiteSpace(token)) {
                throw new ArgumentNullException(nameof(token));
            }

            message.SetParameter(OpenIdConnectConstants.Parameters.Token, token);
        }

        /// <summary>
        /// Sets the token type hint for a message.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        /// <param name="hint">The hint given for the token type hint.</param>
        public static void SetTokenTypeHint(this OpenIdConnectMessage message, string hint) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrWhiteSpace(hint)) {
                throw new ArgumentNullException(nameof(hint));
            }

            message.SetParameter(OpenIdConnectConstants.Parameters.TokenTypeHint, hint);
        }

        /// <summary>
        /// Adds a unique identifier to a given <see cref="OpenIdConnectMessage"/>.
        /// </summary>
        /// <param name="message">The <see cref="OpenIdConnectMessage"/> instance.</param>
        /// <param name="identifier">The unique identifier.</param>
        public static OpenIdConnectMessage SetUniqueIdentifier(this OpenIdConnectMessage message, string identifier) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            message.SetParameter(OpenIdConnectConstants.Parameters.UniqueId, identifier);
            return message;
        }

        /// <summary>
        /// Determines whether the given claim contains a destination.
        /// </summary>
        /// <param name="claim">The <see cref="Claim"/> instance.</param>
        public static bool HasDestination(this Claim claim) {
            return claim.Properties.ContainsKey(OpenIdConnectConstants.Properties.Destination);
        }

        /// <summary>
        /// Determines whether the given claim
        /// contains the required destination.
        /// </summary>
        /// <param name="claim">The <see cref="Claim"/> instance.</param>
        /// <param name="value">The required destination.</param>
        public static bool HasDestination(this Claim claim, string value) {
            string destination;
            if (!claim.Properties.TryGetValue(OpenIdConnectConstants.Properties.Destination, out destination)) {
                return false;
            }

            return HasValue(destination, value);
        }

        /// <summary>
        /// Adds a specific destination to a claim.
        /// </summary>
        /// <param name="claim">The <see cref="Claim"/> instance.</param>
        /// <param name="value">The destination.</param>
        public static Claim WithDestination(this Claim claim, string value) {
            if (claim == null) {
                throw new ArgumentNullException(nameof(claim));
            }

            if (string.IsNullOrEmpty(value)) {
                throw new ArgumentNullException(nameof(value));
            }

            string destination;
            if (claim.Properties.TryGetValue(OpenIdConnectConstants.Properties.Destination, out destination)) {
                claim.Properties[OpenIdConnectConstants.Properties.Destination] = string.Concat(destination, ' ', value);
                return claim;
            }

            claim.Properties.Add(OpenIdConnectConstants.Properties.Destination, value);
            return claim;
        }

        /// <summary>
        /// Clones an identity by filtering its claims and the claims of its actor, recursively.
        /// </summary>
        /// <param name="identity">The <see cref="ClaimsIdentity"/> instance to filter.</param>
        /// <param name="filter">
        /// The delegate filtering the claims: return <c>true</c>
        /// to accept the claim, <c>false</c> to remove it.
        /// </param>
        public static ClaimsIdentity Clone(this ClaimsIdentity identity, Func<Claim, bool> filter) {
            if (identity == null) {
                throw new ArgumentNullException(nameof(identity));
            }
            
            if (filter == null) {
                throw new ArgumentNullException(nameof(filter));
            }

            var clone = identity.Clone();

            // Note: make sure to call ToArray() to avoid modifying
            // the initial collection iterated by ClaimsIdentity.Claims.
            foreach (var claim in clone.Claims.ToArray()) {
                if (!filter(claim)) {
                    clone.RemoveClaim(claim);
                }
            }

            if (clone.Actor != null) {
                clone.Actor = clone.Actor.Clone(filter);
            }

            return clone;
        }

        /// <summary>
        /// Clones a principal by filtering its identities.
        /// </summary>
        /// <param name="principal">The <see cref="ClaimsPrincipal"/> instance to filter.</param>
        /// <param name="filter">
        /// The delegate filtering the claims: return <c>true</c>
        /// to accept the claim, <c>false</c> to remove it.
        /// </param>
        public static ClaimsPrincipal Clone(this ClaimsPrincipal principal, Func<Claim, bool> filter) {
            if (principal == null) {
                throw new ArgumentNullException(nameof(principal));
            }

            if (filter == null) {
                throw new ArgumentNullException(nameof(filter));
            }

            var clone = new ClaimsPrincipal();

            foreach (var identity in principal.Identities) {
                clone.AddIdentity(identity.Clone(filter));
            }

            return clone;
        }

        /// <summary>
        /// Adds a claim to a given identity.
        /// </summary>
        /// <param name="identity">The identity.</param>
        /// <param name="type">The type associated with the claim.</param>
        /// <param name="value">The value associated with the claim.</param>
        public static ClaimsIdentity AddClaim(this ClaimsIdentity identity, string type, string value) {
            if (identity == null) {
                throw new ArgumentNullException(nameof(identity));
            }

            identity.AddClaim(new Claim(type, value));
            return identity;
        }

        /// <summary>
        /// Adds a claim to a given identity.
        /// </summary>
        /// <param name="identity">The identity.</param>
        /// <param name="type">The type associated with the claim.</param>
        /// <param name="value">The value associated with the claim.</param>
        /// <param name="destination">The destination associated with the claim.</param>
        public static ClaimsIdentity AddClaim(this ClaimsIdentity identity, string type, string value, string destination) {
            if (identity == null) {
                throw new ArgumentNullException(nameof(identity));
            }

            identity.AddClaim(new Claim(type, value).WithDestination(destination));
            return identity;
        }

        /// <summary>
        /// Gets the claim value corresponding to the given type.
        /// </summary>
        /// <param name="identity">The identity.</param>
        /// <param name="type">The type associated with the claim.</param>
        /// <returns>The claim value.</returns>
        public static string GetClaim(this ClaimsIdentity identity, string type) {
            if (identity == null) {
                throw new ArgumentNullException(nameof(identity));
            }

            return identity.FindFirst(type)?.Value;
        }

        /// <summary>
        /// Gets the claim value corresponding to the given type.
        /// </summary>
        /// <param name="principal">The principal.</param>
        /// <param name="type">The type associated with the claim.</param>
        /// <returns>The claim value.</returns>
        public static string GetClaim(this ClaimsPrincipal principal, string type) {
            if (principal == null) {
                throw new ArgumentNullException(nameof(principal));
            }

            return principal.FindFirst(type)?.Value;
        }

        /// <summary>
        /// Copies the authentication properties in a new instance.
        /// </summary>
        /// <param name="properties">The authentication properties to copy.</param>
        /// <returns>A new instance containing the copied properties.</returns>
        public static AuthenticationProperties Copy(this AuthenticationProperties properties) {
            if (properties == null) {
                throw new ArgumentNullException(nameof(properties));
            }

            return new AuthenticationProperties(properties.Items.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        /// <summary>
        /// Copies the authentication ticket in a new instance.
        /// </summary>
        /// <param name="ticket">The authentication ticket to copy.</param>
        /// <returns>A new instance containing the copied ticket</returns>
        public static AuthenticationTicket Copy(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return new AuthenticationTicket(ticket.Principal, ticket.Properties.Copy(), ticket.AuthenticationScheme);
        }

        /// <summary>
        /// Copies the authentication properties to another instance.
        /// </summary>
        /// <param name="source">The source instance.</param>
        /// <param name="destination">The destination instance.</param>
        public static void CopyTo(this AuthenticationProperties source, AuthenticationProperties destination) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }

            // Don't copy values if the source
            // and destination instances are identical.
            if (ReferenceEquals(destination, source)) {
                return;
            }

            foreach (var property in source.Items) {
                destination.Items[property.Key] = property.Value;
            }
        }

        /// <summary>
        /// Gets a given property from the authentication ticket.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="property">The specific property to look for.</param>
        /// <returns>The value corresponding to the property, or <c>null</c> if the property cannot be found.</returns>
        public static string GetProperty(this AuthenticationTicket ticket, string property) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            string value;
            if (!ticket.Properties.Items.TryGetValue(property, out value)) {
                return null;
            }

            return value;
        }

        /// <summary>
        /// Gets the audiences list stored in the authentication ticket.
        /// Note: this method automatically excludes duplicate audiences.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The audiences list or <c>Enumerable.Empty</c> is the property cannot be found.</returns>
        public static IEnumerable<string> GetAudiences(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Audiences)
                        ?.Split(' ')
                        ?.Distinct(StringComparer.Ordinal)
                   ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the presenters list stored in the authentication ticket.
        /// Note: this method automatically excludes duplicate presenters.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The presenters list or <c>Enumerable.Empty</c> is the property cannot be found.</returns>
        public static IEnumerable<string> GetPresenters(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Presenters)
                        ?.Split(' ')
                        ?.Distinct(StringComparer.Ordinal)
                   ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the nonce stored in the authentication ticket.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The nonce or <c>null</c> is the property cannot be found.</returns>
        public static string GetNonce(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Nonce);
        }

        /// <summary>
        /// Gets the resources list stored in the authentication ticket.
        /// Note: this method automatically excludes duplicate resources.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The resources list or <c>Enumerable.Empty</c> is the property cannot be found.</returns>
        public static IEnumerable<string> GetResources(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Resources)
                        ?.Split(' ')
                        ?.Distinct(StringComparer.Ordinal)
                   ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the scopes list stored in the authentication ticket.
        /// Note: this method automatically excludes duplicate scopes.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The scopes list or <c>Enumerable.Empty</c> is the property cannot be found.</returns>
        public static IEnumerable<string> GetScopes(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Scopes)
                        ?.Split(' ')
                        ?.Distinct(StringComparer.Ordinal)
                   ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the usage of the token stored in the authentication ticket.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns>The usage of the token or <c>null</c> is the property cannot be found.</returns>
        public static string GetUsage(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            return ticket.GetProperty(OpenIdConnectConstants.Properties.Usage);
        }

        /// <summary>
        /// Determines whether the authentication ticket contains the given audience.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="audience">The audience.</param>
        /// <returns><c>true</c> if the ticket contains the given audience.</returns>
        public static bool HasAudience(this AuthenticationTicket ticket, string audience) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var audiences = ticket.GetProperty(OpenIdConnectConstants.Properties.Audiences)?.Split(' ');
            if (audiences == null) {
                return false;
            }

            return audiences.Contains(audience, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether the authentication ticket contains the given presenter.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="presenter">The presenter.</param>
        /// <returns><c>true</c> if the ticket contains the given presenter.</returns>
        public static bool HasPresenter(this AuthenticationTicket ticket, string presenter) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var presenters = ticket.GetProperty(OpenIdConnectConstants.Properties.Presenters)?.Split(' ');
            if (presenters == null) {
                return false;
            }

            return presenters.Contains(presenter, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether the authentication ticket contains the given resource.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="resource">The resource.</param>
        /// <returns><c>true</c> if the ticket contains the given resource.</returns>
        public static bool HasResource(this AuthenticationTicket ticket, string resource) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var resources = ticket.GetProperty(OpenIdConnectConstants.Properties.Resources)?.Split(' ');
            if (resources == null) {
                return false;
            }

            return resources.Contains(resource, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether the authentication ticket contains the given scope.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="scope">The scope.</param>
        /// <returns><c>true</c> if the ticket contains the given scope.</returns>
        public static bool HasScope(this AuthenticationTicket ticket, string scope) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var scopes = ticket.GetProperty(OpenIdConnectConstants.Properties.Scopes)?.Split(' ');
            if (scopes == null) {
                return false;
            }

            return scopes.Contains(scope, StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets a boolean value indicating whether
        /// the ticket is marked as confidential.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns><c>true</c> if the ticket is confidential, or <c>false</c> if it's not.</returns>
        public static bool IsConfidential(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var value = ticket.GetProperty(OpenIdConnectConstants.Properties.Confidential);
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a boolean value indicating whether the
        /// authentication ticket corresponds to an access token.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns><c>true</c> if the ticket corresponds to an authorization code.</returns>
        public static bool IsAuthorizationCode(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var value = ticket.GetProperty(OpenIdConnectConstants.Properties.Usage);
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            return string.Equals(value, OpenIdConnectConstants.Usages.Code, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a boolean value indicating whether the
        /// authentication ticket corresponds to an access token.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns><c>true</c> if the ticket corresponds to an access token.</returns>
        public static bool IsAccessToken(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var value = ticket.GetProperty(OpenIdConnectConstants.Properties.Usage);
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            return string.Equals(value, OpenIdConnectConstants.Usages.AccessToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a boolean value indicating whether the
        /// authentication ticket corresponds to an identity token.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns><c>true</c> if the ticket corresponds to an identity token.</returns>
        public static bool IsIdentityToken(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var value = ticket.GetProperty(OpenIdConnectConstants.Properties.Usage);
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            return string.Equals(value, OpenIdConnectConstants.Usages.IdToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a boolean value indicating whether the
        /// authentication ticket corresponds to a refresh token.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <returns><c>true</c> if the ticket corresponds to a refresh token.</returns>
        public static bool IsRefreshToken(this AuthenticationTicket ticket) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            var value = ticket.GetProperty(OpenIdConnectConstants.Properties.Usage);
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            return string.Equals(value, OpenIdConnectConstants.Usages.RefreshToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the audiences list in the authentication ticket.
        /// Note: this method automatically excludes duplicate audiences.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="audiences">The audiences to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetAudiences(this AuthenticationTicket ticket, IEnumerable<string> audiences) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            if (audiences == null) {
                throw new ArgumentNullException(nameof(audiences));
            }

            if (audiences.Any(audience => audience.Contains(" "))) {
                throw new ArgumentException("The audiences cannot contain spaces.", nameof(audiences));
            }

            ticket.Properties.Items[OpenIdConnectConstants.Properties.Audiences] =
                string.Join(" ", audiences.Distinct(StringComparer.Ordinal));

            return ticket;
        }

        /// <summary>
        /// Sets the audiences list in the authentication ticket.
        /// Note: this method automatically excludes duplicate audiences.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="audiences">The audiences to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetAudiences(this AuthenticationTicket ticket, params string[] audiences) {
            // Note: guarding the audiences parameter against null values
            // is not necessary as AsEnumerable() doesn't throw on null values.
            return ticket.SetAudiences(audiences.AsEnumerable());
        }

        /// <summary>
        /// Sets the presenters list in the authentication ticket.
        /// Note: this method automatically excludes duplicate presenters.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="presenters">The presenters to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetPresenters(this AuthenticationTicket ticket, IEnumerable<string> presenters) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            if (presenters == null) {
                throw new ArgumentNullException(nameof(presenters));
            }

            if (presenters.Any(presenter => presenter.Contains(" "))) {
                throw new ArgumentException("The presenters cannot contain spaces.", nameof(presenters));
            }

            ticket.Properties.Items[OpenIdConnectConstants.Properties.Presenters] =
                string.Join(" ", presenters.Distinct(StringComparer.Ordinal));

            return ticket;
        }

        /// <summary>
        /// Sets the presenters list in the authentication ticket.
        /// Note: this method automatically excludes duplicate presenters.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="presenters">The presenters to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetPresenters(this AuthenticationTicket ticket, params string[] presenters) {
            // Note: guarding the presenters parameter against null values
            // is not necessary as AsEnumerable() doesn't throw on null values.
            return ticket.SetPresenters(presenters.AsEnumerable());
        }

        /// <summary>
        /// Sets the resources list in the authentication ticket.
        /// Note: this method automatically excludes duplicate resources.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="resources">The resources to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetResources(this AuthenticationTicket ticket, IEnumerable<string> resources) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            if (resources == null) {
                throw new ArgumentNullException(nameof(resources));
            }

            if (resources.Any(resource => resource.Contains(" "))) {
                throw new ArgumentException("The resources cannot contain spaces.", nameof(resources));
            }

            ticket.Properties.Items[OpenIdConnectConstants.Properties.Resources] =
                string.Join(" ", resources.Distinct(StringComparer.Ordinal));

            return ticket;
        }

        /// <summary>
        /// Sets the resources list in the authentication ticket.
        /// Note: this method automatically excludes duplicate resources.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="resources">The resources to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetResources(this AuthenticationTicket ticket, params string[] resources) {
            // Note: guarding the resources parameter against null values
            // is not necessary as AsEnumerable() doesn't throw on null values.
            return ticket.SetResources(resources.AsEnumerable());
        }

        /// <summary>
        /// Sets the scopes list in the authentication ticket.
        /// Note: this method automatically excludes duplicate scopes.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="scopes">The scopes to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetScopes(this AuthenticationTicket ticket, IEnumerable<string> scopes) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            if (scopes == null) {
                throw new ArgumentNullException(nameof(scopes));
            }

            if (scopes.Any(scope => scope.Contains(" "))) {
                throw new ArgumentException("The scopes cannot contain spaces.", nameof(scopes));
            }

            ticket.Properties.Items[OpenIdConnectConstants.Properties.Scopes] =
                string.Join(" ", scopes.Distinct(StringComparer.Ordinal));

            return ticket;
        }

        /// <summary>
        /// Sets the scopes list in the authentication ticket.
        /// Note: this method automatically excludes duplicate scopes.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="scopes">The scopes to store.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetScopes(this AuthenticationTicket ticket, params string[] scopes) {
            // Note: guarding the scopes parameter against null values
            // is not necessary as AsEnumerable() doesn't throw on null values.
            return ticket.SetScopes(scopes.AsEnumerable());
        }

        /// <summary>
        /// Sets the usage of the token in the authentication ticket.
        /// </summary>
        /// <param name="ticket">The authentication ticket.</param>
        /// <param name="usage">The usage of the token.</param>
        /// <returns>The authentication ticket.</returns>
        public static AuthenticationTicket SetUsage(this AuthenticationTicket ticket, string usage) {
            if (ticket == null) {
                throw new ArgumentNullException(nameof(ticket));
            }

            ticket.Properties.Items[OpenIdConnectConstants.Properties.Usage] = usage;

            return ticket;
        }

        private static bool HasValue(string source, string value) {
            if (string.IsNullOrEmpty(source)) {
                return false;
            }

            return (from component in source.Split(' ')
                    where string.Equals(component, value, StringComparison.Ordinal)
                    select component).Any();
        }

        private static bool SetEquals(string source, params string[] components) {
            if (string.IsNullOrEmpty(source)) {
                return false;
            }

            if (components == null || !components.Any()) {
                return false;
            }

            var set = new HashSet<string>(
                collection: source.Split(' '),
                comparer: StringComparer.Ordinal);

            return set.SetEquals(components);
        }
    }
}
