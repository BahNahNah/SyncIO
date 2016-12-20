﻿using SyncIO.Transport;
using SyncIO.Transport.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SyncIO.Server.Network {
    internal delegate void OnTCPSocketException(TcpSocket sender, Exception e);

    /// <summary>
    /// Internal socket used for both client and server.
    /// </summary>
    internal class TcpSocket : SyncIOSocket {

        public event OnTCPSocketException OnException;
        public event Action<TcpSocket, Socket> OnClientConnect;

        public TransportProtocal Protocal { get; }
        public bool Binded => (NetworkSocket?.Connected ?? false) && SuccessfulBind;
       

        private AsyncCallback InternalAcceptHandler;
        private Socket NetworkSocket;
        private bool SuccessfulBind = false;
       

        public TcpSocket(TransportProtocal _protocal) {
            Protocal = _protocal;
            InternalAcceptHandler = new AsyncCallback(HandleAccept);
        }

        public TcpSocket() :this(TransportProtocal.IPv4) {
        }

        /// <summary>
        /// Disposes old socket if exists. 
        /// Creates a new TCP socket with either IPv4 or IPv6 depending on what is specified in the constructor.
        /// </summary>
        private void CreateNewSocket() {
            NetworkSocket?.Dispose();
            if (Protocal == TransportProtocal.IPv6)
                NetworkSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            else
                NetworkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public bool Connect(string host, int port) {
            CreateNewSocket();
            Port = port;
            try {
                NetworkSocket.Connect(host, port);
                SuccessfulBind = true;
            } catch(Exception ex) {
                SuccessfulBind = false;
                OnException?.Invoke(this, ex);
            }
            return SuccessfulBind;
        }

        public bool Connect(EndPoint endpoint) {
            CreateNewSocket();
            try {
                SuccessfulBind = true;
            }catch(Exception ex) {
                SuccessfulBind = false;
                OnException?.Invoke(this, ex);
            }
            return SuccessfulBind;
        }

        public bool BeginAccept (EndPoint ep) {
            CreateNewSocket();
            try {
                NetworkSocket.Bind(ep);
                NetworkSocket.Listen(50);
                SuccessfulBind = true;
            } catch { 
                NetworkSocket = null;
                SuccessfulBind = false;
                return false;
            } 
           
            NetworkSocket.BeginAccept(InternalAcceptHandler, null);
            return true;
        }
        public bool BeginAccept(int port) {
            return BeginAccept(new IPEndPoint(IPAddress.Any, port));
        }

        private void HandleAccept(IAsyncResult ar) {
            try {
                Socket s = NetworkSocket.EndAccept(ar);
                OnClientConnect?.Invoke(this, s);
            } catch (Exception ex) {
                OnException?.Invoke(this, ex);
            }
            NetworkSocket.BeginAccept(InternalAcceptHandler, null);
        }

        public override void Close() {
            if (Binded) {
                NetworkSocket.Shutdown(SocketShutdown.Both);
                NetworkSocket.Dispose();
                NetworkSocket = null;
            }
        }
    }
}
