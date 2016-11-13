//
// MessageLoop.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2008 Alan McGovern
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using DHTNet.BEncode;
using DHTNet.EventArgs;
using DHTNet.Listeners;
using DHTNet.Messages.Errors;
using DHTNet.Messages.Queries;
using DHTNet.Messages.Responses;
using DHTNet.MonoTorrent;
using DHTNet.Nodes;

namespace DHTNet
{
    internal class MessageLoop
    {
        private readonly List<IAsyncResult> _activeSends = new List<IAsyncResult>();
        private readonly DhtEngine _engine;
        private readonly DhtListener _listener;
        private readonly object _locker = new object();
        private readonly Queue<KeyValuePair<IPEndPoint, Message>> _receiveQueue = new Queue<KeyValuePair<IPEndPoint, Message>>();
        private readonly Queue<SendDetails> _sendQueue = new Queue<SendDetails>();
        private readonly List<SendDetails> _waitingResponse = new List<SendDetails>();
        private DateTime _lastSent;

        public MessageLoop(DhtEngine engine, DhtListener listener)
        {
            _engine = engine;
            _listener = listener;
            listener.MessageReceived += MessageReceived;
            DhtEngine.MainLoop.QueueTimeout(TimeSpan.FromMilliseconds(5), delegate
            {
                if (engine.Disposed)
                    return false;
                try
                {
                    SendMessage();
                    ReceiveMessage();
                    TimeoutMessage();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in DHT main loop:");
                    Debug.WriteLine(ex);
                }

                return !engine.Disposed;
            });
        }

        private bool CanSend => (_activeSends.Count < 5) && (_sendQueue.Count > 0) && (DateTime.Now - _lastSent > TimeSpan.FromMilliseconds(5));

        internal event EventHandler<SendQueryEventArgs> QuerySent;

        private void MessageReceived(byte[] buffer, IPEndPoint endpoint)
        {
            lock (_locker)
            {
                // I should check the IP address matches as well as the transaction id
                // FIXME: This should throw an exception if the message doesn't exist, we need to handle this
                // and return an error message (if that's what the spec allows)
                try
                {
                    Message message;
                    if (MessageFactory.TryDecodeMessage((BEncodedDictionary)BEncodedValue.Decode(buffer, 0, buffer.Length, false), out message))
                    {
                        Logger.Log("Received message " + message.GetType().Name + " from " + endpoint);
                        _receiveQueue.Enqueue(new KeyValuePair<IPEndPoint, Message>(endpoint, message));
                    }
                }
                catch (MessageException ex)
                {
                    Console.WriteLine("Message Exception: {0}", ex);
                    // Caused by bad transaction id usually - ignore
                }
                catch (Exception ex)
                {
                    Console.WriteLine("OMGZERS! {0}", ex);
                    //throw new Exception("IP:" + endpoint.Address.ToString() + "bad transaction:" + e.Message);
                }
            }
        }

        private void RaiseMessageSent(IPEndPoint endpoint, QueryMessage query, ResponseMessage response)
        {
            QuerySent?.Invoke(this, new SendQueryEventArgs(endpoint, query, response));
        }

        private void SendMessage()
        {
            SendDetails? send = null;
            if (CanSend)
                send = _sendQueue.Dequeue();

            if (send != null)
            {
                SendMessage(send.Value.Message, send.Value.Destination);
                SendDetails details = send.Value;
                details.SentAt = DateTime.UtcNow;
                if (details.Message is QueryMessage)
                    _waitingResponse.Add(details);
            }
        }

        internal void Start()
        {
            if (_listener.Status != ListenerStatus.Listening)
                _listener.Start();
        }

        internal void Stop()
        {
            if (_listener.Status != ListenerStatus.NotListening)
                _listener.Stop();
        }

        private void TimeoutMessage()
        {
            if (_waitingResponse.Count > 0)
                if (DateTime.UtcNow - _waitingResponse[0].SentAt > _engine.TimeOut)
                {
                    SendDetails details = _waitingResponse.TakeFirst();
                    MessageFactory.UnregisterSend((QueryMessage)details.Message);
                    RaiseMessageSent(details.Destination, (QueryMessage)details.Message, null);
                }
        }

        private void ReceiveMessage()
        {
            if (_receiveQueue.Count == 0)
                return;

            KeyValuePair<IPEndPoint, Message> receive = _receiveQueue.Dequeue();
            Message m = receive.Value;
            IPEndPoint source = receive.Key;
            for (int i = 0; i < _waitingResponse.Count; i++)
            {
                if (_waitingResponse[i].Message.TransactionId.Equals(m.TransactionId))
                    _waitingResponse.RemoveAt(i--);
            }

            try
            {
                Node node = _engine.RoutingTable.FindNode(m.Id);

                // What do i do with a null node?
                if (node == null)
                {
                    node = new Node(m.Id, source);
                    _engine.RoutingTable.Add(node);
                }
                node.Seen();
                m.Handle(_engine, node);
                ResponseMessage response = m as ResponseMessage;
                if (response != null)
                    RaiseMessageSent(node.EndPoint, response.Query, response);
            }
            catch (MessageException ex)
            {
                Console.WriteLine("Incoming message barfed: {0}", ex);
                // Normal operation (FIXME: do i need to send a response error message?) 
            }
            catch (Exception ex)
            {
                Console.WriteLine("Handle Error for message: {0}", ex);
                EnqueueSend(new ErrorMessage(ErrorCode.GenericError, "Misshandle received message!"), source);
            }
        }

        private void SendMessage(Message message, IPEndPoint endpoint)
        {
            _lastSent = DateTime.Now;
            byte[] buffer = message.Encode();
            Logger.Log("Sending message " + message.GetType().Name + " to " + endpoint);
            _listener.Send(buffer, endpoint);
        }

        internal void EnqueueSend(Message message, IPEndPoint endpoint)
        {
            lock (_locker)
            {
                if (message.TransactionId == null)
                {
                    if (message is ResponseMessage)
                        throw new ArgumentException("Message must have a transaction id");
                    do
                    {
                        message.TransactionId = TransactionId.NextId();
                    } while (MessageFactory.IsRegistered(message.TransactionId));
                }

                // We need to be able to cancel a query message if we time out waiting for a response
                if (message is QueryMessage)
                    MessageFactory.RegisterSend((QueryMessage)message);

                _sendQueue.Enqueue(new SendDetails(endpoint, message));
            }
        }

        internal void EnqueueSend(Message message, Node node)
        {
            EnqueueSend(message, node.EndPoint);
        }

        private struct SendDetails
        {
            public SendDetails(IPEndPoint destination, Message message)
            {
                Destination = destination;
                Message = message;
                SentAt = DateTime.MinValue;
            }

            public readonly IPEndPoint Destination;
            public readonly Message Message;
            public DateTime SentAt;
        }
    }
}