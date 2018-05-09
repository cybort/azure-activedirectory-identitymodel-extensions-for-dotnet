﻿using S2SMiddleTier;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.IdentityModel.Tokens.Jwt.Tests
{
    class AsyncCryptoProvider : ICryptoProvider
    {

        private AsyncAsymmetricSignatureProvider _signatureProvider;
        public AsyncCryptoProvider(SecurityKey key, string algorithm, bool willCreateSignatures)
        {
            _signatureProvider = new AsyncAsymmetricSignatureProvider(key, algorithm, willCreateSignatures);
        }

        public object Create(string algorithm, params object[] args)
        {
            return _signatureProvider;
        }

        public bool IsSupportedAlgorithm(string algorithm, params object[] args)
        {
            return true;
        }

        public void Release(object cryptoInstance)
        {
        }
    }
}
