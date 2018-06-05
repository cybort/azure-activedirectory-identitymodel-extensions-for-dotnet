//------------------------------------------------------------------------------
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

using System;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tests;
using Xunit;

using ALG = Microsoft.IdentityModel.Tokens.SecurityAlgorithms;
using EE = Microsoft.IdentityModel.Tests.ExpectedException;
using KM = Microsoft.IdentityModel.Tests.KeyingMaterial;

#pragma warning disable CS3016 // Arrays as attribute arguments is not CLS-compliant

namespace Microsoft.IdentityModel.Tokens.Tests
{
    /// <summary>
    /// Tests for CryptoProviderFactory
    /// </summary>
    public class CryptoProviderFactoryTests
    {
        /// <summary>
        /// Tests that defaults haven't changed.
        /// </summary>
        [Fact]
        public void Defaults()
        {
            TestUtilities.WriteHeader($"{this}.Defaults");
            var context = new CompareContext($"{this}.Defaults");

            var cryptoFactory1 = CryptoProviderFactory.Default;
            var cryptoFactory2 = CryptoProviderFactory.Default;
            if (!object.ReferenceEquals(cryptoFactory1, cryptoFactory2))
                context.Diffs.Add("!object.ReferenceEquals(cryptoFactory1, cryptoFactory2)");

            if (cryptoFactory2.CacheSignatureProviders)
                context.Diffs.Add("cryptoFactory2.CacheSignatureProviders should be false");

            if (cryptoFactory1.CustomCryptoProvider != null)
                context.Diffs.Add("cryptoFactory2.CustomCryptoProvider should be NULL");

            if (typeof(InMemoryCryptoProviderCache) != cryptoFactory1.CryptoProviderCache.GetType())
                context.Diffs.Add("typeof(InMemoryCryptoProviderCache) != cryptoFactory1.CryptoProviderCache.GetType()");

            TestUtilities.AssertFailIfErrors(context);
        }

        /// <summary>
        /// Tests that SymmetricSignatureProviders that fault will be removed from cache
        /// </summary>
        /// <param name="theoryData"></param>
        [Theory, MemberData(nameof(FaultingAsymmetricSignatureProvidersTheoryData))]
        public void FaultingAsymmetricSignatureProviders(SignatureProviderTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.FaultingAsymmetricSignatureProviders", theoryData);

            try
            {
                var bytes = new byte[256];
                var signingSignatureProvider = theoryData.CryptoProviderFactory.CreateForSigning(theoryData.SigningKey, theoryData.SigningAlgorithm) as AsymmetricSignatureProvider;
                var signedBytes = signingSignatureProvider.Sign(bytes);
                var verifyingSignatureProvider = theoryData.CryptoProviderFactory.CreateForVerifying(theoryData.VerifyKey, theoryData.VerifyAlgorithm) as AsymmetricSignatureProvider;
                verifyingSignatureProvider.Verify(bytes, signedBytes);

                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            var signingProviderFound = theoryData.CryptoProviderFactory.CryptoProviderCache.TryGetSignatureProvider(theoryData.SigningKey, theoryData.SigningAlgorithm, theoryData.SigningSignatureProviderType, true, out SignatureProvider signingProvider);
            if (signingProviderFound != theoryData.ShouldFindSignSignatureProvider)
                context.Diffs.Add($"(signingProviderFound '{signingProviderFound}' != theoryData.ShouldFindSigningSignatureProvider: '{theoryData.ShouldFindSignSignatureProvider}'");

            var verifyingProviderFound = theoryData.CryptoProviderFactory.CryptoProviderCache.TryGetSignatureProvider(theoryData.VerifyKey, theoryData.VerifyAlgorithm, theoryData.VerifySignatureProviderType, false, out SignatureProvider verifyingProvider);
            if (verifyingProviderFound != theoryData.ShouldFindVerifySignatureProvider)
                context.Diffs.Add($"(verifyingSignatureProviderFound '{verifyingProviderFound}' != theoryData.ShouldFindVerifyingSignatureProvider: '{theoryData.ShouldFindSignSignatureProvider}'");

            TestUtilities.AssertFailIfErrors(context);
        }

        /// <summary>
        /// When a SignatureProvider faults, we want to remove it from the cache.
        /// Otherwise the fault will continue and there is no opportunity for recovery.
        /// </summary>
        public static TheoryData<SignatureProviderTheoryData> FaultingAsymmetricSignatureProvidersTheoryData
        {
            get
            {
                var theoryData = new TheoryData<SignatureProviderTheoryData>();

                // signing dispose fault
                var signingSignatureProvider = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKey, ALG.RsaSha256, true);
                signingSignatureProvider.Dispose();
                theoryData.Add(new SignatureProviderTheoryData
                {
                    First = true,
                    ExpectedException = EE.ObjectDisposedException,
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.RsaSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider
                    },
                    ShouldFindSignSignatureProvider = false,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.RsaSha256,
                    SigningKey = Default.AsymmetricSigningKey,
                    SigningSignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    VerifyAlgorithm = ALG.RsaSha256,
                    VerifyKey = Default.AsymmetricSigningKeyPublic,
                    VerifySignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    TestId = "SignDisposeFault"
                });

                // verify dispose fault
                signingSignatureProvider = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKey, ALG.RsaSha256, true);
                var verifyingSignatureProvider = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKeyPublic, ALG.RsaSha256, false);
                verifyingSignatureProvider.Dispose();
                theoryData.Add(new SignatureProviderTheoryData
                {
                    First = true,
                    ExpectedException = EE.ObjectDisposedException,
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.RsaSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.RsaSha256,
                    SigningKey = Default.AsymmetricSigningKey,
                    SigningSignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    VerifyAlgorithm = ALG.RsaSha256,
                    VerifyKey = Default.AsymmetricSigningKeyPublic,
                    VerifySignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    TestId = "VerifyDisposeFault"
                });

