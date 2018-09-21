﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ProxyServerSharp;

namespace Socks4ToSock5
{
    class Program
    {
        static void Main(string[] args)
        {
            // SOCKS 5
            var client = LMKR.SocksProxy.ConnectToSocks5Proxy(
                "127.0.0.1", 9951, "80.77.123.12",
                80, String.Empty, String.Empty);
            string strGet = "GET / HTTP/1.1\r\nHost: deaknet.hu\r\nAccept: image/gif, image/jpeg, */*\r\nAccept-Language: en-us\r\nAccept-Encoding: gzip, deflate\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1)\r\n\r\n";
            var integer = client.Send(System.Text.Encoding.ASCII.GetBytes(strGet));

            ReadResponse(client);

            // SOCKS 4
            var server = new SOCKS4Server(1080,4096);
            server.LocalConnect += server_LocalConnect;
            server.RemoteConnect += server_RemoteConnect;
            server.Start();
            Thread.Sleep(10000000);
        }

        static void server_RemoteConnect(object sender, System.Net.IPEndPoint iep)
        {
            Console.WriteLine($"RemoteConnect: {iep.ToString()}");
        }

        static void server_LocalConnect(object sender, System.Net.IPEndPoint iep)
        {
            var socks4Server = (SOCKS4Server) sender;
            Console.WriteLine($"LocalConnect: {iep.ToString()}");
        }

        static void ReadResponse(Socket socket)
        {
            bool flag = true; // just so we know we are still reading
            string headerString = ""; // to store header information
            int contentLength = 0; // the body length
            byte[] bodyBuff = new byte[0]; // to later hold the body content
            while (flag)
            {
                // read the header byte by byte, until \r\n\r\n
                byte[] buffer = new byte[1];
                socket.Receive(buffer, 0, 1, 0);
                headerString += Encoding.ASCII.GetString(buffer);
                contentLength = 0;
                if (headerString.Contains("\r\n\r\n"))
                {
                    var headers = headerString.Split(new string[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var header in headers)
                    {
                        var keyValues = header.Split(new string[] {":"}, StringSplitOptions.RemoveEmptyEntries);
                        if (keyValues[0].Trim().ToLowerInvariant().Equals("content-length"))
                        {
                            contentLength = int.Parse(keyValues[1]);
                            break;
                        }
                    }
                    
                    flag = false;
                    // read the body
                    bodyBuff = new byte[contentLength];
                    socket.Receive(bodyBuff, 0, contentLength, 0);
                }
            }
            Console.WriteLine("Server Response :");
            string body = Encoding.ASCII.GetString(bodyBuff);
            Console.WriteLine(body);
        }
    }
}
