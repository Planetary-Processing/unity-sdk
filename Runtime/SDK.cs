using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;  // Add this namespace for JObject
using Google.Protobuf;
using System.Threading;
//using System.Net.WebSockets;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using RC4Cryptography;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;

namespace Planetary {
    public class Entity {
        public string id;
        public double x;
        public double y;
        public double z;
        public Dictionary<string, object> data;
        public string dataJSON;
        public string type;
    }

    public class Chunk {
        public ulong id;
        public long x;
        public long y;
        public Dictionary<string, object> data;
    }

    public class SDK {

        private ulong gameID;
        private bool connected = false;
        public string UUID;
        private NetworkStream stream = null;
        private StreamReader sr = null;
        private Thread thread;
        private Action<Chunk> chunkCallback;
        private Action<Dictionary<String, object>> eventCallback;
        private ConcurrentQueue<Packet> packetQueue = new ConcurrentQueue<Packet>();

        private Mutex m = new Mutex();
        public readonly Dictionary<string, Entity> entities = new Dictionary<string, Entity>();
        private RC4 inp;
        private RC4 oup;

        private DateTime lastMessageReceived = DateTime.UtcNow;
        private const int timeoutSeconds = 10;

        public SDK(ulong gameid, Action<Chunk> chunkCallback, Action<Dictionary<String, object>> eventCallback) {
            gameID = gameid;
            this.chunkCallback = chunkCallback;
            this.eventCallback = eventCallback;
        }

        public SDK(ulong gameid, Action<Chunk> chunkCallback) {
            gameID = gameid;
            this.chunkCallback = chunkCallback;
        }

        public SDK(ulong gameid) {
            gameID = gameid;
        }

        public void Connect(string username, string password) {
            UUID = init(username, password, gameID);
        }

       
        private string init(string email, string password, ulong gameID) {
            string uuid = "";
            Console.SetOut(new StreamWriter("sdk_log2.txt") { AutoFlush = true });
            try {
                var jsonBody = JsonConvert.SerializeObject(new { GameID = gameID, Username = email, Password = password });
                var body = new StringContent(jsonBody, Encoding.UTF8, "application/json");                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://api.planetaryprocessing.io/");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = client.PostAsync("/_api/golang.planetaryprocessing.io/apis/httputils/HTTPUtils/GetKey", body).Result;
                Byte[] key;
                if (response.IsSuccessStatusCode) {
                    Dictionary<String, String> data = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content.ReadAsStringAsync().Result);

                    key = System.Convert.FromBase64String(data["Key"]);
                    uuid = data["UUID"];
                } else {
                    throw new Exception("countn't get key, auth request failed");
                }
                inp = new RC4(key);
                oup = new RC4(key);
                IPHostEntry ipHostInfo = Dns.GetHostEntry("planetaryprocessing.io");
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                Socket socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(ipAddress, 42);
                stream = new NetworkStream(socket);
                var login = new Login {
                    UUID = uuid,
                    GameID = gameID
                };
                Byte[] dat = encodeLogin(login);
                stream.Write(dat, 0, dat.Length);
                sr = new StreamReader(stream, Encoding.UTF8);
                string line = sr.ReadLine();
                Login resp = decodeLogin(line);
                if (resp.UUID != uuid) {
                    throw new Exception("auth failed");
                }
                Console.WriteLine("...");
                connected = true;
                thread = new Thread(new ThreadStart(recv));
                thread.Start();
                Thread.Sleep(1000);
                

            } catch (Exception e) {
                if (sr != null) {
                sr.Dispose();
                }
                connected = false;
                throw e;
            }
            return uuid;
            }


        public void Join() {
            send(new Packet {
                Join = new Position { X = 0, Y = 0, Z = 0 }
            });
        }

        public void Update() {
            while (packetQueue.TryDequeue(out var pckt)) {
              handlePacket(pckt);
            }
        }

        public bool IsConnected() {
            return connected;
        }

        public void Message(Dictionary<String, object> msg) {
            var s = JsonConvert.SerializeObject(msg);
            send(new Packet { Arbitrary = s });
        }

        public void DirectMessage(string uuid, Dictionary<string, object> msg)
        {
            string jsonData = JsonConvert.SerializeObject(msg);

            var message = new Message{ TargetUUID = uuid, Data = jsonData };
            send(new Packet { Message = message });
        }

        public void Logout()
        {
            send(new Packet { Leave = true });
            Console.WriteLine("Client disconnected from server");
            connected = false;
        }