                // signing public key fault
                var signingKey = new CustomRsaSecurityKey(2048, PrivateKeyStatus.Exists, KM.RsaParameters_2048_Public);
                signingSignatureProvider = new CustomAsymmetricSignatureProvider(signingKey, ALG.RsaSha256, true);
                theoryData.Add(new SignatureProviderTheoryData
                {
                    First = true,
#if NET452
                    ExpectedException = EE.CryptographicException(),
#else
                    ExpectedException = new EE(typeof(Exception)){IgnoreExceptionType = true},
#endif
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.RsaSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider
                    },
                    ShouldFindSignSignatureProvider = false,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.RsaSha256,
                    SigningKey = signingKey,
                    SigningSignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    VerifyAlgorithm = ALG.RsaSha256,
                    VerifyKey = Default.AsymmetricSigningKeyPublic,
                    VerifySignatureProviderType = typeof(CustomAsymmetricSignatureProvider).ToString(),
                    TestId = "SignPublicKeyFault"
                });

                return theoryData;
            }
        }

        /// <summary>
        /// Tests that SymmetricSignatureProviders that fault will be removed from cache
        /// </summary>
        /// <param name="theoryData"></param>
        [Theory, MemberData(nameof(FaultingSymmetricSignatureProvidersTheoryData))]
        public void FaultingSymmetricSignatureProviders(SignatureProviderTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.FaultingSymmetricSignatureProviders", theoryData);

            try
            {
                var bytes = new byte[256];
                var signingSignatureProvider = theoryData.CryptoProviderFactory.CreateForSigning(theoryData.SigningKey, theoryData.SigningAlgorithm) as SymmetricSignatureProvider;
                var signedBytes = signingSignatureProvider.Sign(bytes);
                var verifyingSignatureProvider = theoryData.CryptoProviderFactory.CreateForVerifying(theoryData.VerifyKey, theoryData.VerifyAlgorithm) as SymmetricSignatureProvider;
                if (theoryData.VerifySpecifyingLength)
                    verifyingSignatureProvider.Verify(bytes, signedBytes);
                else
                    verifyingSignatureProvider.Verify(bytes, signedBytes, bytes.Length - 1);

                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            var signProviderFound = theoryData.CryptoProviderFactory.CryptoProviderCache.TryGetSignatureProvider(theoryData.SigningKey, theoryData.SigningAlgorithm, theoryData.SigningSignatureProviderType, true, out SignatureProvider signingProvider);
            if (signProviderFound != theoryData.ShouldFindSignSignatureProvider)
                context.Diffs.Add($"signingProviderFound '{signProviderFound}' != theoryData.ShouldFindSignSignatureProvider: '{theoryData.ShouldFindSignSignatureProvider}'");

            var verifyProviderFound = theoryData.CryptoProviderFactory.CryptoProviderCache.TryGetSignatureProvider(theoryData.VerifyKey, theoryData.VerifyAlgorithm, theoryData.VerifySignatureProviderType, false, out SignatureProvider verifyingProvider);
            if (verifyProviderFound != theoryData.ShouldFindVerifySignatureProvider)
                context.Diffs.Add($"verifySignatureProviderFound '{verifyProviderFound}' != theoryData.ShouldFindVerifySignatureProvider: '{theoryData.ShouldFindVerifySignatureProvider}'");

            TestUtilities.AssertFailIfErrors(context);
        }

        /// <summary>
        /// When a SignatureProvider faults, we want to remove it from the cache.
        /// Otherwise the fault will continue and the next usage will result in a new provider
        /// </summary>
        public static TheoryData<SignatureProviderTheoryData> FaultingSymmetricSignatureProvidersTheoryData
        {
            get
            {
                var theoryData = new TheoryData<SignatureProviderTheoryData>();

                // signing will fault, signingSignatureProvider should be removed from cache,no need for verifying signature provider
                theoryData.Add(new SignatureProviderTheoryData
                {
                    First = true,
                    ExpectedException = EE.CryptographicException("KeyedHashAlgorithmThrowOnHashFinal"),
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true)
                        {
                            KeyedHashAlgorithmPublic = new CustomKeyedHashAlgorithm(Default.SymmetricSigningKey256.Key)
                            {
                                ThrowOnHashFinal = new CryptographicException("KeyedHashAlgorithmThrowOnHashFinal")
                            }
                        },
                    },
                    ShouldFindSignSignatureProvider = false,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "SignKeyedHashFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString()
                });

                // verifying will fault, verifying and signingSignatureProvider should be removed from cache since in symmetric case
                // they are the same.
                theoryData.Add(new SignatureProviderTheoryData
                {
                    ExpectedException = EE.CryptographicException("KeyedHashAlgorithmThrowOnHashFinal"),
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true),
                        VerifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false)
                        {
                            KeyedHashAlgorithmPublic = new CustomKeyedHashAlgorithm(Default.SymmetricSigningKey256.Key)
                            {
                                ThrowOnHashFinal = new CryptographicException("KeyedHashAlgorithmThrowOnHashFinal")
                            }
                        },
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "VerifyKeyedHashFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString()
                });

                // verifying will fault, verifying and signingSignatureProvider should be removed from cache since in symmetric case
                // they are the same.
                theoryData.Add(new SignatureProviderTheoryData
                {
                    ExpectedException = EE.CryptographicException("KeyedHashAlgorithmThrowOnHashFinal"),
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true),
                        VerifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false)
                        {
                            KeyedHashAlgorithmPublic = new CustomKeyedHashAlgorithm(Default.SymmetricSigningKey256.Key)
                            {
                                ThrowOnHashFinal = new CryptographicException("KeyedHashAlgorithmThrowOnHashFinal")
                            }
                        },
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "VerifySpecifyingLengthKeyedHashFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    VerifySpecifyingLength = true
                });

                // Symmetric disposed signing
                var signingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true);
                signingSignatureProvider.Dispose();
                var verifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false);
                theoryData.Add(new SignatureProviderTheoryData
                {
                    ExpectedException = EE.ObjectDisposedException,
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider,
                    },
                    ShouldFindSignSignatureProvider = false,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "SignDisposedFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString()
                });

                // Symmetric disposed verifying
                signingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true);
                verifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false);
                verifyingSignatureProvider.Dispose();
                theoryData.Add(new SignatureProviderTheoryData
                {
                    ExpectedException = EE.ObjectDisposedException,
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider,
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "VerifyDisposeFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString()
                });

                // Symmetric disposed verifying (specifying length)
                signingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true);
                verifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false);
                verifyingSignatureProvider.Dispose();
                theoryData.Add(new SignatureProviderTheoryData
                {
                    ExpectedException = EE.ObjectDisposedException,
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider,
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = false,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "VerifySpecifyingLengthDisposedFault",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    VerifySpecifyingLength = true
                });

                // Symmetric signing verifying succeed
                signingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true);
                verifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false);
                theoryData.Add(new SignatureProviderTheoryData
                {
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider,
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = true,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "SignVerifySucceed",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    VerifySpecifyingLength = false
                });

                // Symmetric signing verifying (specifying length) succeed
                signingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true);
                verifyingSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, false);
                theoryData.Add(new SignatureProviderTheoryData
                {
                    CryptoProviderFactory = new CustomCryptoProviderFactory(new string[] { ALG.HmacSha256 })
                    {
                        CacheSignatureProviders = true,
                        SigningSignatureProvider = signingSignatureProvider,
                        VerifyingSignatureProvider = verifyingSignatureProvider,
                    },
                    ShouldFindSignSignatureProvider = true,
                    ShouldFindVerifySignatureProvider = true,
                    SigningAlgorithm = ALG.HmacSha256,
                    SigningKey = Default.SymmetricSigningKey256,
                    SigningSignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    TestId = "SignVerifySpecifyingLengthSucceed",
                    VerifyAlgorithm = ALG.HmacSha256,
                    VerifyKey = Default.SymmetricSigningKey256,
                    VerifySignatureProviderType = typeof(CustomSymmetricSignatureProvider).ToString(),
                    VerifySpecifyingLength = true
                });

                return theoryData;
            }
        }


        [Theory, MemberData(nameof(ReleaseSignatureProvidersTheoryData))]
        public void ReleaseSignatureProviders(SignatureProviderTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.ReleaseSignatureProviders", theoryData);
            var cryptoProviderFactory = new CryptoProviderFactory();
            try
            { if (theoryData.CustomCryptoProvider != null)
                    cryptoProviderFactory.CustomCryptoProvider = theoryData.CustomCryptoProvider;
                cryptoProviderFactory.ReleaseSignatureProvider(theoryData.SigningSignatureProvider);
                if (theoryData.CustomCryptoProvider != null && theoryData.SigningSignatureProvider != null && !((CustomCryptoProvider)theoryData.CustomCryptoProvider).ReleaseCalled)
                    context.Diffs.Add("Release wasn't called on the CustomCryptoProvider.");
                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            TestUtilities.AssertFailIfErrors(context);
        }

        public static TheoryData<SignatureProviderTheoryData> ReleaseSignatureProvidersTheoryData
        {
            get
            {
                var cache = new InMemoryCryptoProviderCache();
                var asymmetricSignatureProvider = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKey, Default.AsymmetricSigningAlgorithm, true) { ThrowOnDispose = new InvalidOperationException() };
                var asymmetricSignatureProviderToRelease = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKey, Default.AsymmetricSigningAlgorithm, true);
                var symmetricSignatureProvider = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true) { ThrowOnDispose = new InvalidOperationException() };
                var asymmetricSignatureProviderCached = new CustomAsymmetricSignatureProvider(Default.AsymmetricSigningKey, Default.AsymmetricSigningAlgorithm, true) { ThrowOnDispose = new InvalidOperationException() };
                var symmetricSignatureProviderCached = new CustomSymmetricSignatureProvider(Default.SymmetricSigningKey256, ALG.HmacSha256, true) { ThrowOnDispose = new InvalidOperationException() };
                cache.TryAdd(asymmetricSignatureProviderCached);
                cache.TryAdd(symmetricSignatureProviderCached);

                var theoryData = new TheoryData<SignatureProviderTheoryData>
                {
                    new SignatureProviderTheoryData
                    {
                        ExpectedException = EE.InvalidOperationException(),
                        First = true,
                        SigningSignatureProvider = asymmetricSignatureProvider,
                        TestId = "Release1"
                    },
                    new SignatureProviderTheoryData
                    {
                        SigningSignatureProvider = asymmetricSignatureProviderCached,
                        TestId = "Release2"
                    },
                    new SignatureProviderTheoryData
                    {
                        ExpectedException = EE.InvalidOperationException(),
                        SigningSignatureProvider = symmetricSignatureProvider,
                        TestId = "Release3"
                    },
                    new SignatureProviderTheoryData
                    {
                        SigningSignatureProvider = symmetricSignatureProviderCached,
                        TestId = "Release4"
                    },
                    new SignatureProviderTheoryData
                    {
                       CustomCryptoProvider = new CustomCryptoProvider(new string[] {"RS256"})
                       {
                           SignatureProvider = asymmetricSignatureProviderToRelease
                       },
                       SigningSignatureProvider = asymmetricSignatureProviderToRelease,
                       TestId = "CustomCryptoProviderRelease"
                    },
                    new SignatureProviderTheoryData
                    {
                       ExpectedException = EE.ArgumentNullException(),
                       CustomCryptoProvider = new CustomCryptoProvider(new string[] {"RS256"})
                       {
                           SignatureProvider = asymmetricSignatureProviderToRelease
                       },
                       SigningSignatureProvider = null,
                       TestId = "CustomCryptoProviderRelease - SignatureProvider null"
                    }
                };

                return theoryData;
            }
        }

        [Theory, MemberData(nameof(ReleaseHashAlgorithmsTheoryData))]
        public void ReleaseHashAlgorithms(CryptoProviderFactoryTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.ReleaseHashAlgorithms", theoryData);
            var cryptoProviderFactory = theoryData.CryptoProviderFactory;
            try
            {
                cryptoProviderFactory.ReleaseHashAlgorithm(theoryData.HashAlgorithm);
                if (theoryData.CustomCryptoProvider != null && theoryData.HashAlgorithm != null && !((CustomCryptoProvider)theoryData.CustomCryptoProvider).ReleaseCalled)
                    context.Diffs.Add("Release wasn't called on the CustomCryptoProvider.");
                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            TestUtilities.AssertFailIfErrors(context);
        }

        public static TheoryData<CryptoProviderFactoryTheoryData> ReleaseHashAlgorithmsTheoryData
        {
            get
            {
                var customCryptoProvider = new CustomCryptoProvider(new string[] { SecurityAlgorithms.Sha256 })
                {
                    HashAlgorithm = Default.HashAlgorithm
                };
                var cryptoProviderFactory = new CryptoProviderFactory() { CustomCryptoProvider = customCryptoProvider };
 
                var theoryData = new TheoryData<CryptoProviderFactoryTheoryData>
                {
                    new CryptoProviderFactoryTheoryData
                    {
                       First = true,
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       HashAlgorithm = (HashAlgorithm) cryptoProviderFactory.CreateHashAlgorithm(SecurityAlgorithms.Sha256),
                       TestId = "CustomCryptoProviderRelease"
                    },
                    new CryptoProviderFactoryTheoryData
                    {
                       ExpectedException = EE.ArgumentNullException(),
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       HashAlgorithm = null,
                       TestId = "CustomCryptoProviderRelease - HashAlgorithm null"
                    }
                };

                return theoryData;
            }
        }

        [Theory, MemberData(nameof(ReleaseKeyWrapProvidersTheoryData))]
        public void ReleaseKeyWrapProviders(CryptoProviderFactoryTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.ReleaseKeyWrapProviders", theoryData);
            var cryptoProviderFactory = theoryData.CryptoProviderFactory;
            try
            {
                cryptoProviderFactory.ReleaseKeyWrapProvider(theoryData.KeyWrapProvider);
                if (theoryData.CustomCryptoProvider != null && theoryData.KeyWrapProvider != null && !((CustomCryptoProvider)theoryData.CustomCryptoProvider).ReleaseCalled)
                    context.Diffs.Add("Release wasn't called on the CustomCryptoProvider.");
                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            TestUtilities.AssertFailIfErrors(context);
        }

        public static TheoryData<CryptoProviderFactoryTheoryData> ReleaseKeyWrapProvidersTheoryData
        {
            get
            {
                SecurityKey key = Default.SymmetricEncryptionKey128;
                var provider = key.CryptoProviderFactory.CreateKeyWrapProvider(key, SecurityAlgorithms.Aes128KW);
                var customCryptoProvider = new CustomCryptoProvider(new string[] { SecurityAlgorithms.Aes128KW })
                {
                    KeyWrapProvider = provider
                };
                var cryptoProviderFactory = new CryptoProviderFactory() { CustomCryptoProvider = customCryptoProvider };

                var theoryData = new TheoryData<CryptoProviderFactoryTheoryData>
                {
                    new CryptoProviderFactoryTheoryData
                    {
                       First = true,
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       KeyWrapProvider = provider,
                       TestId = "CustomCryptoProviderRelease"
                    },
                    new CryptoProviderFactoryTheoryData
                    {
                       ExpectedException = EE.ArgumentNullException(),
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       KeyWrapProvider = null,
                       TestId = "CustomCryptoProviderRelease - KeyWrapProvider null"
                    }
                };

                return theoryData;
            }
        }

        [Theory, MemberData(nameof(ReleaseRsaKeyWrapProvidersTheoryData))]
        public void ReleaseRsaKeyWrapProviders(CryptoProviderFactoryTheoryData theoryData)
        {
            IdentityModelEventSource.ShowPII = true;
            var context = TestUtilities.WriteHeader($"{this}.ReleaseRsaKeyWrapProviders", theoryData);
            var cryptoProviderFactory = theoryData.CryptoProviderFactory;
            try
            {
                cryptoProviderFactory.ReleaseKeyWrapProvider(theoryData.RsaKeyWrapProvider);
                if (theoryData.CustomCryptoProvider != null && theoryData.RsaKeyWrapProvider != null && !((CustomCryptoProvider)theoryData.CustomCryptoProvider).ReleaseCalled)
                    context.Diffs.Add("Release wasn't called on the CustomCryptoProvider.");
                theoryData.ExpectedException.ProcessNoException(context);
            }
            catch (Exception ex)
            {
                theoryData.ExpectedException.ProcessException(ex, context);
            }

            TestUtilities.AssertFailIfErrors(context);
        }

        public static TheoryData<CryptoProviderFactoryTheoryData> ReleaseRsaKeyWrapProvidersTheoryData
        {
            get
            {
                SecurityKey key = Default.SymmetricEncryptionKey128;
                var provider = (RsaKeyWrapProvider) key.CryptoProviderFactory.CreateKeyWrapProvider(KeyingMaterial.RsaSecurityKey1, SecurityAlgorithms.RsaPKCS1);
                var customCryptoProvider = new CustomCryptoProvider(new string[] { SecurityAlgorithms.RsaPKCS1 })
                {
                    RsaKeyWrapProvider = provider
                };
                var cryptoProviderFactory = new CryptoProviderFactory() { CustomCryptoProvider = customCryptoProvider };

                var theoryData = new TheoryData<CryptoProviderFactoryTheoryData>
                {
                    new CryptoProviderFactoryTheoryData
                    {
                       First = true,
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       RsaKeyWrapProvider = provider,
                       TestId = "CustomCryptoProviderRelease"
                    },
                    new CryptoProviderFactoryTheoryData
                    {
                       ExpectedException = EE.ArgumentNullException(),
                       CustomCryptoProvider = customCryptoProvider,
                       CryptoProviderFactory = cryptoProviderFactory,
                       RsaKeyWrapProvider = null,
                       TestId = "CustomCryptoProviderRelease - RsaKeyWrapProvider null"
                    }
                };

                return theoryData;
            }
        }
    }
}
#pragma warning restore CS3016 // Arrays as attribute arguments is not CLS-compliant
