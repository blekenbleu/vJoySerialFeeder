/*
 * Created by SharpDevelop.
 * User: Cleric
 * Date: 19.3.2018 г.
 * Time: 17:42 ч.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis; // CA2202

namespace vJoySerialFeeder
{
	/// <summary>
	/// Implementing interaction with vJoySerialFeeder via WebSocket
	/// 
	/// The implementation of WebSocket is minimal, but compliant with RFC6455
	/// (https://tools.ietf.org/html/rfc6455#section-7.1.1),
	/// with the following exceptions:
	///  * no fragmented messages
	///  * no packets longer than 125 bytes
	///  * no subprotocols, no extensions
	/// 
	/// vJoySerialFeeder acts as server. It accepts simple commands - see onMessage() for details
	/// Response is in JSON format: {"mapping": .., "input": .., "output": ..}
	/// </summary>
	class WebSocket {
		
		enum SubscriptionTypes { Input, Output, Both }
		/// <summary>
		/// Encapsulates a subscription for Mapping updates
		/// </summary>
		class Subscription {
			internal readonly int mappingIndex;
			internal SubscriptionTypes type;
			
			int? Input;
			float? Output;
			
			internal Subscription(int i, SubscriptionTypes type) {
				mappingIndex = i;
				this.type = type;
			}
			
			/// <summary>
			/// Updates the input/output cache and returns true if there
			/// were changes
			/// </summary>
			/// <param name="Input"></param>
			/// <param name="Output"></param>
			/// <returns></returns>
			internal bool Update(int input, float output) {
				bool upd = (type == SubscriptionTypes.Input || type == SubscriptionTypes.Both) && this.Input != input
					|| (type == SubscriptionTypes.Output || type == SubscriptionTypes.Both) && this.Output != output;

				this.Input = input;
				this.Output = output;
				return upd;
			}
		}
		
		public int Port { get; private set; }

		static readonly char[] HEADER_SEP = new char[] {':'};
		
		const byte OP_TEXT = 0x1;
		const byte OP_PING = 0x9;
		const byte OP_PONG = 0xa;
		const byte OP_CLOSE = 0x8;

		
		TcpListener listener;
        static readonly object _locker = new object();
		Dictionary<Socket, List<Subscription>> subscriptions = new Dictionary<Socket, List<WebSocket.Subscription>>();
		List<Socket> deadSockets = new List<Socket>();
		bool started;
		
		public WebSocket(int port) {
			Port = port;
			listener = new TcpListener(IPAddress.Any, port);
			listener.Start();
			listener.BeginAcceptSocket(AcceptConnection, null);
			
			started = true;
		}
		
		public void Stop() {
			if(!started)
				return;
			
			listener.Stop();
			started = false;
		}
	
		void AcceptConnection(IAsyncResult ar) {
			// accept another
			Socket sock = null;
			
			try {
				// accept another connection asynchronously
				sock = listener.EndAcceptSocket(ar);
                sock.Blocking = true;
				
				listener.BeginAcceptSocket(AcceptConnection, null);
				
				if(Negotiate(sock))
					ReceiveLoop(sock);
			}
			catch(Exception e) {
				System.Diagnostics.Debug.WriteLine(e.Message);
				if(sock != null) {
					RemoveSubscriptionsForSocket(sock);
					try {
						sock.Close();
					}
					catch(Exception) {}
				}
			}
			
		}

        /// <summary>
        /// Perform minimal WebSocket negotiation
        /// </summary>
        /// <param name="sock"></param>
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        bool Negotiate(Socket sock) {
			string sec = null;
            bool ok = false;
            NetworkStream ns = null;

            try
            {
                ns = new NetworkStream(sock);
                try
                {

                    using (var tr = new StreamReader(ns))
                    {
                        string s;
                        while (!(s = tr.ReadLine()).Equals(string.Empty))
                        {
                            // read HTTP headers
                            var header = s.Split(HEADER_SEP, 2);
                            if (header[0].ToLower().Equals("sec-websocket-key"))
                                sec = header[1].Trim();
                        }

                        if (sec == null)
                        {
                            sock.Close();
                        }
                        else    // valid WebSocket request
                        {
                            sec += "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                            sec = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(sec)));

                            using (var tw = new StreamWriter(ns))
                            {
                                tw.Write("HTTP/1.1 101 Switching Protocols\r\n");
                                tw.Write("Upgrade: websocket\r\n");
                                tw.Write("Connection: Upgrade\r\n");
                                tw.Write("Sec-WebSocket-Accept: " + sec + "\r\n");
                                tw.Write("\r\n");
                                tw.Flush();
                            }

                            ok = true;
                        }
                    }   // To avoid CA2202 here probably requires overriding StreamWriter.Dispose
                }
                finally
                {
                    ns = null;
                }
            }
            finally
            {
                if (ns != null)
                    ns.Dispose();
            }
            return ok;
		}
		
		/// <summary>
		/// This is the receive loop. It reads from the socket and interprets
		/// the data as websocket frames.
		/// </summary>
		/// <param name="sock"></param>
		void ReceiveLoop(Socket sock) {
			var buf = new byte[256];
			sock.ReceiveTimeout = 1000;
			
			while(true) {
				try {
	        		if(sock.Receive(buf, 2, SocketFlags.None) != 2)
	        			throw new Exception("Not enough data");
	        		
	        		// check FIN bit
	        		if((buf[0] & 0x80) == 0)
	        			throw new Exception("Fragmented frames not supported");
	        		
	        		// check MASK bit
	        		if((buf[1] & 0x80) == 0)
	        			throw new Exception("Client frames must be masked");
	        		
	        		// get length
	        		var len = buf[1] & 0x7f;
	        		if(len > 125)
	        			throw new Exception("Unsupported length");
	        		
	        		var opcode = buf[0] & 0xF; 
	        		
	        		var fullLen = len + 4; // 4 mask bytes
	        		if(sock.Receive(buf, fullLen, SocketFlags.None) != fullLen)
	        			throw new Exception("Not enough data");
	        		
	        		// unmask 
					for(var i = 0; i<len; i++)
						buf[4+i] ^= buf[i%4];
	        		
	        		switch(opcode) {
	        			case OP_TEXT:
	    					var msg = Encoding.UTF8.GetString(buf, 4, len);
	    					OnMessage(sock, msg);
	        				
							break;
							
	        			case OP_PING:
	    					// ping
	    					// data to send with pong is the same as the one in buf,
	    					// just prepare the two header bytes
	    					buf[2] = 0x80 | OP_PONG;
							buf[3] = (byte)len;
							sock.Send(buf, 2, len+2, SocketFlags.None);
	        				
	        				break;
	        				
	        			case OP_CLOSE:
	        				// close frame
	        				sock.Close();
	        				return;
	        				
	        			default:
	        				throw new Exception("unsupported opcode "+opcode);
	        		}
				}
				catch(SocketException ex) {
					if(ex.SocketErrorCode == SocketError.TimedOut
                       // linux seems to return 'WouldBlock'
                       || ex.SocketErrorCode == SocketError.WouldBlock) {
						if(!started)
							// WebSocket has been disabled
							throw new Exception("Closing connection - WebSocket has been disabled");
					}
					else
						throw;
				}
        	}
		}
		
		
		
		
		
		
		
		
		
		
		
		/// <summary>
		/// Can be called from any thread. Sends websocket messages for all
		/// subscriptions whose values have changed.
		/// </summary>
		public void  Dispatch() {
			lock(subscriptions) {
				foreach(var sock in subscriptions.Keys) {
					var l = subscriptions[sock];
					
					foreach(var sub in l) {
						var m = MainForm.Instance.MappingAt(sub.mappingIndex);
						
						if(m == null)
							// the mapping does not exists at this point
							continue;
						
						if(sub.Update(m.Input, m.Output)) {
							try {
								SendMappingData(sock, sub.mappingIndex+1, m);
							}
							catch(Exception e) {
								System.Diagnostics.Debug.WriteLine(e.Message);
								deadSockets.Add(sock);
								break;
							}
						}
					}
				}
				
				if(deadSockets.Count > 0) {
					foreach(var sock in deadSockets)
						RemoveSubscriptionsForSocket(sock);
					
					deadSockets.Clear();
				}
			}
		}
		
		
		/// <summary>
		/// Parses and executes commands coming from clients.
		/// 
		/// See the switch() below
		/// 
		/// </summary>
		/// <param name="sock"></param>
		/// <param name="msg"></param>
		void OnMessage(Socket sock, string msg) {
			try {
				var parts = msg.ToLower().Split(null);
				
				if(parts.Length < 1)
					throw new Exception("Bad command");
				
				switch(parts[0]) {
					case "get":
						// get MAPPING_ID
						//
						// requests json message with the values of mapping MAPPING_ID
						// all MAPPING_IDs start from 1
						
						AssertPartsNum(parts, 2);
						var idx = ParseInt(parts, 1);
						var m = GetMapping(idx);
						
						SendMappingData(sock, idx, m);
						
						break;
						
					case "set":
						// set (input|output) MAPPING_ID VALUE
						//
						// sets the Input or Output of mapping MAPPING_ID to VALUE
						// If you are setting the Input, the Output of the mapping will
						// be updated automatically.
						
						AssertPartsNum(parts, 4);
						idx = ParseInt(parts, 2);
						m = GetMapping(idx);
						
						switch(parts[1]) {
							case "input":
								m.Input = ParseInt(parts, 3);
								break;
							case "output":
								m.Output = ParseFloat(parts, 3);
								break;
							default:
								throw new Exception("Expected 'input' or 'output' after 'set' command");
						}
						
						break;
						
					case "sub":
						// sub (input|output|both) MAPPING_ID
						//
						// subscribes for changes in the Input, Output of both
						// of mapping MAPPNIGS_ID
						//
						// Subscriptions cause messages to be received upon changes, which 
						// have the same format as the ones received from the `get` command
						
						AssertPartsNum(parts, 3);
						idx = ParseInt(parts, 2);
						m = GetMapping(idx);
						
						switch(parts[1]) {
			            	case "input":
								Subscribe(sock, SubscriptionTypes.Input, idx);
								break;
							case "output":
								Subscribe(sock, SubscriptionTypes.Output, idx);
								break;
							case "both":
								Subscribe(sock, SubscriptionTypes.Both, idx);
								break;
						    default:
								throw new Exception("Expected 'input', 'output' or 'both' after 'set' command");
			           	}
						
						break;
						
					case "unsub":
						// unsub MAPPING_ID
						//
						// unsubscribes to any changes of mapping MAPPING_ID
						
						AssertPartsNum(parts, 2);
						idx = ParseInt(parts, 1);
						Unsubscribe(sock, idx);
						break;
						
					default:
						throw new Exception("Unknown command");
						
				}
			}
			catch(Exception e) {
				SendError(sock, e.Message);
			}
		}
		
		void AssertPartsNum(string[] parts, int n) {
			if(parts.Length != n)
				throw new Exception("Incorrect number of arguments");
		}
		
		int ParseInt(string[] parts, int idx) {
            if (!int.TryParse(parts[idx], out int i))
                throw new Exception("Bad number supplied for argument " + (idx + 2));
            return i;
		}
		
		float ParseFloat(string[] parts, int idx) {
            if (!float.TryParse(parts[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                throw new Exception("Bad number supplied for argument " + (idx + 2));
            return f;
		}
		
		Mapping GetMapping(int idx) {
			var m = MainForm.Instance.MappingAt(idx - 1);
			if(m == null)
				throw new Exception("There is no mapping "+idx);
			return m;
		}

		
		void SendError(Socket sock, String msg) {
			SendMessage(sock, "{\"error\":\""+msg.Replace("\"", "\\\"")+"\"}");
		}
		
		void SendMappingData(Socket sock, int mi, Mapping m) {
			SendMessage(sock, "{\"mapping\":"+mi+",\"input\":"+m.Input
			            +(MainForm.Instance.Failsafe ? ",\"failsafe\":true" : "")
			            +",\"output\":"+m.Output.ToString(CultureInfo.InvariantCulture)+"}");
		}
		
		void SendMessage(Socket sock, string msg) {
            byte[] sendBuf = new byte[256];

            lock (_locker) {
                var slen = Encoding.UTF8.GetBytes(msg, 0, msg.Length, sendBuf, 2);
				sendBuf[0] = 0x81;
				sendBuf[1] = (byte)slen;
				sock.Send(sendBuf, slen+2, SocketFlags.None);
			}
		}
		
		void RemoveSubscriptionsForSocket(Socket sock) {
			lock(subscriptions) {
				if(subscriptions.ContainsKey(sock))
				   subscriptions.Remove(sock);
			}
		}
		
		void Subscribe(Socket sock, SubscriptionTypes type, int mIdx){
			mIdx--; // to zero-based index
			
			lock(subscriptions) {
				var m = MainForm.Instance.MappingAt(mIdx);
				if(m == null)
					throw new Exception("No such mapping");
				
				if(!subscriptions.ContainsKey(sock))
					subscriptions[sock] = new List<Subscription>();
				var l = subscriptions[sock];
				
				foreach(var sub in l) {
					if(sub.mappingIndex == mIdx) {
						// already subscribed
						if(sub.type != type)
							// extend the subscription
							sub.type = SubscriptionTypes.Both;
						return;
					}
				}
				
				l.Add(new Subscription(mIdx, type));
			}
		               	
		}
		
		void Unsubscribe(Socket sock, int mIdx) {
			mIdx--; // to zero-based index
			
			lock(subscriptions) {
				if(subscriptions.ContainsKey(sock)) {
					var l = subscriptions[sock];
					for(var i=0; i<l.Count; i++) {
						if(l[i].mappingIndex == mIdx) {
							l.RemoveAt(i);
							break; // there is only one sub for mIdx
						}
					}
				}
			}
		}
	}
}
