#if !DISABLE_DHT
//
// TokenManager.cs
//
// Authors:
//   Olivier Dufour <olivier.duff@gmail.com>
//
// Copyright (C) 2008 Olivier Dufour
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Security.Cryptography;
using DHTNet.BEncode;

namespace DHTNet.Nodes
{
    internal class TokenManager
    {
        private byte[] secret;
        private byte[] previousSecret;
        private DateTime LastSecretGeneration;
        private RandomNumberGenerator random;
        private IncrementalHash sha1;
        private TimeSpan timeout = TimeSpan.FromMinutes(5);

        internal TimeSpan Timeout
        {
            get { return timeout; }
            set { timeout = value; }
        }

        public TokenManager()
        {
            sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            random = RandomNumberGenerator.Create();
            LastSecretGeneration = DateTime.MinValue; //in order to force the update
            secret = new byte[10];
            previousSecret = new byte[10];

            //PORT NOTE: Used GetNonZeroBytes() here before
            random.GetBytes(secret);
            random.GetBytes(previousSecret);
        }
        public BEncodedString GenerateToken(Node node)
        {
            return GetToken(node, secret);
        }

        public bool VerifyToken(Node node, BEncodedString token)
        {
            return (token.Equals(GetToken(node, secret)) || token.Equals(GetToken(node, previousSecret)));
        }
        
        private BEncodedString GetToken(Node node, byte[] s)
        {
            //refresh secret needed
            if (LastSecretGeneration.Add(timeout) < DateTime.UtcNow)
            {
                LastSecretGeneration = DateTime.UtcNow;
                secret.CopyTo(previousSecret, 0);

                //PORT NOTE: Used GetNonZeroBytes() here before
                random.GetBytes(secret);
            }

            byte[] n = node.CompactPort().TextBytes;

            sha1.AppendData(n);
            sha1.AppendData(s);

            return sha1.GetHashAndReset();
        }
    }
}
#endif