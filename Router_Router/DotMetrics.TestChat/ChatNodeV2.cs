using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZeroMQ;
using FistCore.Common.Serialization;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace DotMetrics.TestChat
{
    public class ChatNodeV2
    {
        public static ZContext Context { get; private set; }

        public bool IsServer { get; private set; }
        public bool IsStarted { get; private set; }

        public static string Endpoint { get; private set; }

        private ZSocket activeSocket;

        public ChatNodeV2(bool isServer)
        {
            this.IsServer = isServer;
            if (ChatNodeV2.Context == null)
            {
                ChatNodeV2.Context = new ZContext();
                this._sendList = new List<ZFrame>();
                this._receiveList = new List<ZFrame>();
                this._output = new List<ZFrame>();
                this._concurrentOutput = new ConcurrentBag<ZFrame>();
            }
        }

        private List<ZFrame> _receiveList;
        private List<ZFrame> _sendList;
        private List<ZFrame> _output;
        private ConcurrentBag<ZFrame> _concurrentOutput;

        public void StartRouterClient(string endpoint)
        {
            using (var client = new ZSocket(ChatNodeV2.Context, ZSocketType.ROUTER))
            {
                ChatNodeV2.Endpoint = endpoint;
                // ID
                Random rnd = new Random();
                client.Identity = Encoding.UTF8.GetBytes("CLIENT " + "[" + rnd.Next(1, 9999) + "]"); //set rand client id

                client.Connect(Endpoint);
                activeSocket = client;

                ZError error;
                ZMessage incoming;

                //Stopwatch stopWatch = new Stopwatch();
                //stopWatch.Start();

                int requests = 0;
                while (true)
                {
                    using (var request = new ZMessage())
                    {
                        request.Add(new ZFrame(client.Identity));
                        request.Add(new ZFrame("Request " + (++requests) + " at " + DateTime.Now.ToLongTimeString()));
                        List<ZFrame> primljenePoruke = new List<ZFrame>();
                        primljenePoruke.AddRange(request);
                        _sendList.AddRange(primljenePoruke);

                        foreach (ZFrame item in _sendList)
                        {
                            _concurrentOutput.Add(item);
                        }
                        _sendList.Clear();

                        if (!client.Send(request, out error))
                        {
                            if (error == ZError.ETERM)
                                return;    // Interrupted
                            throw new ZException(error);
                        }
                    }
                    //stopWatch.Stop();
                }
            }
        }

        public void StartRouterServer(string endpoint)
        {
            Endpoint = endpoint;
            using (var router = new ZSocket(ChatNodeV2.Context, ZSocketType.ROUTER))
            {
                router.Bind(Endpoint);
                activeSocket = router;
                ZError error;
                ZMessage request;

                while (true)
                {
                    if (null == (request = router.ReceiveMessage(out error)))
                    {
                        if (error == ZError.ETERM)
                            return;    // Interrupted
                        throw new ZException(error);
                    }
                    IsStarted = true;
                    using (request)
                    {
                        // The DEALER socket gives us the reply envelope and message
                        string identity = request[1].ReadString();
                        string content = request[2].ReadString();

                        using (var received = new ZMessage())
                        {
                            received.Add(new ZFrame(identity));
                            received.Add(new ZFrame($"Server respose to [{content}] @ [{DateTime.Now.ToLongTimeString()}]"));

                            router.SendMore(received[0]);
                            router.SendMore(new ZFrame());
                            router.SendMore(received[1]);

                            List<ZFrame> primljenePoruke = new List<ZFrame>();
                            primljenePoruke.AddRange(received);
                            _receiveList.AddRange(primljenePoruke);

                            foreach (ZFrame item in _receiveList)
                            {
                                _output.Add(item);
                            }
                            _receiveList.Clear();

                            if (!router.Send(received, out error))
                            {
                                if (error == ZError.ETERM)
                                    return;    // Interrupted
                                throw new ZException(error);
                            }
                        }
                    }
                }
            }
        }

        public void Stop(string endpoint)
        {
            Endpoint = endpoint;

            if (this.IsStarted == true)
            {
                if (this.IsServer)
                {
                    try
                    {
                        this.activeSocket.Unbind(Endpoint);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message + "\n" + ex.StackTrace);
                    }
                    this.IsStarted = false;
                }
                else
                {
                    try
                    {
                        this.activeSocket.Disconnect(Endpoint);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message + "\n" + ex.StackTrace);
                    }
                    this.IsStarted = false;
                }
            }
        }

        public void InputTextFromUI(ZFrame inputMsg)
        {
            _sendList.Add(inputMsg);
        }

        public void OutputTextToUI(ZFrame outputMsg)
        {
            _concurrentOutput.Add(outputMsg);
        }

        public string RefreshOutput()
        {
            string chatText = "";
            //List<Message> sortedOutput = _output.OrderBy(item => item.Timestamp).ToList();
            foreach (ZFrame frame in _concurrentOutput) //Calling _sendList.ToList() copies the values of _sendList to a separate list at the start of the foreach.Nothing else has access to this list(it doesn't even have a variable name!), so nothing can modify it inside the loop.
            {//with concurrentBag [0] item is last frame added (not 1st like in list)
                chatText += frame + Environment.NewLine;
            }
            return chatText;
        }
    }
}