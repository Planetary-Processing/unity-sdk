using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using System.Threading;
using System.Threading.Channels;
using System.Net.WebSockets;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

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
    private Thread thread;
    private Action<Chunk> chunkCallback;
    private Channel<Packet> channel = Channel.CreateUnbounded<Packet>();
    private Mutex m = new Mutex();
    public readonly Dictionary<string, Entity> entities = new Dictionary<string, Entity>();


    private ClientWebSocket client;

    public SDK(ulong gameid, Action<Chunk> chunkCallback) {
      gameID = gameid;
      this.chunkCallback = chunkCallback;
    }

    public SDK(ulong gameid) {
      gameID = gameid;
    }

    public void Connect(string username, string password) {
      
      Thread t = new Thread(() => {
        Task t = init(username, password, gameID);
        t.Wait();
    });
      t.Start();
      t.Join();
    }

    private async Task init(string email, string password, ulong gameID) {
      try {
        client = new ClientWebSocket();

        // Setting the Origin header for authentication
        client.Options.SetRequestHeader("Origin", "https://planetaryprocessing.io\r\n");

        Console.WriteLine("confirm auth params:'"+ email+"', '"+ password+"', '" + gameID+"'");

        // First connection
        var websocketUri = "wss://planetaryprocessing.io/_ws";
        await client.ConnectAsync(new Uri(websocketUri), CancellationToken.None);
        connected = true;

        // Creating a Login message using Protobuf
        var login = new Login {
          GameID = gameID,
          Email = email,
          Password = password
        };
        // Serializing the Login message to a byte array & send to server
        Byte[] dat = login.ToByteArray();
        await client.SendAsync(new ArraySegment<byte>(dat), WebSocketMessageType.Text, true, CancellationToken.None);
        // Wait for response and get the UUID from the message
        byte[] buffer = new byte[1024 * 4];
        WebSocketReceiveResult result = await client.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Text) {
          var uuid = Login.Parser.ParseFrom(buffer.Take(result.Count).ToArray()); // retrieve login message ({"UUID" : "....."})
          UUID = uuid.UUID;
          if (string.IsNullOrEmpty(UUID))
            {
                throw new OperationCanceledException("Connection denied: game offline.");
            }
        }
        Console.WriteLine("Websocket connected & authenticated");
        thread = new Thread(new ThreadStart(recv));
        thread.Start();
        Thread.Sleep(1000); // buffer time for connection lag(?)
        
      } catch (Exception e) {
        Console.WriteLine("error" + e);
        connected = false;
        throw e;
      }

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

    public void Message(Dictionary<String, object> msg) {
      var s = JsonSerializer.Serialize(msg);
      send(new Packet{Arbitrary = s});
    }

    public void Logout() {
      send(new Packet{Leave = true});
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
      if (packet.Delete != null && packet.Delete.EntityID != UUID) {
        entities.Remove(packet.Delete.EntityID);
      }
      if (packet.Chunk != null) {
        if (chunkCallback != null) {
          chunkCallback.Invoke(new Chunk{
            id = packet.Chunk.ID,
            x = packet.Chunk.X,
            y = packet.Chunk.Y,
            data = decodeEvent(packet.Chunk.Data)
          });
        }
      }
    }

    private async void send(Packet packet) {
      if ( connected == false ) {
         throw new Exception("send called before connection is established");
      }
      m.WaitOne();
      try {
        await client.SendAsync(new ArraySegment<byte>(encodePacket(packet)), WebSocketMessageType.Text, true, CancellationToken.None);
        
      } catch (Exception e) {
        Console.WriteLine(e.ToString());
        connected = false;
      } finally {
        m.ReleaseMutex(); 
      }
    }


    // Thread for getting comms from server
    private async void recv() {
      try {
        byte[] buffer = new byte[1024 * 4096]; // 4MB is max size
        while (connected) {
          // Get comms 
          WebSocketReceiveResult result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

          // check if conn closed and break receive loop if so
          if (result.CloseStatus.HasValue)
          {
              Console.WriteLine("WebSocket Disconnected");
              await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged Close", CancellationToken.None);
              connected = false;
              break; // Exit the loop as the connection is closed
          } else { // cnxn still up
            try
              {
                string receivedMessage = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                Packet packet = decodePacket(receivedMessage);
                channel.Writer.TryWrite(packet);
            }
            catch (Exception e) {
              Console.WriteLine(e);
            }
          }
      
        }
      } catch (Exception e) {
        Console.WriteLine($"Error in receive thread: {e.ToString()}");
        connected = false;
      } finally {
        // On error, close cnxn properly
        if (client != null && (client.State == WebSocketState.Open || client.State == WebSocketState.CloseSent)) {
          await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing due to error", CancellationToken.None);
        }
      }
    }

    // Decodes a base64 encoded string into a Packet object
    private Packet decodePacket(string s) {
      Byte[] bts = System.Convert.FromBase64String(s);
      Packet pckt = Packet.Parser.ParseFrom(bts);
      return pckt;
    }

    // Encodes a Packet object into a base64 string with a newline character
    private Byte[] encodePacket(Packet p) {
      return Encoding.UTF8.GetBytes(
        System.Convert.ToBase64String(p.ToByteArray()) + "\n");
    }


    // Converts a JsonElement to a variant type (object)
    private object ConvertToVariant(JsonElement value) {
      switch (value.ValueKind) {
        case JsonValueKind.True:
          return true;
        case JsonValueKind.False:
          return false;
        case JsonValueKind.Number:
          return value.GetDouble();
        case JsonValueKind.String:
          return value.GetString();
        case JsonValueKind.Object:
          var gdDict = new Dictionary<object, object>();
          foreach (var kvp in value.EnumerateObject()) {
            gdDict[kvp.Name] = ConvertToVariant(kvp.Value);
          }
          return gdDict;
        case JsonValueKind.Array:
          var gdDict2 = new Dictionary<object, object>();
          int i = 1;
          foreach (var v in value.EnumerateArray()) {
            gdDict2[i] = ConvertToVariant(v);
            i++;
          }
          return gdDict2;
        default:
          return null;
          }
        }
        

    // Converts a dictionary of JsonElements to a dictionary of variant types (objects)
    private Dictionary<string, object> ConvertToVariantDictionary(Dictionary<string, JsonElement> dict) {
      var gdDict = new Dictionary<string, object>();
      foreach (var kvp in dict)
      {
        gdDict[kvp.Key] = ConvertToVariant(kvp.Value);
      }
      return gdDict;
    }

    // Decodes a JSON string into a dictionary of string to object
    private Dictionary<String, object> decodeEvent(string e) {
        return ConvertToVariantDictionary(JsonSerializer.Deserialize<Dictionary<String, JsonElement>>(e));
      }
    }
}
