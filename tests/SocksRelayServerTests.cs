﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SocksRelayServer;
using SocksSharp;
using SocksSharp.Proxy;

namespace SocksRelayServerTests
{
    [TestClass]
    public class SocksRelayServerTests
    {
        private static IPAddress _remoteProxyAddress = IPAddress.Parse("192.168.0.101");
        private static int _remoteProxyPort = 1080;

        public SocksRelayServerTests()
        {
            var remoteProxyAddress = Environment.GetEnvironmentVariable("REMOTE_PROXY_ADDRESS");
            if (!string.IsNullOrEmpty(remoteProxyAddress))
            {
                _remoteProxyAddress = IPAddress.Parse(remoteProxyAddress);
            }

            var remoteProxyPort = Environment.GetEnvironmentVariable("REMOTE_PROXY_PORT");
            if (!string.IsNullOrEmpty(remoteProxyPort))
            {
                _remoteProxyPort = int.Parse(remoteProxyPort);
            }
        }

        [TestInitialize]
        public void IsRemoteProxyListening()
        {
            using (var client = new TcpClient())
            {
                try
                {
                    client.Connect(_remoteProxyAddress, _remoteProxyPort);
                }
                catch (SocketException)
                {
                    Assert.Fail($"Remote proxy server is not running on {_remoteProxyAddress}:{_remoteProxyPort}, check configured IP and port");
                }

                client.Close();
            }
        }

        [TestMethod]
        public void CheckIfTrafficIsNotMalformed()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.Start();

                var tasks = new List<Task>();
                for (var i = 0; i < 8; i++)
                {
                    tasks.Add(TestHelpers.DoTestRequest<Socks4a>(relay.LocalEndPoint, "https://httpbin.org/headers"));
                }

