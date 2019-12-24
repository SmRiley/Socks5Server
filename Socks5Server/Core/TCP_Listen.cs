﻿using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Socks5Server;

namespace Socks5Server.Core
{
    class TCP_Listen
    {
        private int Data_Size = 1024;
        private byte[] Receive_Data;
        int TimeOut =1000 * 5;
        bool UDP_Support = true;


        public TCP_Listen(IPAddress IP,int Port) {
            Receive_Data = new byte[Data_Size];
            TcpListener Socks_Server = new TcpListener(IP, Port);
            Program.PrintLog(string.Format("Socks服务已启动,监听{0}端口中", Port));
            Socks_Server.Start();
            Socks_Server.BeginAcceptTcpClient(AcceptTcpClient,Socks_Server);
        }

        private void AcceptTcpClient(IAsyncResult ar) {
            TcpListener Socks_Server = ar.AsyncState as TcpListener;
            TcpClient Tcp_Client = Socks_Server.EndAcceptTcpClient(ar);
            //超时设置
            Tcp_Client.ReceiveTimeout = Tcp_Client.SendTimeout = TimeOut;
            Task task = new Task(()=> {
                TCP_Connect(Tcp_Client);
            });
            task.Start();
            Socks_Server.BeginAcceptTcpClient(new AsyncCallback(AcceptTcpClient),Socks_Server);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="tcpClient">TCPClient</param>
        /// <param name="Data">数据</param>
        private void TCP_Send(TcpClient tcpClient,byte[] Data) {
            tcpClient.GetStream().Write(Data);
        }

        private void TCP_Connect(object state) {
            TcpClient Tcp_Client = state as TcpClient;
            Program.PrintLog(string.Format("接收来自{0},当前线程数:{1}",Tcp_Client.Client.RemoteEndPoint,ThreadPool.ThreadCount));
            NetworkStream TCP_Stream = Tcp_Client.GetStream();
            var State_VT = (Tcp_Client, TCP_Stream);
            TCP_Stream.BeginRead(Receive_Data,0,Data_Size,new AsyncCallback(TCP_Receive),State_VT);
        }

        /// <summary>
        /// 读取回调
        /// </summary>
        /// <param name="ar"></param>
        private void TCP_Receive(IAsyncResult ar) {
            var State_Vt = (((TcpClient Tcp_Client, NetworkStream TCP_Stream))ar.AsyncState);
            try
            {
                int size = State_Vt.TCP_Stream.EndRead(ar);
                if (size > 0)
                {
                    byte[] Methods = Datahandle.Get_Checking_Method(Receive_Data.Take(size).ToArray());
                    int Data_Type = Datahandle.Get_Which_Type(Receive_Data.Take(size).ToArray());
                    //请求建立连接

                    if (Methods.Contains((byte)0))
                    {
                        TCP_Send(State_Vt.Tcp_Client, Datahandle.No_Authentication_Required);
                        Program.PrintLog("等待接受代理目标信息");
                        State_Vt.TCP_Stream.BeginRead(Receive_Data, 0, Data_Size, new AsyncCallback(TCP_Receive), State_Vt);
                    }
                    //接受代理目标端信息
                    else if (1 < Data_Type && Data_Type < 8)
                    {
                        var Request_Info = Datahandle.Get_Request_Info(Receive_Data.Take(size).ToArray());
                        if (Request_Info.type == 1)
                        {
                            //TCP
                            TcpClient Tcp_Proxy = Datahandle.Connecte_TCP(Request_Info.IP, Request_Info.port);
                            if (Tcp_Proxy.Connected)
                            {
                                new Socks_Server(State_Vt.Tcp_Client, Tcp_Proxy);
                                TCP_Send(State_Vt.Tcp_Client, Datahandle.Proxy_Success);
                                Program.PrintLog("正式开启TCP代理隧道");
                            }
                            else
                            {
                                TCP_Send(State_Vt.Tcp_Client, Datahandle.Connect_Fail);
                                Close(State_Vt.Tcp_Client);
                            }
                        }
                        else if (Request_Info.type == 3) {
                            //UDP               
                            if (UDP_Support)
                            {

                                //得到客户端开放UDP端口
                                IPEndPoint ClientPoint = new IPEndPoint((State_Vt.Tcp_Client.Client.RemoteEndPoint as IPEndPoint).Address, Request_Info.port);
                                var Socks_Servers = new Socks_Server(ClientPoint, State_Vt.Tcp_Client);
                                var UDP_Client = Socks_Servers.UDP_Client;
                                if (UDP_Client != null)
                                {
                                    byte[] header = Datahandle.Get_UDP_Header(UDP_Client.Client.LocalEndPoint as IPEndPoint);
                                    TCP_Send(State_Vt.Tcp_Client, header);
                                    try
                                    {
                                        State_Vt.TCP_Stream.BeginRead(Receive_Data, 0, Data_Size, new AsyncCallback((IAsyncResult ar) =>
                                        {
                                            try
                                            {
                                                if (Receive_Data.Length == 0)
                                                {
                                                    Close(State_Vt.Tcp_Client, Socks_Servers);
                                                }
                                            }
                                            catch (Exception)
                                            {

                                            }
                                        }), State_Vt);
                                    }
                                    catch (Exception)
                                    {
                                        Close(State_Vt.Tcp_Client, Socks_Servers);
                                    }
                                }
                                else {
                                    Close(State_Vt.Tcp_Client);
                                }
                                             
                            }
                            else
                            {
                                Close(State_Vt.Tcp_Client);
                            }
                        }
                    }
                    
                }
                else
                {
                    Close(State_Vt.Tcp_Client);
                }
            }
            catch (Exception)
            {
                Close(State_Vt.Tcp_Client);
            }

        }



        /// <summary>
        /// 关闭客户端连接
        /// </summary>
        /// <param name="Tcp_Client">客户端TCPClient</param>
        private void Close(TcpClient Tcp_Client,Socks_Server socks_Servers = null)
        {
            try
            {
                if (Tcp_Client.Connected)
                {
                    Program.PrintLog(string.Format("已关闭客户端{0}的连接", Tcp_Client.Client.RemoteEndPoint));
                    Tcp_Client.GetStream().Close();
                    Tcp_Client.Close();
                }

                if (socks_Servers != null) {
                    socks_Servers.UDP_Client.Close();
                    socks_Servers = null;
                }
            }
            catch (SocketException){ 
            
            }
        }

    }
}
