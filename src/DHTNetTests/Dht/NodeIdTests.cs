using DHTNet.Nodes;
using NUnit.Framework;

namespace DHTNet.Tests.Dht
{
    [TestFixture]
    public class NodeIdTests
    {
        [SetUp]
        public void Setup()
        {
            _nodes = new NodeId[20];
            for (int i = 0; i < _nodes.Length; i++)
            {
                byte[] id = new byte[20];
                for (int j = 0; j < id.Length; j++)
                    id[j] = (byte) (i * 20 + j);
                _nodes[i] = new NodeId(id);
            }
        }

        private NodeId[] _nodes;

        [Test]
        public void CompareTest()
        {
            byte[] i = new byte[20];
            byte[] j = new byte[20];
            i[19] = 1;
            j[19] = 2;
            NodeId one = new NodeId(i);
            NodeId two = new NodeId(j);
            Assert.IsTrue(one.CompareTo(two) < 0);
            Assert.IsTrue(two.CompareTo(one) > 0);
            Assert.IsTrue(one.CompareTo(one) == 0);
        }

        [Test]
        public void CompareTest2()
        {
            byte[] data = {1, 179, 114, 132, 233, 117, 195, 250, 164, 35, 157, 48, 170, 96, 87, 111, 42, 137, 195, 199};
            BigInteger a = new BigInteger(data);
            BigInteger b = new BigInteger(new byte[0]);

            Assert.AreNotEqual(a, b, "#1");
        }

        [Test]
        public void GreaterLessThanTest()
        {
            Assert.IsTrue(_nodes[0] < _nodes[1], "#1");
            Assert.IsTrue(_nodes[1] > _nodes[0], "#2");
            Assert.IsTrue(_nodes[0] == _nodes[0], "#3");
            Assert.AreEqual(_nodes[0], _nodes[0], "#4");
            Assert.IsTrue(_nodes[2] > _nodes[1], "#5");
            Assert.IsTrue(_nodes[15] < _nodes[10], "#6");
        }

        [Test]
        public void XorTest()
        {
            NodeId zero = new NodeId(new byte[20]);

            byte[] b = new byte[20];
            b[0] = 1;
            NodeId one = new NodeId(b);

            NodeId r = one.Xor(zero);
            Assert.AreEqual(one, r, "#1");
            Assert.IsTrue(one > zero, "#2");
            Assert.IsTrue(one.CompareTo(zero) > 0, "#3");

            NodeId z = one.Xor(r);
            Assert.AreEqual(zero, z, "#4");
        }
    }
}