                Task.WaitAll(tasks.ToArray());
            }
        }

        [TestMethod]
        public async Task CheckIfLargeFilesAreDownloaded()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.Start();

                var settings = new ProxySettings
                {
                    Host = relay.LocalEndPoint.Address.ToString(),
                    Port = relay.LocalEndPoint.Port,
                    ConnectTimeout = 15000,
                    ReadWriteTimeOut = 15000,
                };

                using (var proxyClientHandler = new ProxyClientHandler<Socks4a>(settings))
                {
                    using (var httpClient = new HttpClient(proxyClientHandler))
                    {
                        var request = new HttpRequestMessage
                        {
                            Version = HttpVersion.Version11,
                            Method = HttpMethod.Get,
                            RequestUri = new Uri("https://updates.tdesktop.com/tsetup/tportable.1.8.15.zip"),
                        };

                        var response = await httpClient.SendAsync(request);
                        var content = await response.Content.ReadAsByteArrayAsync();

                        var contentLengthHeader = long.Parse(response.Content.Headers.First(h => h.Key.Equals("Content-Length")).Value.First());
                        Assert.IsTrue(contentLengthHeader > 5 * 1024 * 1024); // at least 5 MB

                        Assert.AreEqual(contentLengthHeader, content.Length);
                    }
                }
            }
        }

        [TestMethod]
        public async Task CheckRelayingToIpv4Address()
        {
            using (var relay = CreateRelayServer())
            {
                relay.Start();

                await TestHelpers.DoTestRequest<Socks4>(relay.LocalEndPoint, "http://172.217.18.78/");
            }
        }

        [TestMethod]
        public async Task CheckRelayingToHostnameResolveLocally()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.Start();

                await TestHelpers.DoTestRequest<Socks4a>(relay.LocalEndPoint, "https://www.thinkwithgoogle.com/intl/de-de/");
            }
        }

        [TestMethod]
        public async Task CheckRelayingToHostnameResolveRemotely()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = true;
                relay.Start();

                await TestHelpers.DoTestRequest<Socks4a>(relay.LocalEndPoint, "https://www.thinkwithgoogle.com/intl/de-de/");
            }
        }

        [TestMethod]
        public async Task CheckRelayingToNonExistentIpAddress()
        {
            using (var relay = CreateRelayServer())
            {
                relay.Start();

                var settings = new ProxySettings
                {
                    Host = relay.LocalEndPoint.Address.ToString(),
                    Port = relay.LocalEndPoint.Port,
                    ConnectTimeout = 15000,
                    ReadWriteTimeOut = 15000,
                };

                using (var proxyClientHandler = new ProxyClientHandler<Socks4a>(settings))
                {
                    using (var httpClient = new HttpClient(proxyClientHandler))
                    {
                        try
                        {
                            await httpClient.GetAsync("http://0.1.2.3");
                            Assert.Fail();
                        }
                        catch (ProxyException e)
                        {
                            Assert.AreEqual("Request rejected or failed", e.Message);
                        }
                    }
                }
            }
        }

        [TestMethod]
        public async Task CheckRelayingToNonExistentHostnameResolveLocally()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.Start();

                var settings = new ProxySettings
                {
                    Host = relay.LocalEndPoint.Address.ToString(),
                    Port = relay.LocalEndPoint.Port,
                    ConnectTimeout = 15000,
                    ReadWriteTimeOut = 15000,
                };

                using (var proxyClientHandler = new ProxyClientHandler<Socks4a>(settings))
                {
                    using (var httpClient = new HttpClient(proxyClientHandler))
                    {
                        try
                        {
                            await httpClient.GetAsync("https://nonexists-subdomain.google.com");
                            Assert.Fail();
                        }
                        catch (ProxyException)
                        {
                            // this is expected
                        }
                    }
                }
            }
        }

        [TestMethod]
        public async Task CheckRelayingToNonExistentHostnameUsingCustomResolver()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.DnsResolver = new CustomDnsResolver();
                relay.Start();

                var settings = new ProxySettings
                {
                    Host = relay.LocalEndPoint.Address.ToString(),
                    Port = relay.LocalEndPoint.Port,
                    ConnectTimeout = 15000,
                    ReadWriteTimeOut = 15000,
                };

                using (var proxyClientHandler = new ProxyClientHandler<Socks4a>(settings))
                {
                    using (var httpClient = new HttpClient(proxyClientHandler))
                    {
                        try
                        {
                            await httpClient.GetAsync("https://nonexists-subdomain.google.com");
                            Assert.Fail();
                        }
                        catch (ProxyException)
                        {
                            // this is expected
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void CheckRelayingToHostnameUsingCustomResolver()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = false;
                relay.DnsResolver = new CustomDnsResolver();
                relay.Start();

                var tasks = new List<Task>();
                for (var i = 0; i < 8; i++)
                {
                    tasks.Add(TestHelpers.DoTestRequest<Socks4a>(relay.LocalEndPoint, "https://www.thinkwithgoogle.com/intl/de-de/"));
                }

                Task.WaitAll(tasks.ToArray());
            }
        }

        [TestMethod]
        public async Task CheckRelayingToNonExistentHostnameResolveRemotely()
        {
            using (var relay = CreateRelayServer())
            {
                relay.ResolveHostnamesRemotely = true;
                relay.Start();

                var settings = new ProxySettings
                {
                    Host = relay.LocalEndPoint.Address.ToString(),
                    Port = relay.LocalEndPoint.Port,
                    ConnectTimeout = 15000,
                    ReadWriteTimeOut = 15000,
                };

                using (var proxyClientHandler = new ProxyClientHandler<Socks4a>(settings))
                {
                    using (var httpClient = new HttpClient(proxyClientHandler))
                    {
                        try
                        {
                            await httpClient.GetAsync("https://nonexists-subdomain.google.com");
                            Assert.Fail();
                        }
                        catch (ProxyException e)
                        {
                            Assert.AreEqual("Request rejected or failed", e.Message);
                        }
                    }
                }
            }
        }

        private static ISocksRelayServer CreateRelayServer()
        {
            ISocksRelayServer relay = new SocksRelayServer.SocksRelayServer(new IPEndPoint(IPAddress.Loopback, TestHelpers.GetFreeTcpPort()), new IPEndPoint(_remoteProxyAddress, _remoteProxyPort));

            relay.OnLogMessage += (sender, s) => Console.WriteLine($"OnLogMessage: {s}");
            relay.OnLocalConnect += (sender, endpoint) => Console.WriteLine($"Accepted connection from {endpoint}");
            relay.OnRemoteConnect += (sender, endpoint) => Console.WriteLine($"Opened connection to {endpoint}");

            Console.WriteLine($"Created new instance of RelayServer on {relay.LocalEndPoint}");

            return relay;
        }
    }
}
