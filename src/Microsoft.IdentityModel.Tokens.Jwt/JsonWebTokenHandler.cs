﻿//------------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using Microsoft.IdentityModel.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using TokenLogMessages = Microsoft.IdentityModel.Tokens.LogMessages;

namespace Microsoft.IdentityModel.Tokens.Jwt
{
    /// <summary>
    /// A <see cref="SecurityTokenHandler"/> designed for creating and validating Json Web Tokens. See: http://tools.ietf.org/html/rfc7519 and http://www.rfc-editor.org/info/rfc7515
    /// </summary>
    public class JsonWebTokenHandler : SecurityTokenHandler
    {
        private JwtTokenUtilities _jwtTokenUtilities = new JwtTokenUtilities();

        /// <summary>
        /// Gets the type of the <see cref="JsonWebToken"/>.
        /// </summary>
        /// <return>The type of <see cref="JsonWebToken"/></return>
        public override Type TokenType
        {
            get { return typeof(JsonWebToken); }
        }

        /// <summary>
        /// Determines if the string is a well formed Json Web Token (JWT).
        /// <para>see: http://tools.ietf.org/html/rfc7519 </para>
        /// </summary>
        /// <param name="token">String that should represent a valid JWT.</param>
        /// <remarks>Uses <see cref="Regex.IsMatch(string, string)"/> matching one of:
        /// <para>JWS: @"^[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]*$"</para>
        /// <para>JWE: (dir): @"^[A-Za-z0-9-_]+\.\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]*$"</para>
        /// <para>JWE: (wrappedkey): @"^[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]$"</para>
        /// </remarks>
        /// <returns>
        /// <para>'false' if the token is null or whitespace.</para>
        /// <para>'false' if token.Length * 2 >  <see cref="SecurityTokenHandler.MaximumTokenSizeInBytes"/>.</para>
        /// <para>'true' if the token is in JSON compact serialization format.</para>
        /// </returns>
        public override bool CanReadToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            if (token.Length * 2 > MaximumTokenSizeInBytes)
            {
                LogHelper.LogInformation(TokenLogMessages.IDX10209, token.Length, MaximumTokenSizeInBytes);
                return false;
            }

            // Set the maximum number of segments to MaxJwtSegmentCount + 1. This controls the number of splits and allows detecting the number of segments is too large.
            // For example: "a.b.c.d.e.f.g.h" => [a], [b], [c], [d], [e], [f.g.h]. 6 segments.
            // If just MaxJwtSegmentCount was used, then [a], [b], [c], [d], [e.f.g.h] would be returned. 5 segments.
            string[] tokenParts = token.Split(new char[] { '.' }, JwtConstants.MaxJwtSegmentCount + 1);
            if (tokenParts.Length == JwtConstants.JwsSegmentCount)
            {
                return JwtTokenUtilities.RegexJws.IsMatch(token);
            }
            else if (tokenParts.Length == JwtConstants.JweSegmentCount)
            {
                return JwtTokenUtilities.RegexJwe.IsMatch(token);
            }

            LogHelper.LogInformation(LogMessages.IDX14107);
            return false;
        }

        /// <summary>
        /// Creates a JWS asynchronously.
        /// </summary>
        public async Task<string> CreateJWSAsync(JObject payload, SigningCredentials signingCredentials)
        {
            return await CreateJsonWebTokenAsync(payload, signingCredentials, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a JWS.
        /// </summary>
        public string CreateJWS(JObject payload, SigningCredentials signingCredentials)
        {
            return CreateJsonWebToken(payload, signingCredentials, null);
        }

        /// <summary>
        /// Creates a JsonWebToken (JWE or JWS) asynchronously.
        /// </summary>
        public async Task<string> CreateJsonWebTokenAsync(JObject payload, SigningCredentials signingCredentials, EncryptingCredentials encryptingCredentials)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            string rawHeader;
            if (!JsonWebTokenManager.KeyToHeaderCache.TryGetValue(JsonWebTokenManager.GetHeaderCacheKey(signingCredentials), out rawHeader))
            {
                var header = signingCredentials == null ? new JObject() : new JObject
                {
                    { JwtHeaderParameterNames.Alg, signingCredentials.Algorithm },
                    { JwtHeaderParameterNames.Kid, signingCredentials.Key.KeyId },
                    { JwtHeaderParameterNames.Typ, JwtConstants.HeaderType }
                };

                rawHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header.ToString(Newtonsoft.Json.Formatting.None)));
                JsonWebTokenManager.KeyToHeaderCache.TryAdd(JsonWebTokenManager.GetHeaderCacheKey(signingCredentials), rawHeader);
            }
            
