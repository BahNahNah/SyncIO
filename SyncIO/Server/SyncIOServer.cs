﻿using SyncIO.Network;
using SyncIO.Network.Callbacks;
using SyncIO.Transport;
using SyncIO.Transport.Packets;
using SyncIO.Transport.Packets.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Collections;
using SyncIO.Server.RemoteCalls;
using SyncIO.Transport.RemoteCalls;

namespace SyncIO.Server {
    public delegate void OnClientConnectDelegate(SyncIOServer sender, SyncIOConnectedClient client);
    public class SyncIOServer : IEnumerable<SyncIOSocket>{
        public event OnClientConnectDelegate OnClientConnect;
        public TransportProtocal Protocal { get; }

        public ClientManager Clients { get; private set; }

        private Packager Packager;
        private CallbackManager<SyncIOConnectedClient> Callbacks;
        private RemoteCallServerManager RemoteFuncs;
        private Func<Guid> GuidGenerator = Guid.NewGuid;
        private List<SyncIOSocket> OpenSockets = new List<SyncIOSocket>();

        public SyncIOServer(TransportProtocal _protocal, Packager _packager) {
            Protocal = _protocal;
            Packager = _packager;
            Callbacks = new CallbackManager<SyncIOConnectedClient>();
            Clients = new ClientManager();
            RemoteFuncs = new RemoteCallServerManager(Packager);

            SetHandler<RemoteCallRequest>((cl, att) => RemoteFuncs.HandleClientFunctionCall(cl, att));
        }

        public SyncIOServer() : this(TransportProtocal.IPv4, new Packager()) {
        }

        /// <summary>
        /// Listens on a new port.
        /// </summary>
        /// <param name="port">Port to listen</param>
        /// <returns>The open socket on success, else null.</returns>
        public SyncIOSocket ListenTCP(int port) {
            var baseSock = new BaseServerSocket(Protocal);
            baseSock.OnClientConnect += TcpSock_OnClientConnect;
            if (!baseSock.BeginAccept(port))
                return null;

            OpenSockets.Add(baseSock);
            baseSock.OnClose += (s, err) => {
                OpenSockets.Remove(s);
            };

            baseSock.UdpDataReceved += HandleUDPData;

            return baseSock;
        }

        private void HandleUDPData(byte[] data) {
            try {
                var p = Packager.UnpackIdentified(data);

                var client = Clients[p.ID] as InternalSyncIOConnectedClient;
                if (client != null) {

                    if(p.Packet is UdpHandshake) {
                        client.Send(p.Packet);
                    }else {
                        ReceveHandler(client, p.Packet);
                    }

                }
                    

            } catch {
                //Failed UDP accept.
            }
        }

        private void TcpSock_OnClientConnect(BaseServerSocket sender, Socket s) {
            var client = new InternalSyncIOConnectedClient(s, Packager);

            client.SetID(GuidGenerator());
            client.BeginReceve(ReceveHandler);
            client.Send((cl) => {

                Clients.Add(cl);
                client.OnDisconnect += (c, err) => {
                    Clients.Remove(c);
                };

                OnClientConnect?.Invoke(this, cl);//Trigger event after handshake packet has been sent.
            }, new HandshakePacket(true, client.ID));
            
        }

        private void ReceveHandler(InternalSyncIOConnectedClient client, IPacket data) {
            Callbacks.Handle(client, data);
        }

        /// <summary>
        /// If not set, clients may receve duplicate Guids.
        /// </summary>
        /// <param name="_call">Call to guid generator. By default is Guid.NewGuid</param>
        public void SetGuidGenerator(Func<Guid> _call) {
            if (_call == null)
                return;
            GuidGenerator = _call;
        }


        /// <summary>
        /// Add handler for raw object array receve
        /// </summary>
        /// <param name="callback"></param>
        public void SetHandler(Action<SyncIOConnectedClient, object[]> callback) {
            Callbacks.SetArrayHandler(callback);
        }

        /// <summary>
        /// Add handler for IPacket type receve
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="callback"></param>
        public void SetHandler<T>(Action<SyncIOConnectedClient, T> callback) where T : class, IPacket {
            Callbacks.SetHandler<T>(callback);
        }

        /// <summary>
        /// Add handler for all IPacket packets.
        /// If another handler is raised for the type of IPacket, this callback will not be called for it.
        /// </summary>
        /// <param name="callback"></param>
        public void SetHandler(Action<SyncIOConnectedClient, IPacket> callback) {
            Callbacks.SetPacketHandler(callback);
        }

        /// <summary>
        /// Makes a function callable to clients
        /// </summary>
        /// <param name="name">Function name</param>
        /// <param name="func">function to call</param>
        /// <returns></returns>
        public RemoteFunctionBind RegisterRemoteFunction(string name, Delegate func) {
            return RemoteFuncs.BindRemoteCall(name, func);
        }

        public void SetDefaultRemoteFunctionAuthCallback(RemoteFunctionCallAuth _DefaultAuthCallback) {
            RemoteFuncs.SetDefaultAuthCallback(_DefaultAuthCallback);
        }

        public SyncIOSocket this[int port] {
            get {
                return OpenSockets.FirstOrDefault(x => x.EndPoint.Port == port);
            }
        }

        public IEnumerator<SyncIOSocket> GetEnumerator() {
            return OpenSockets.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return OpenSockets.GetEnumerator();
        }
    }
}