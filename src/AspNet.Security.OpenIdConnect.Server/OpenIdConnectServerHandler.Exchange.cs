/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenIdConnect.Server
 * for more information concerning the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using Microsoft.AspNet.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AspNet.Security.OpenIdConnect.Server {
    internal partial class OpenIdConnectServerHandler : AuthenticationHandler<OpenIdConnectServerOptions> {
        private async Task InvokeTokenEndpointAsync() {
            if (!string.Equals(Request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "A malformed token request has been received: make sure to use POST."
                });

                return;
            }

            // See http://openid.net/specs/openid-connect-core-1_0.html#FormSerialization
            if (string.IsNullOrEmpty(Request.ContentType)) {
                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "A malformed token request has been received: " +
                        "the mandatory 'Content-Type' header was missing from the POST request."
                });

                return;
            }

            // May have media/type; charset=utf-8, allow partial match.
            if (!Request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "A malformed token request has been received: " +
                        "the 'Content-Type' header contained an unexcepted value. " +
                        "Make sure to use 'application/x-www-form-urlencoded'."
                });

                return;
            }

            var form = await Request.ReadFormAsync(Context.RequestAborted);

            var request = new OpenIdConnectMessage(form.ToDictionary()) {
                RequestType = OpenIdConnectRequestType.TokenRequest
            };

            // Reject token requests missing the mandatory grant_type parameter.
            if (string.IsNullOrEmpty(request.GrantType)) {
                Logger.LogError("The token request was rejected because the grant type was missing.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "The mandatory 'grant_type' parameter was missing.",
                });

                return;
            }

            // Reject grant_type=authorization_code requests missing the authorization code.
            // See https://tools.ietf.org/html/rfc6749#section-4.1.3
            else if (request.IsAuthorizationCodeGrantType() && string.IsNullOrEmpty(request.Code)) {
                Logger.LogError("The token request was rejected because the authorization code was missing.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "The mandatory 'code' parameter was missing."
                });

                return;
            }

            // Reject grant_type=refresh_token requests missing the refresh token.
            // See https://tools.ietf.org/html/rfc6749#section-6
            else if (request.IsRefreshTokenGrantType() && string.IsNullOrEmpty(request.RefreshToken)) {
                Logger.LogError("The token request was rejected because the refresh token was missing.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "The mandatory 'refresh_token' parameter was missing."
                });

                return;
            }

            // Reject grant_type=password requests missing username or password.
            // See https://tools.ietf.org/html/rfc6749#section-4.3.2
            else if (request.IsPasswordGrantType() && (string.IsNullOrEmpty(request.Username) ||
                                                       string.IsNullOrEmpty(request.Password))) {
                Logger.LogError("The token request was rejected because the resource owner credentials were missing.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidRequest,
                    ErrorDescription = "The mandatory 'username' and/or 'password' parameters " +
                                       "was/were missing from the request message."
                });

                return;
            }

            // When client_id and client_secret are both null, try to extract them from the Authorization header.
            // See http://tools.ietf.org/html/rfc6749#section-2.3.1 and
            // http://openid.net/specs/openid-connect-core-1_0.html#ClientAuthentication
            if (string.IsNullOrEmpty(request.ClientId) && string.IsNullOrEmpty(request.ClientSecret)) {
                string header = Request.Headers[HeaderNames.Authorization];
                if (!string.IsNullOrEmpty(header) && header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) {
                    try {
                        var value = header.Substring("Basic ".Length).Trim();
                        var data = Encoding.UTF8.GetString(Convert.FromBase64String(value));

                        var index = data.IndexOf(':');
                        if (index >= 0) {
                            request.ClientId = data.Substring(0, index);
                            request.ClientSecret = data.Substring(index + 1);
                        }
                    }

                    catch (FormatException) { }
                    catch (ArgumentException) { }
                }
            }

            var clientNotification = new ValidateClientAuthenticationContext(Context, Options, request);
            await Options.Provider.ValidateClientAuthentication(clientNotification);

            // Reject the request if client authentication was rejected.
            if (clientNotification.IsRejected) {
                Logger.LogError("invalid client authentication.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = clientNotification.Error ?? OpenIdConnectConstants.Errors.InvalidClient,
                    ErrorDescription = clientNotification.ErrorDescription,
                    ErrorUri = clientNotification.ErrorUri
                });

                return;
            }

            // Reject grant_type=client_credentials requests if client authentication was skipped.
            else if (clientNotification.IsSkipped && request.IsClientCredentialsGrantType()) {
                Logger.LogError("client authentication is required for client_credentials grant type.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "client authentication is required when using client_credentials"
                });

                return;
            }

            // Ensure that the client_id has been set from the ValidateClientAuthentication event.
            else if (clientNotification.IsValidated && string.IsNullOrEmpty(request.ClientId)) {
                Logger.LogError("Client authentication was validated but the client_id was not set.");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.ServerError,
                    ErrorDescription = "An internal server error occurred."
                });

                return;
            }

            var validatingContext = new ValidateTokenRequestContext(Context, Options, request);

            // Validate the token request immediately if the grant type used by
            // the client application doesn't rely on a previously-issued token/code.
            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType()) {
                await Options.Provider.ValidateTokenRequest(validatingContext);

                if (!validatingContext.IsValidated) {
                    // Note: use invalid_request as the default error if none has been explicitly provided.
                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = validatingContext.Error ?? OpenIdConnectConstants.Errors.InvalidRequest,
                        ErrorDescription = validatingContext.ErrorDescription,
                        ErrorUri = validatingContext.ErrorUri
                    });

                    return;
                }
            }

            AuthenticationTicket ticket = null;

            // See http://tools.ietf.org/html/rfc6749#section-4.1
            // and http://tools.ietf.org/html/rfc6749#section-4.1.3 (authorization code grant).
            // See http://tools.ietf.org/html/rfc6749#section-6 (refresh token grant).
            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()) {
                ticket = request.IsAuthorizationCodeGrantType() ?
                    await DeserializeAuthorizationCodeAsync(request.Code, request) :
                    await DeserializeRefreshTokenAsync(request.RefreshToken, request);

                if (ticket == null) {
                    Logger.LogError("invalid ticket");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.InvalidGrant,
                        ErrorDescription = "Invalid ticket"
                    });

                    return;
                }

                if (!ticket.Properties.ExpiresUtc.HasValue ||
                     ticket.Properties.ExpiresUtc < Options.SystemClock.UtcNow) {
                    Logger.LogError("expired ticket");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.InvalidGrant,
                        ErrorDescription = "Expired ticket"
                    });

                    return;
                }

                // If the client was fully authenticated when retrieving its refresh token,
                // the current request must be rejected if client authentication was not enforced.
                if (request.IsRefreshTokenGrantType() && !clientNotification.IsValidated && ticket.IsConfidential()) {
                    Logger.LogError("client authentication is required to use this ticket");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.InvalidGrant,
                        ErrorDescription = "Client authentication is required to use this ticket"
                    });

                    return;
                }

                // Note: presenters may be empty during a grant_type=refresh_token request if the refresh token
                // was issued to a public client but cannot be null for an authorization code grant request.
                var presenters = ticket.GetPresenters();
                if (request.IsAuthorizationCodeGrantType() && !presenters.Any()) {
                    Logger.LogError("The client the authorization code was issued to cannot be resolved.");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.ServerError,
                        ErrorDescription = "An internal server error occurred."
                    });

                    return;
                }

                // At this stage, client_id cannot be null for grant_type=authorization_code requests,
                // as it must either be set in the ValidateClientAuthentication notification
                // by the developer or manually flowed by non-confidential client applications.
                // See https://tools.ietf.org/html/rfc6749#section-4.1.3
                if (request.IsAuthorizationCodeGrantType() && string.IsNullOrEmpty(request.ClientId)) {
                    Logger.LogError("client_id was missing from the token request");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.InvalidRequest,
                        ErrorDescription = "client_id was missing from the token request"
                    });

                    return;
                }

                // Ensure the authorization code/refresh token was issued to the client application making the token request.
                // Note: when using the refresh token grant, client_id is optional but must validated if present.
                // As a consequence, this check doesn't depend on the actual status of client authentication.
                // See https://tools.ietf.org/html/rfc6749#section-6
                // and http://openid.net/specs/openid-connect-core-1_0.html#RefreshingAccessToken
                if (!string.IsNullOrEmpty(request.ClientId) && presenters.Any() &&
                    !presenters.Contains(request.ClientId, StringComparer.Ordinal)) {
                    Logger.LogError("ticket does not contain matching client_id");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.InvalidGrant,
                        ErrorDescription = "Ticket does not contain matching client_id"
                    });

                    return;
                }

                // Validate the redirect_uri flowed by the client application during this token request.
                // Note: for pure OAuth2 requests, redirect_uri is only mandatory if the authorization request
                // contained an explicit redirect_uri. OpenID Connect requests MUST include a redirect_uri
                // but the specifications allow proceeding the token request without returning an error
                // if the authorization request didn't contain an explicit redirect_uri.
                // See https://tools.ietf.org/html/rfc6749#section-4.1.3
                // and http://openid.net/specs/openid-connect-core-1_0.html#TokenRequestValidation
                string address;
                if (request.IsAuthorizationCodeGrantType() &&
                    ticket.Properties.Items.TryGetValue(OpenIdConnectConstants.Properties.RedirectUri, out address)) {
                    ticket.Properties.Items.Remove(OpenIdConnectConstants.Properties.RedirectUri);

                    if (string.IsNullOrEmpty(request.RedirectUri)) {
                        Logger.LogError("redirect_uri was missing from the grant_type=authorization_code request.");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidRequest,
                            ErrorDescription = "redirect_uri was missing from the token request"
                        });

                        return;
                    }

                    else if (!string.Equals(address, request.RedirectUri, StringComparison.Ordinal)) {
                        Logger.LogError("authorization code does not contain matching redirect_uri");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = "Authorization code does not contain matching redirect_uri"
                        });

                        return;
                    }
                }

                if (!string.IsNullOrEmpty(request.Resource)) {
                    // When an explicit resource parameter has been included in the token request
                    // but was missing from the authorization request, the request MUST be rejected.
                    var resources = ticket.GetResources();
                    if (!resources.Any()) {
                        Logger.LogError("token request cannot contain a resource");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = "Token request cannot contain a resource parameter" +
                                               "if the authorization request didn't contain one"
                        });

                        return;
                    }

                    // When an explicit resource parameter has been included in the token request,
                    // the authorization server MUST ensure that it doesn't contain resources
                    // that were not allowed during the authorization request.
                    else if (!new HashSet<string>(resources).IsSupersetOf(request.GetResources())) {
                        Logger.LogError("token request does not contain matching resource");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = "Token request doesn't contain a valid resource parameter"
                        });

                        return;
                    }

                    // Replace the resources initially granted by the resources
                    // listed by the client application in the token request.
                    ticket.SetResources(request.GetResources());
                }

                if (!string.IsNullOrEmpty(request.Scope)) {
                    // When an explicit scope parameter has been included in the token request
                    // but was missing from the authorization request, the request MUST be rejected.
                    // See http://tools.ietf.org/html/rfc6749#section-6
                    var scopes = ticket.GetScopes();
                    if (!scopes.Any()) {
                        Logger.LogError("token request cannot contain a scope");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = "Token request cannot contain a scope parameter" +
                                               "if the authorization request didn't contain one"
                        });

                        return;
                    }

                    // When an explicit scope parameter has been included in the token request,
                    // the authorization server MUST ensure that it doesn't contain scopes
                    // that were not allowed during the authorization request.
                    // See https://tools.ietf.org/html/rfc6749#section-6
                    else if (!new HashSet<string>(scopes).IsSupersetOf(request.GetScopes())) {
                        Logger.LogError("token request does not contain matching scope");

                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = "Token request doesn't contain a valid scope parameter"
                        });

                        return;
                    }

                    // Replace the scopes initially granted by the scopes
                    // listed by the client application in the token request.
                    ticket.SetScopes(request.GetScopes());
                }

                // Expose the authentication ticket extracted from the authorization
                // code or the refresh token before invoking ValidateTokenRequest.
                validatingContext.AuthenticationTicket = ticket;

                await Options.Provider.ValidateTokenRequest(validatingContext);

                if (!validatingContext.IsValidated) {
                    // Note: use invalid_request as the default error if none has been explicitly provided.
                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = validatingContext.Error ?? OpenIdConnectConstants.Errors.InvalidRequest,
                        ErrorDescription = validatingContext.ErrorDescription,
                        ErrorUri = validatingContext.ErrorUri
                    });

                    return;
                }

                if (request.IsAuthorizationCodeGrantType()) {
                    // Note: the authentication ticket is copied to avoid modifying the properties of the authorization code.
                    var context = new GrantAuthorizationCodeContext(Context, Options, request, ticket.Copy());
                    await Options.Provider.GrantAuthorizationCode(context);

                    if (!context.IsValidated) {
                        // Note: use invalid_grant as the default error if none has been explicitly provided.
                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = context.Error ?? OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = context.ErrorDescription,
                            ErrorUri = context.ErrorUri
                        });

                        return;
                    }

                    ticket = context.AuthenticationTicket;
                }

                else {
                    // Note: the authentication ticket is copied to avoid modifying the properties of the refresh token.
                    var context = new GrantRefreshTokenContext(Context, Options, request, ticket.Copy());
                    await Options.Provider.GrantRefreshToken(context);

                    if (!context.IsValidated) {
                        // Note: use invalid_grant as the default error if none has been explicitly provided.
                        await SendErrorPayloadAsync(new OpenIdConnectMessage {
                            Error = context.Error ?? OpenIdConnectConstants.Errors.InvalidGrant,
                            ErrorDescription = context.ErrorDescription,
                            ErrorUri = context.ErrorUri
                        });

                        return;
                    }

                    ticket = context.AuthenticationTicket;
                }

                // By default, when using the authorization code or the refresh token grants, the authentication ticket
                // extracted from the code/token is used as-is. If the developer didn't provide his own ticket
                // or didn't set an explicit expiration date, the ticket properties are reset to avoid aligning the
                // expiration date of the generated tokens with the lifetime of the authorization code/refresh token.
                if (ticket.Properties.IssuedUtc == validatingContext.AuthenticationTicket.Properties.IssuedUtc) {
                    ticket.Properties.IssuedUtc = null;
                }

                if (ticket.Properties.ExpiresUtc == validatingContext.AuthenticationTicket.Properties.ExpiresUtc) {
                    ticket.Properties.ExpiresUtc = null;
                }
            }

            // See http://tools.ietf.org/html/rfc6749#section-4.3
            // and http://tools.ietf.org/html/rfc6749#section-4.3.2
            else if (request.IsPasswordGrantType()) {
                var context = new GrantResourceOwnerCredentialsContext(Context, Options, request);
                await Options.Provider.GrantResourceOwnerCredentials(context);

                if (!context.IsValidated) {
                    // Note: use invalid_grant as the default error if none has been explicitly provided.
                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = context.Error ?? OpenIdConnectConstants.Errors.InvalidGrant,
                        ErrorDescription = context.ErrorDescription,
                        ErrorUri = context.ErrorUri
                    });

                    return;
                }

                ticket = context.AuthenticationTicket;
            }

            // See http://tools.ietf.org/html/rfc6749#section-4.4
            // and http://tools.ietf.org/html/rfc6749#section-4.4.2
            else if (request.IsClientCredentialsGrantType()) {
                var context = new GrantClientCredentialsContext(Context, Options, request);
                await Options.Provider.GrantClientCredentials(context);

                if (!context.IsValidated) {
                    // Note: use unauthorized_client as the default error if none has been explicitly provided.
                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = context.Error ?? OpenIdConnectConstants.Errors.UnauthorizedClient,
                        ErrorDescription = context.ErrorDescription,
                        ErrorUri = context.ErrorUri
                    });

                    return;
                }

                ticket = context.AuthenticationTicket;
            }

            // See http://tools.ietf.org/html/rfc6749#section-8.3
            else {
                var context = new GrantCustomExtensionContext(Context, Options, request);
                await Options.Provider.GrantCustomExtension(context);

                if (!context.IsValidated) {
                    // Note: use unsupported_grant_type as the default error if none has been explicitly provided.
                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = context.Error ?? OpenIdConnectConstants.Errors.UnsupportedGrantType,
                        ErrorDescription = context.ErrorDescription,
                        ErrorUri = context.ErrorUri
                    });

                    return;
                }

                ticket = context.AuthenticationTicket;
            }

            var notification = new TokenEndpointContext(Context, Options, request, ticket);
            await Options.Provider.TokenEndpoint(notification);

            if (notification.HandledResponse) {
                return;
            }

            // Flow the changes made to the ticket.
            ticket = notification.Ticket;

            // Ensure an authentication ticket has been provided:
            // a null ticket MUST result in an internal server error.
            if (ticket == null) {
                Logger.LogError("authentication ticket missing");

                await SendErrorPayloadAsync(new OpenIdConnectMessage {
                    Error = OpenIdConnectConstants.Errors.ServerError
                });

                return;
            }

            if (clientNotification.IsValidated) {
                // Store a boolean indicating whether the ticket should be marked as confidential.
                ticket.Properties.Items[OpenIdConnectConstants.Properties.Confidential] = "true";
            }

            // Note: the application is allowed to specify a different "scope": in this case,
            // don't replace the "scope" property stored in the authentication ticket.
            if (!ticket.Properties.Items.ContainsKey(OpenIdConnectConstants.Properties.Scopes) &&
                 request.HasScope(OpenIdConnectConstants.Scopes.OpenId)) {
                // Always include the "openid" scope when the developer didn't explicitly call SetScopes.
                ticket.Properties.Items[OpenIdConnectConstants.Properties.Scopes] = OpenIdConnectConstants.Scopes.OpenId;
            }

            var response = new OpenIdConnectMessage();

            // Note: by default, an access token is always returned, but the client application can use the "response_type" parameter
            // to only include specific types of tokens. When this parameter is missing, an access token is always generated.
            if (string.IsNullOrEmpty(request.ResponseType) || request.HasResponseType(OpenIdConnectConstants.ResponseTypes.Token)) {
                // Make sure to create a copy of the authentication properties
                // to avoid modifying the properties set on the original ticket.
                var properties = ticket.Properties.Copy();

                string resources;
                if (!properties.Items.TryGetValue(OpenIdConnectConstants.Properties.Resources, out resources)) {
                    Logger.LogInformation("No explicit resource has been associated with the authentication ticket: " +
                                          "the access token will thus be issued without any audience attached.");
                }

                // Note: when the "resource" parameter added to the OpenID Connect response
                // is identical to the request parameter, keeping it is not necessary.
                if (request.IsAuthorizationCodeGrantType() || (!string.IsNullOrEmpty(request.Resource) &&
                                                               !string.Equals(request.Resource, resources, StringComparison.Ordinal))) {
                    response.Resource = resources;
                }

                // Note: when the "scope" parameter added to the OpenID Connect response
                // is identical to the request parameter, keeping it is not necessary.
                string scopes;
                properties.Items.TryGetValue(OpenIdConnectConstants.Properties.Scopes, out scopes);
                if (request.IsAuthorizationCodeGrantType() || (!string.IsNullOrEmpty(request.Scope) && 
                                                               !string.Equals(request.Scope, scopes, StringComparison.Ordinal))) {
                    response.Scope = scopes;
                }

                // When sliding expiration is disabled, the access token added to the response
                // cannot live longer than the refresh token that was used in the token request.
                if (request.IsRefreshTokenGrantType() && !Options.UseSlidingExpiration &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.HasValue &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.Value <
                        (Options.SystemClock.UtcNow + Options.AccessTokenLifetime)) {
                    properties.ExpiresUtc = validatingContext.AuthenticationTicket.Properties.ExpiresUtc;
                }

                response.TokenType = OpenIdConnectConstants.TokenTypes.Bearer;
                response.AccessToken = await SerializeAccessTokenAsync(ticket.Principal, properties, request, response);

                // Ensure that an access token is issued to avoid returning an invalid response.
                // See http://openid.net/specs/oauth-v2-multiple-response-types-1_0.html#Combinations
                if (string.IsNullOrEmpty(response.AccessToken)) {
                    Logger.LogError("SerializeAccessTokenAsync returned no access token.");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.ServerError,
                        ErrorDescription = "no valid access token was issued"
                    });

                    return;
                }

                // properties.ExpiresUtc is automatically set by SerializeAccessTokenAsync but the end user
                // is free to set a null value directly in the SerializeAccessToken event.
                if (properties.ExpiresUtc.HasValue && properties.ExpiresUtc > Options.SystemClock.UtcNow) {
                    var lifetime = properties.ExpiresUtc.Value - Options.SystemClock.UtcNow;
                    var expiration = (long) (lifetime.TotalSeconds + .5);

                    response.ExpiresIn = expiration.ToString(CultureInfo.InvariantCulture);
                }
            }

            // Note: by default, an identity token is always returned when the "openid" scope has been requested,
            // but the client application can use the "response_type" parameter to only include specific types of tokens.
            // When this parameter is missing, an identity token is always generated.
            if (ticket.HasScope(OpenIdConnectConstants.Scopes.OpenId) &&
               (string.IsNullOrEmpty(request.ResponseType) || request.HasResponseType(OpenIdConnectConstants.ResponseTypes.IdToken))) {
                // Make sure to create a copy of the authentication properties
                // to avoid modifying the properties set on the original ticket.
                var properties = ticket.Properties.Copy();

                // When sliding expiration is disabled, the identity token added to the response
                // cannot live longer than the refresh token that was used in the token request.
                if (request.IsRefreshTokenGrantType() && !Options.UseSlidingExpiration &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.HasValue &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.Value <
                        (Options.SystemClock.UtcNow + Options.IdentityTokenLifetime)) {
                    properties.ExpiresUtc = validatingContext.AuthenticationTicket.Properties.ExpiresUtc;
                }

                response.IdToken = await SerializeIdentityTokenAsync(ticket.Principal, properties, request, response);

                // Ensure that an identity token is issued to avoid returning an invalid response.
                // See http://openid.net/specs/openid-connect-core-1_0.html#TokenResponse
                // and http://openid.net/specs/openid-connect-core-1_0.html#RefreshTokenResponse
                if (string.IsNullOrEmpty(response.IdToken)) {
                    Logger.LogError("SerializeIdentityTokenAsync returned no identity token.");

                    await SendErrorPayloadAsync(new OpenIdConnectMessage {
                        Error = OpenIdConnectConstants.Errors.ServerError,
                        ErrorDescription = "no valid identity token was issued"
                    });

                    return;
                }
            }

            // Note: by default, a refresh token is always returned when the "offline_access" scope has been requested,
            // but the client application can use the "response_type" parameter to only include specific types of tokens.
            // When this parameter is missing, a refresh token is always generated.
            if (ticket.HasScope(OpenIdConnectConstants.Scopes.OfflineAccess) &&
               (string.IsNullOrEmpty(request.ResponseType) || request.HasResponseType(OpenIdConnectConstants.Parameters.RefreshToken))) {
                // Make sure to create a copy of the authentication properties
                // to avoid modifying the properties set on the original ticket.
                var properties = ticket.Properties.Copy();

                // When sliding expiration is disabled, the refresh token added to the response
                // cannot live longer than the refresh token that was used in the token request.
                if (request.IsRefreshTokenGrantType() && !Options.UseSlidingExpiration &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.HasValue &&
                    validatingContext.AuthenticationTicket.Properties.ExpiresUtc.Value <
                        (Options.SystemClock.UtcNow + Options.RefreshTokenLifetime)) {
                    properties.ExpiresUtc = validatingContext.AuthenticationTicket.Properties.ExpiresUtc;
                }

                response.RefreshToken = await SerializeRefreshTokenAsync(ticket.Principal, properties, request, response);
            }

            var payload = new JObject();

            foreach (var parameter in response.Parameters) {
                payload.Add(parameter.Key, parameter.Value);
            }

            var responseNotification = new TokenEndpointResponseContext(Context, Options, ticket, request, payload);
            await Options.Provider.TokenEndpointResponse(responseNotification);

            if (responseNotification.HandledResponse) {
                return;
            }

            using (var buffer = new MemoryStream())
            using (var writer = new JsonTextWriter(new StreamWriter(buffer))) {
                payload.WriteTo(writer);
                writer.Flush();

                Response.ContentLength = buffer.Length;
                Response.ContentType = "application/json;charset=UTF-8";

                Response.Headers[HeaderNames.CacheControl] = "no-cache";
                Response.Headers[HeaderNames.Pragma] = "no-cache";
                Response.Headers[HeaderNames.Expires] = "-1";

                buffer.Seek(offset: 0, loc: SeekOrigin.Begin);
                await buffer.CopyToAsync(Response.Body, 4096, Context.RequestAborted);
            }
        }
    }
}