            string rawPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None)));
            string rawSignature = signingCredentials == null ? string.Empty : await _jwtTokenUtilities.CreateEncodedSignatureAsync(string.Concat(rawHeader, ".", rawPayload), signingCredentials).ConfigureAwait(false);

            var rawData = rawHeader + "." + rawPayload + "." + rawSignature;

            if (encryptingCredentials != null)
                return EncryptToken(rawData, encryptingCredentials);
            else
                return rawData;
        }

        /// <summary>
        /// Creates a JsonWebToken (JWE or JWS).
        /// </summary>
        public string CreateJsonWebToken(JObject payload, SigningCredentials signingCredentials, EncryptingCredentials encryptingCredentials)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            string rawHeader;
            if (!JsonWebTokenManager.KeyToHeaderCache.TryGetValue(JsonWebTokenManager.GetHeaderCacheKey(signingCredentials), out rawHeader))
            {
                var header = signingCredentials == null ? new JObject() : new JObject
                {
                    { JwtHeaderParameterNames.Alg, signingCredentials.Algorithm },
                    { JwtHeaderParameterNames.Kid, signingCredentials.Key.KeyId },
                    { JwtHeaderParameterNames.Typ, JwtConstants.HeaderType }
                };

                rawHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header.ToString(Newtonsoft.Json.Formatting.None)));
                JsonWebTokenManager.KeyToHeaderCache.TryAdd(JsonWebTokenManager.GetHeaderCacheKey(signingCredentials), rawHeader);
            }

            string rawPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None)));
            string rawSignature = signingCredentials == null ? string.Empty : JwtTokenUtilities.CreateEncodedSignature(string.Concat(rawHeader, ".", rawPayload), signingCredentials);

            var rawData = rawHeader + "." + rawPayload + "." + rawSignature;

            if (encryptingCredentials != null)
                return EncryptToken(rawData, encryptingCredentials);
            else
                return rawData;
        }

        /// <summary>
        /// Creates a JsonWebToken (JWE or JWS) asynchronously. Raw header value is passed in as one of the parameters for testing purposes.
        /// </summary>
        public async Task<string> CreateJsonWebTokenAsync(JObject payload, SigningCredentials signingCredentials, EncryptingCredentials encryptingCredentials, string rawHeader)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (rawHeader == null)
                throw new ArgumentNullException(nameof(rawHeader));

            string rawPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None)));
            string rawSignature = signingCredentials == null ? string.Empty : await _jwtTokenUtilities.CreateEncodedSignatureAsync(string.Concat(rawHeader, ".", rawPayload), signingCredentials).ConfigureAwait(false);

            var rawData = rawHeader + "." + rawPayload + "." + rawSignature;

            if (encryptingCredentials != null)
                return EncryptToken(rawData, encryptingCredentials);
            else
                return rawData;
        }

        /// <summary>
        /// Creates a JsonWebToken (JWE or JWS). Raw header value is passed in as one of the parameters for testing purposes.
        /// </summary>
        public string CreateJsonWebToken(JObject payload, SigningCredentials signingCredentials, EncryptingCredentials encryptingCredentials, string rawHeader)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (rawHeader == null)
                throw new ArgumentNullException(nameof(rawHeader));

            string rawPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None)));
            string rawSignature = signingCredentials == null ? string.Empty : JwtTokenUtilities.CreateEncodedSignature(string.Concat(rawHeader, ".", rawPayload), signingCredentials);

            var rawData = rawHeader + "." + rawPayload + "." + rawSignature;

            if (encryptingCredentials != null)
                return EncryptToken(rawData, encryptingCredentials);
            else
                return rawData;
        }

        private string EncryptToken(string innerJwt, EncryptingCredentials encryptingCredentials)
        {
            var cryptoProviderFactory = encryptingCredentials.CryptoProviderFactory ?? encryptingCredentials.Key.CryptoProviderFactory;

            if (cryptoProviderFactory == null)
                throw LogHelper.LogExceptionMessage(new ArgumentException(LogMessages.IDX14104));

            // if direct algorithm, look for support
            if (JwtConstants.DirectKeyUseAlg.Equals(encryptingCredentials.Alg, StringComparison.Ordinal))
            {
                if (!cryptoProviderFactory.IsSupportedAlgorithm(encryptingCredentials.Enc, encryptingCredentials.Key))
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogHelper.FormatInvariant(TokenLogMessages.IDX10615, encryptingCredentials.Enc, encryptingCredentials.Key)));

                var header = new JObject();

                if (!string.IsNullOrEmpty(encryptingCredentials.Alg))
                    header.Add(JwtHeaderParameterNames.Alg, encryptingCredentials.Alg);

                if (!string.IsNullOrEmpty(encryptingCredentials.Enc))
                    header.Add(JwtHeaderParameterNames.Enc, encryptingCredentials.Enc);

                if (!string.IsNullOrEmpty(encryptingCredentials.Key.KeyId))
                    header.Add(JwtHeaderParameterNames.Kid, encryptingCredentials.Key.KeyId);

                header.Add(JwtHeaderParameterNames.Typ, JwtConstants.HeaderType);

                var encryptionProvider = cryptoProviderFactory.CreateAuthenticatedEncryptionProvider(encryptingCredentials.Key, encryptingCredentials.Enc);
                if (encryptionProvider == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogMessages.IDX14103));

                try
                {
                    var rawHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header.ToString(Newtonsoft.Json.Formatting.None)));
                    var encryptionResult = encryptionProvider.Encrypt(Encoding.UTF8.GetBytes(innerJwt), Encoding.ASCII.GetBytes(rawHeader));
                    return string.Join(".", rawHeader, string.Empty, Base64UrlEncoder.Encode(encryptionResult.IV), Base64UrlEncoder.Encode(encryptionResult.Ciphertext), Base64UrlEncoder.Encode(encryptionResult.AuthenticationTag));

                }
                catch (Exception ex)
                {
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogHelper.FormatInvariant(TokenLogMessages.IDX10616, encryptingCredentials.Enc, encryptingCredentials.Key), ex));
                }
            }
            else
            {
                if (!cryptoProviderFactory.IsSupportedAlgorithm(encryptingCredentials.Alg, encryptingCredentials.Key))
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogHelper.FormatInvariant(TokenLogMessages.IDX10615, encryptingCredentials.Alg, encryptingCredentials.Key)));

                SymmetricSecurityKey symmetricKey = null;

                // only 128, 384 and 512 AesCbcHmac for CEK algorithm
                if (SecurityAlgorithms.Aes128CbcHmacSha256.Equals(encryptingCredentials.Enc, StringComparison.Ordinal))
                    symmetricKey = new SymmetricSecurityKey(JwtTokenUtilities.GenerateKeyBytes(256));
                else if (SecurityAlgorithms.Aes192CbcHmacSha384.Equals(encryptingCredentials.Enc, StringComparison.Ordinal))
                    symmetricKey = new SymmetricSecurityKey(JwtTokenUtilities.GenerateKeyBytes(384));
                else if (SecurityAlgorithms.Aes256CbcHmacSha512.Equals(encryptingCredentials.Enc, StringComparison.Ordinal))
                    symmetricKey = new SymmetricSecurityKey(JwtTokenUtilities.GenerateKeyBytes(512));
                else
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogHelper.FormatInvariant(TokenLogMessages.IDX10617, SecurityAlgorithms.Aes128CbcHmacSha256, SecurityAlgorithms.Aes192CbcHmacSha384, SecurityAlgorithms.Aes256CbcHmacSha512, encryptingCredentials.Enc)));

                var kwProvider = cryptoProviderFactory.CreateKeyWrapProvider(encryptingCredentials.Key, encryptingCredentials.Alg);
                var wrappedKey = kwProvider.WrapKey(symmetricKey.Key);
                var encryptionProvider = cryptoProviderFactory.CreateAuthenticatedEncryptionProvider(symmetricKey, encryptingCredentials.Enc);
                if (encryptionProvider == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogMessages.IDX14103));

                try
                {
                    var header = new JObject();

                    if (!string.IsNullOrEmpty(encryptingCredentials.Alg))
                        header.Add(JwtHeaderParameterNames.Alg, encryptingCredentials.Alg);

                    if (!string.IsNullOrEmpty(encryptingCredentials.Enc))
                        header.Add(JwtHeaderParameterNames.Enc, encryptingCredentials.Enc);

                    if (!string.IsNullOrEmpty(encryptingCredentials.Key.KeyId))
                        header.Add(JwtHeaderParameterNames.Kid, encryptingCredentials.Key.KeyId);

                    header.Add(JwtHeaderParameterNames.Typ, JwtConstants.HeaderType);

                    var rawHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header.ToString(Newtonsoft.Json.Formatting.None)));
                    var encryptionResult = encryptionProvider.Encrypt(Encoding.UTF8.GetBytes(innerJwt), Encoding.ASCII.GetBytes(rawHeader));
                    return string.Join(".", rawHeader, Base64UrlEncoder.Encode(wrappedKey), Base64UrlEncoder.Encode(encryptionResult.IV), Base64UrlEncoder.Encode(encryptionResult.Ciphertext), Base64UrlEncoder.Encode(encryptionResult.AuthenticationTag));
                }
                catch (Exception ex)
                {
                    throw LogHelper.LogExceptionMessage(new SecurityTokenEncryptionFailedException(LogHelper.FormatInvariant(TokenLogMessages.IDX10616, encryptingCredentials.Enc, encryptingCredentials.Key), ex));
                }
            }
        }

        private IEnumerable<SecurityKey> GetAllSigningKeys(string token, TokenValidationParameters validationParameters)
        {
            LogHelper.LogInformation(TokenLogMessages.IDX10243);
            if (validationParameters.IssuerSigningKey != null)
                yield return validationParameters.IssuerSigningKey;

            if (validationParameters.IssuerSigningKeys != null)
                foreach (SecurityKey key in validationParameters.IssuerSigningKeys)
                    yield return key;
        }

        /// <summary>
        /// Returns a <see cref="SecurityKey"/> to use when validating the signature of a token.
        /// </summary>
        /// <param name="token">The <see cref="string"/> representation of the token that is being validated.</param>
        /// <param name="jwtToken">The <see cref="JsonWebToken"/> that is being validated.</param>
        /// <param name="validationParameters">A <see cref="TokenValidationParameters"/>  required for validation.</param>
        /// <returns>Returns a <see cref="SecurityKey"/> to use for signature validation.</returns>
        /// <remarks>If key fails to resolve, then null is returned</remarks>
        protected virtual SecurityKey ResolveIssuerSigningKey(string token, JsonWebToken jwtToken, TokenValidationParameters validationParameters)
        {
            if (validationParameters == null)
                throw LogHelper.LogArgumentNullException(nameof(validationParameters));

            if (jwtToken == null)
                throw LogHelper.LogArgumentNullException(nameof(jwtToken));

            if (!string.IsNullOrEmpty(jwtToken.Kid))
            {
                string kid = jwtToken.Kid;
                if (validationParameters.IssuerSigningKey != null && string.Equals(validationParameters.IssuerSigningKey.KeyId, kid, StringComparison.Ordinal))
                    return validationParameters.IssuerSigningKey;

                if (validationParameters.IssuerSigningKeys != null)
                {
                    foreach (SecurityKey signingKey in validationParameters.IssuerSigningKeys)
                    {
                        if (signingKey != null && string.Equals(signingKey.KeyId, kid, StringComparison.Ordinal))
                        {
                            return signingKey;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(jwtToken.X5t))
            {
                string x5t = jwtToken.X5t;
                if (validationParameters.IssuerSigningKey != null)
                {
                    if (string.Equals(validationParameters.IssuerSigningKey.KeyId, x5t, StringComparison.Ordinal))
                        return validationParameters.IssuerSigningKey;

                    X509SecurityKey x509Key = validationParameters.IssuerSigningKey as X509SecurityKey;
                    if (x509Key != null && string.Equals(x509Key.X5t, x5t, StringComparison.Ordinal))
                        return validationParameters.IssuerSigningKey;
                }

                if (validationParameters.IssuerSigningKeys != null)
                {
                    foreach (SecurityKey signingKey in validationParameters.IssuerSigningKeys)
                    {
                        if (signingKey != null && string.Equals(signingKey.KeyId, x5t, StringComparison.Ordinal))
                        {
                            return signingKey;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Converts a string into an instance of <see cref="JsonWebToken"/>.
        /// </summary>
        /// <param name="token">A 'JSON Web Token' (JWT) in JWS or JWE Compact Serialization Format.</param>
        /// <returns>A <see cref="JsonWebToken"/></returns>
        /// <exception cref="ArgumentNullException">'token' is null or empty.</exception>
        /// <exception cref="ArgumentException">'token.Length' > <see cref="SecurityTokenHandler.MaximumTokenSizeInBytes"/>.</exception>
        /// <remarks><para>If the 'token' is in JWE Compact Serialization format, only the protected header will be deserialized.</para>
        /// This method is unable to decrypt the payload. Use <see cref="ValidateJWSAsync(string, TokenValidationParameters)"/>to obtain the payload.</remarks>
        public JsonWebToken ReadJsonWebToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                throw LogHelper.LogArgumentNullException(nameof(token));

            if (token.Length > MaximumTokenSizeInBytes)
                throw LogHelper.LogExceptionMessage(new ArgumentException(LogHelper.FormatInvariant(TokenLogMessages.IDX10209, token.Length, MaximumTokenSizeInBytes)));

            return new JsonWebToken(token);
        }

        /// <summary>
        /// Converts a string into an instance of <see cref="JsonWebToken"/>.
        /// </summary>
        /// <param name="token">A 'JSON Web Token' (JWT) in JWS or JWE Compact Serialization Format.</param>
        /// <returns>A <see cref="JsonWebToken"/></returns>
        /// <exception cref="ArgumentNullException">'token' is null or empty.</exception>
        /// <exception cref="ArgumentException">'token.Length * 2' > <see cref="SecurityTokenHandler.MaximumTokenSizeInBytes"/>.</exception>
        /// <remarks><para>If the 'token' is in JWE Compact Serialization format, only the protected header will be deserialized.</para>
        /// This method is unable to decrypt the payload. Use <see cref="ValidateJWSAsync(string, TokenValidationParameters)"/>to obtain the payload.</remarks>
        public override SecurityToken ReadToken(string token)
        {
            return ReadJsonWebToken(token);
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        public override SecurityToken ReadToken(XmlReader reader, TokenValidationParameters validationParameters)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Validates a JWS asynchronously.
        /// </summary>
        public async Task<TokenValidationResult> ValidateJWSAsync(string token, TokenValidationParameters validationParameters)
        {
            if (string.IsNullOrEmpty(token))
                throw new ArgumentNullException(nameof(token));

            if (validationParameters == null)
                throw new ArgumentNullException(nameof(validationParameters));

            if (token.Length > MaximumTokenSizeInBytes)
                throw LogHelper.LogExceptionMessage(new ArgumentException(LogHelper.FormatInvariant(TokenLogMessages.IDX10209, token.Length, MaximumTokenSizeInBytes)));

            var validatedJsonWebToken = await ValidateSignatureAsync(token, validationParameters).ConfigureAwait(false);

            return await ValidateTokenPayloadAsync(validatedJsonWebToken as JsonWebToken, validationParameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates the JWT signature asynchronously.
        /// </summary>
        public async Task<JsonWebToken> ValidateSignatureAsync(string token, TokenValidationParameters validationParameters)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw LogHelper.LogArgumentNullException(nameof(token));

            if (validationParameters == null)
                throw LogHelper.LogArgumentNullException(nameof(validationParameters));

            if (validationParameters.SignatureValidator != null)
            {
                var validatedToken = validationParameters.SignatureValidator(token, validationParameters);
                if (validatedToken == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10505, token)));

                var validatedJsonWebToken = validatedToken as JsonWebToken;
                if (validatedJsonWebToken == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10506, typeof(JsonWebToken), validatedJsonWebToken.GetType(), token)));

                return validatedJsonWebToken;
            }

            JsonWebToken jwtToken = null;

            if (validationParameters.TokenReader != null)
            {
                var securityToken = validationParameters.TokenReader(token, validationParameters);
                if (securityToken == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10510, token)));

                jwtToken = securityToken as JsonWebToken;
                if (jwtToken == null)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10509, typeof(JsonWebToken), securityToken.GetType(), token)));
            }
            else
            {
                jwtToken = new JsonWebToken(token);
            }

            byte[] encodedBytes = Encoding.UTF8.GetBytes(jwtToken.RawHeader + "." + jwtToken.RawPayload);
            if (string.IsNullOrEmpty(jwtToken.RawSignature))
            {
                if (validationParameters.RequireSignedTokens)
                    throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10504, token)));
                else
                    return jwtToken;
            }

            bool keyMatched = false;
            IEnumerable<SecurityKey> keys = null;
            if (validationParameters.IssuerSigningKeyResolver != null)
            {
                keys = validationParameters.IssuerSigningKeyResolver(token, jwtToken, jwtToken.Kid, validationParameters);
            }
            else
            {
                var key = ResolveIssuerSigningKey(token, jwtToken, validationParameters);
                if (key != null)
                {
                    keyMatched = true;
                    keys = new List<SecurityKey> { key };
                }
            }

            if (keys == null)
            {
                // control gets here if:
                // 1. User specified delegate: IssuerSigningKeyResolver returned null
                // 2. ResolveIssuerSigningKey returned null
                // Try all the keys. This is the degenerate case, not concerned about perf.
                keys = GetAllSigningKeys(token, validationParameters);
            }

            // keep track of exceptions thrown, keys that were tried
            var exceptionStrings = new StringBuilder();
            var keysAttempted = new StringBuilder();
            bool canMatchKey = !string.IsNullOrEmpty(jwtToken.Kid);
            byte[] signatureBytes;

            try
            {
                signatureBytes = Base64UrlEncoder.DecodeBytes(jwtToken.RawSignature);
            }
            catch (FormatException e)
            {
                throw new SecurityTokenInvalidSignatureException(TokenLogMessages.IDX10508, e);
            }

            foreach (var key in keys)
            {
                try
                {
                    if (await ValidateSignatureAsync(encodedBytes, signatureBytes, key, jwtToken.Alg, validationParameters).ConfigureAwait(false))
                    {
                        LogHelper.LogInformation(TokenLogMessages.IDX10242, token);
                        jwtToken.SigningKey = key;
                        return jwtToken;
                    }
                }
                catch (Exception ex)
                {
                    exceptionStrings.AppendLine(ex.ToString());
                }

                if (key != null)
                {
                    keysAttempted.AppendLine(key.ToString() + " , KeyId: " + key.KeyId);
                    if (canMatchKey && !keyMatched && key.KeyId != null)
                        keyMatched = jwtToken.Kid.Equals(key.KeyId, StringComparison.Ordinal);
                }
            }

            // if the kid != null and the signature fails, throw SecurityTokenSignatureKeyNotFoundException
            if (!keyMatched && canMatchKey && keysAttempted.Length > 0)
                throw LogHelper.LogExceptionMessage(new SecurityTokenSignatureKeyNotFoundException(LogHelper.FormatInvariant(TokenLogMessages.IDX10501, jwtToken.Kid, jwtToken)));

            if (keysAttempted.Length > 0)
                throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(LogHelper.FormatInvariant(TokenLogMessages.IDX10503, keysAttempted, exceptionStrings, jwtToken)));

            throw LogHelper.LogExceptionMessage(new SecurityTokenInvalidSignatureException(TokenLogMessages.IDX10500));
        }      
     
        /// <summary>
        /// Obtains a <see cref="SignatureProvider "/> and validates the signature.
        /// </summary>
        /// <param name="encodedBytes">Bytes to validate.</param>
        /// <param name="signature">Signature to compare against.</param>
        /// <param name="key"><See cref="SecurityKey"/> to use.</param>
        /// <param name="algorithm">Crypto algorithm to use.</param>
        /// <param name="validationParameters">Priority will be given to <see cref="TokenValidationParameters.CryptoProviderFactory"/> over <see cref="SecurityKey.CryptoProviderFactory"/>.</param>
        /// <returns>'true' if signature is valid.</returns>
        private async Task<bool> ValidateSignatureAsync(byte[] encodedBytes, byte[] signature, SecurityKey key, string algorithm, TokenValidationParameters validationParameters)
        {
            var cryptoProviderFactory = validationParameters.CryptoProviderFactory ?? key.CryptoProviderFactory;
            if (!cryptoProviderFactory.IsSupportedAlgorithm(algorithm, key))
            {
                LogHelper.LogInformation(LogMessages.IDX14000, algorithm, key);
                return false;
            }

            var signatureProvider = cryptoProviderFactory.CreateForVerifying(key, algorithm);
            if (signatureProvider == null)
                throw LogHelper.LogExceptionMessage(new InvalidOperationException(LogHelper.FormatInvariant(TokenLogMessages.IDX10647, (key == null ? "Null" : key.ToString()), (algorithm == null ? "Null" : algorithm))));

            try
            {
                return await signatureProvider.VerifyAsync(encodedBytes, signature).ConfigureAwait(false);
            }
            finally
            {
                cryptoProviderFactory.ReleaseSignatureProvider(signatureProvider);
            }
        }

        private async Task<TokenValidationResult> ValidateTokenPayloadAsync(JsonWebToken jwtToken, TokenValidationParameters validationParameters)
        {

            DateTime? expires = (jwtToken.ValidTo == null) ? null : new DateTime?(jwtToken.ValidTo);
            DateTime? notBefore = (jwtToken.ValidFrom == null) ? null : new DateTime?(jwtToken.ValidFrom);

            Validators.ValidateLifetime(notBefore, expires, jwtToken, validationParameters);
            Validators.ValidateAudience(jwtToken.Audiences, jwtToken, validationParameters);
            string issuer = Validators.ValidateIssuer(jwtToken.Issuer, jwtToken, validationParameters);
            Validators.ValidateTokenReplay(expires, jwtToken.RawData, validationParameters);
            if (validationParameters.ValidateActor && !string.IsNullOrWhiteSpace(jwtToken.Actor))
            {
                var actorValidationResult = await ValidateJWSAsync(jwtToken.Actor, validationParameters.ActorValidationParameters ?? validationParameters).ConfigureAwait(false);
            }

            Validators.ValidateIssuerSecurityKey(jwtToken.SigningKey, jwtToken, validationParameters);

            return new TokenValidationResult
            {
                SecurityToken = jwtToken
            };
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        public override void WriteToken(XmlWriter writer, SecurityToken token)
        {
            throw new NotImplementedException();
        }
    }
}