        // Decodes and formats a packet coming
        private void handlePacket(Packet packet) {
            if (packet.Update != null) {
                Entity e = null;
                if (entities.TryGetValue(packet.Update.EntityID, out e)) {
                    e.x = packet.Update.X;
                    e.y = packet.Update.Y;
                    e.z = packet.Update.Z;
                    e.dataJSON = packet.Update.Data;
                    e.data = decodeEvent(packet.Update.Data);
                    e.type = packet.Update.Type;
                } else {
                    entities.Add(packet.Update.EntityID, new Entity {
                        id = packet.Update.EntityID,
                        x = packet.Update.X,
                        y = packet.Update.Y,
                        z = packet.Update.Z,
                        dataJSON = packet.Update.Data,
                        data = decodeEvent(packet.Update.Data),
                        type = packet.Update.Type
                    });
                }
            }
            if (packet.Delete != null && packet.Delete.EntityID != UUID) {
                entities.Remove(packet.Delete.EntityID);
            }
            if (packet.Chunk != null) {
                if (chunkCallback != null) {
                    chunkCallback.Invoke(new Chunk {
                        id = packet.Chunk.ID,
                        x = packet.Chunk.X,
                        y = packet.Chunk.Y,
                        data = decodeEvent(packet.Chunk.Data)
                    });
                }
            }
            if (!string.IsNullOrEmpty(packet.Event)) {
                eventCallback?.Invoke(decodeEvent(packet.Event));
            }
          Console.WriteLine("handling packet: finish");
        }

        private void send(Packet packet) {
            if (connected == false) {
                throw new Exception("send called before connection is established");
            }
            m.WaitOne();
            try {
                Console.WriteLine("Attempting Send");
                Byte[] bts = encodePacket(packet);
                stream.Write(bts, 0, bts.Length);
              Console.WriteLine("Send completed successfully.");
            } catch (Exception e) {
                Console.WriteLine("error in send!");
                Console.WriteLine(e.ToString());
                connected = false;
            } finally {
                m.ReleaseMutex();
            }
        }

        // Thread for getting comms from server
        private void recv() {
            try {
                while (connected) {
                    string line;
                    while ((line = sr.ReadLine()) != null) {
                        packetQueue.Enqueue(decodePacket(line));
                    }
                }
             } catch (Exception e) {
                Console.WriteLine(e.ToString());
                if (sr != null) {
                sr.Dispose();
                }
                connected = false;
            } 
        }

        private Login decodeLogin(string s) {
        Byte[] bts = System.Convert.FromBase64String(s);
        Login pckt = Login.Parser.ParseFrom(bts);
        return pckt;
        }

        private Packet decodePacket(string s) {
        Byte[] bts = System.Convert.FromBase64String(s);
        bts = inp.Apply(bts);
        Packet pckt = Packet.Parser.ParseFrom(bts);
        return pckt;
        }

        private Byte[] encodeLogin(Login l) {
        return Encoding.UTF8.GetBytes(
            System.Convert.ToBase64String(l.ToByteArray()) + "\n");
        }

        private Byte[] encodePacket(Packet p) {
        return Encoding.UTF8.GetBytes(
            System.Convert.ToBase64String(oup.Apply(p.ToByteArray())) + "\n");
        }

        // Converts a JToken to a variant type (object)
        private object ConvertToVariant(JToken value) {
            switch (value.Type) {
                case JTokenType.Boolean:
                    return value.ToObject<bool>();
                case JTokenType.Integer:
                    return value.ToObject<long>();    
                case JTokenType.Float:
                    return value.ToObject<double>();
                case JTokenType.String:
                    return value.ToObject<string>();
                case JTokenType.Object:
                    var gdDict = new Dictionary<object, object>();
                    foreach (var kvp in value.ToObject<JObject>().Properties()) {
                        gdDict[kvp.Name] = ConvertToVariant(kvp.Value);
                    }
                    return gdDict;
                case JTokenType.Array:
                    var gdDict2 = new Dictionary<object, object>();
                    int i = 1;
                    foreach (var v in value.ToObject<JArray>()) {
                        gdDict2[i] = ConvertToVariant(v);
                        i++;
                    }
                    return gdDict2;
                default:
                    return null;
            }
        }

        // Converts a dictionary of JTokens to a dictionary of variant types (objects)
        private Dictionary<string, object> ConvertToVariantDictionary(Dictionary<string, JToken> dict) {
            var gdDict = new Dictionary<string, object>();
            foreach (var kvp in dict) {
                gdDict[kvp.Key] = ConvertToVariant(kvp.Value);
            }
            return gdDict;
        }

        // Decodes a JSON string into a dictionary of string to object
        private Dictionary<string, object> decodeEvent(string e) {
            try {
                var jtokenDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(e);
                foreach (var kv in jtokenDict) {
                    Console.WriteLine($"  {kv.Key}: {kv.Value} (Type: {kv.Value.Type})");
                }

                var variantDict = ConvertToVariantDictionary(jtokenDict);
                foreach (var kv in variantDict) {
                    Console.WriteLine($"  {kv.Key}: {kv.Value} (Type: {kv.Value?.GetType().Name ?? "null"})");
                }

                return variantDict;
            }
            catch (Exception ex) {
                Console.WriteLine($"decodeEvent error: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }


        private void StartWatchdog() {
          new Thread(() => {
              while (connected) {
                  Thread.Sleep(5000); // check every 5s
                  if ((DateTime.UtcNow - lastMessageReceived).TotalSeconds > timeoutSeconds) {
                      Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Connection timeout detected.");
                      connected = false;
                      
                      break;
                  }
              }
          }).Start();
      }

    }

}
