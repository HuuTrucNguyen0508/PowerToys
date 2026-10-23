// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class SocketJoinTests
{
    [TestMethod]
    public void HandshakingSocketCountsAsAnInFlightConnection()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var address = IPAddress.Parse("192.168.1.20");
        var tcp = new TcpSk(true, socket, SocketStatus.Handshaking, "PEER", address);
        var sockets = (SocketStuff)RuntimeHelpers.GetUninitializedObject(typeof(SocketStuff));
        sockets.TcpSocketsLock = new object();
        typeof(SocketStuff).GetProperty("TcpSockets", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sockets, new List<TcpSk> { tcp });
        var previous = Common.Sk;
        try
        {
            Common.Sk = sockets;
            Assert.IsTrue(Common.IsConnectingByAClientSocketTo("PEER", address));
            Assert.IsFalse(Common.IsConnectingByAClientSocketTo("OTHER", address));
            Assert.IsFalse(Common.IsConnectingByAClientSocketTo("PEER", IPAddress.Parse("192.168.1.21")));
            tcp.Status = SocketStatus.Connected;
            Assert.IsFalse(Common.IsConnectingByAClientSocketTo("PEER", address));
        }
        finally
        {
            Common.Sk = previous;
        }
    }
}
