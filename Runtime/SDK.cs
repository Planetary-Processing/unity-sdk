using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using System.Threading;
using System.Threading.Channels;
using RC4Cryptography;
using System.Collections.Generic;
using System.IO;

namespace Planetary {
  public class Entity {
    public string id;
    public double x;
    public double y;
    public double z;
    public Dictionary<string, dynamic> data;
    public string dataJSON;
    public string type;
  }

  public class SDK {

    private ulong gameID;
    private bool connected = false;
    public string UUID;
    private NetworkStream stream = null;
    private StreamReader sr = null;
    private Thread thread;
    private Action<Dictionary<string, dynamic>> onEvent;
    private Channel<Packet> channel = Channel.CreateUnbounded<Packet>();
    private Mutex m = new Mutex();
    public readonly Dictionary<string, Entity> entities = new Dictionary<string, Entity>();
    private RC4 inp;
    private RC4 oup;

    public SDK(ulong gameid, string token, Action<Dictionary<string, dynamic>> callback) {
      gameID = gameid;
    }

    public SDK(ulong gameid) {
      gameID = gameid;
    }

    public void Connect(string username, string password) {
      UUID = init(username, password, gameID);
    }

    private string init(string email, string password, ulong gameID) {
      string uuid = "";
      try {
        var body = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {GameID = gameID, Username = email, Password = password})));
        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri("https://api.planetaryprocessing.io/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage response = client.PostAsync("/_api/golang.planetaryprocessing.io/apis/httputils/HTTPUtils/GetKey", body).Result;
        Byte[] key;
        if (response.IsSuccessStatusCode) {
          Dictionary<String, String> data = JsonSerializer.Deserialize<Dictionary<String, String>>(response.Content.ReadAsStringAsync().Result);
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
        thread = new Thread(new ThreadStart(recv));
        thread.Start();
        Thread.Sleep(1000);
        connected = true;
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
      send(new Packet{
        Join = new Position{X=0, Y=0, Z=0}
      });
    }

    public void Update() {
      Packet pckt;
      while (channel.Reader.TryRead(out pckt)) {
        handlePacket(pckt);
      }
    }

    public bool IsConnected() {
      return connected;
    }

    public void Message(Dictionary<String, dynamic> msg) {
      var s = JsonSerializer.Serialize(msg);
      send(new Packet{Arbitrary = s});
    }

    public void Logout() {
      send(new Packet{Leave = true});
    }

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
          entities.Add(packet.Update.EntityID, new Entity{
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
      if (packet.Delete != null) {
        entities.Remove(packet.Delete.EntityID);
      }
    }

    private void send(Packet packet) {
      if ( connected == false ) {
         throw new Exception("send called before connection is established");
      }
      m.WaitOne();
      // perhaps an automatic re-init would be useful here
      try {
        Byte[] bts = encodePacket(packet);
        stream.Write(bts, 0, bts.Length);
      } catch (Exception e) {
        Console.WriteLine(e.ToString());
        if (sr != null) {
          sr.Dispose();
        }
        connected = false;
      } finally {
        m.ReleaseMutex();
      }
    }

    private void recv() {
      try {
        while (true) {
          string line;
          while ((line = sr.ReadLine()) != null) {
            if (!channel.Writer.TryWrite(decodePacket(line))) {
              throw new Exception("failed to read packet");
            }
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

    private Dictionary<String, dynamic> decodeEvent(string e) {
      return JsonSerializer.Deserialize<Dictionary<String, dynamic>>(e);
    }
  }
}
