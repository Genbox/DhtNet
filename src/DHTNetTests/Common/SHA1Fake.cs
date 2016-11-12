using System.Security.Cryptography;

namespace DHTNet.Tests.Common
{
    class SHA1Fake : SHA1
    {
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            
        }

        protected override byte[] HashFinal()
        {
            return new byte[20];
        }

        public override void Initialize()
        {
            
        }
    }
}
