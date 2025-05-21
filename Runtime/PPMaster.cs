using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;


namespace Planetary
{
    [AddComponentMenu("PP/Master")]
    public class PPMaster : MonoBehaviour
    {
        private SDK sdk;
        public GameObject Player;
        public GameObject ChunkPrefab;
        public GameObject[] Prefabs;
        private Dictionary<string, GameObject> PrefabMap = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> Entities = new Dictionary<string, GameObject>();
        private Dictionary<ulong, GameObject> Chunks = new Dictionary<ulong, GameObject>();
        public ulong GameID;
        public uint ChunkSize;
        public bool TwoDimensions = false;
        public GameObject ServerToClientObject;
        private bool dcAlerted = false;

        void Awake()
        {
            Application.runInBackground = true;

            foreach (GameObject pf in Prefabs)
            {
                PPEntity sse = pf.GetComponent<PPEntity>();
                if (sse == null)
                {
                    Debug.LogError("Prefab lacks Entity component", pf);
                    continue;
                }
                PrefabMap[sse.Type] = pf;
            }

            if (ServerToClientObject)
            {
                var callbackComponent = ServerToClientObject.GetComponent<MonoBehaviour>();
                sdk = new SDK(GameID, HandleChunk, (Dictionary<string, object> evt) =>
                {
                    callbackComponent.Invoke("ServerToClient", 0f); // Adjust if needed
                });
            }
            else
            {
                sdk = new SDK(GameID, HandleChunk);
            }

            Player.GetComponent<PPEntity>().Master = this;
        }

        public void Init(string username, string password, float timeout = 5000f)
        {
            sdk.Connect(username, password);
            dcAlerted = false;
        }

        public void Join()
        {
            if (!sdk.IsConnected())
            {
                Debug.LogError("Joining failed. Client not connected to Planetary Processing servers.");
                return;
            }
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] join start");
            sdk.Join();
        }

        private void HandleChunk(Chunk cnk)
        {
            if (ChunkPrefab == null) return;

            if (Chunks.ContainsKey(cnk.id))
            {
                var ppchunk = Chunks[cnk.id].GetComponent<PPChunk>();
                ppchunk.data = cnk.data;
            }
            else
            {
                GameObject go = Instantiate(ChunkPrefab);
                go.transform.position = TwoDimensions ?
                    new Vector3(cnk.x * ChunkSize, cnk.y * ChunkSize, 0) :
                    new Vector3(cnk.x * ChunkSize, 0, cnk.y * ChunkSize);

                var ppchunk = go.GetComponent<PPChunk>();
                ppchunk.data = cnk.data;
                ppchunk.id = cnk.id;
                ppchunk.x = cnk.x;
                ppchunk.y = cnk.y;
                Chunks[cnk.id] = go;
            }

            List<ulong> toRemove = new List<ulong>();
            foreach (var kvp in Chunks)
            {
                var id = kvp.Key;
                var other = kvp.Value;
                var ppchunk = other.GetComponent<PPChunk>();
                if (Mathf.Abs(ppchunk.x - cnk.x) > 3 || Mathf.Abs(ppchunk.y - cnk.y) > 3)
                {
                    Destroy(other);
                    toRemove.Add(id);
                }
            }

            foreach (ulong id in toRemove)
            {
                Chunks.Remove(id);
            }
        }

        void FixedUpdate()
        {
            if (!sdk.IsConnected())
            {
                if (!dcAlerted)
                {
                    Debug.LogError("Connection to server lost");
                    dcAlerted = true;
                }
                return;
            }

            sdk.Update();

            foreach (var kvp in sdk.entities)
            {
                string uuid = kvp.Key;
                Entity e = kvp.Value;

                if (!Entities.ContainsKey(uuid))
                {
                    GameObject g;
                    if (sdk.UUID == uuid)
                    {
                        g = Player;
                    }
                    else if (PrefabMap.ContainsKey(e.type))
                    {
                        g = Instantiate(PrefabMap[e.type]);
                    }
                    else
                    {
                        continue;
                    }

                    Entities[uuid] = g;
                    PPEntity ppe = g.GetComponent<PPEntity>();
                    ppe.UUID = uuid;
                    ppe.Master = this;
                }
            }

            List<string> toRemove = new List<string>();
            foreach (var kvp in Entities)
            {
                string uuid = kvp.Key;
                GameObject e = kvp.Value;

                if (!sdk.entities.ContainsKey(uuid))
                {
                    if (uuid != sdk.UUID)
                    {
                        Destroy(e);
                    }
                    toRemove.Add(uuid);
                }
            }

            foreach (string uuid in toRemove)
            {
                Entities.Remove(uuid);
            }
        }

        public Entity GetEntity(string uuid)
        {
            Entity result;
            return sdk.entities.TryGetValue(uuid, out result) ? result : null;
        }

        public List<Entity> GetEntities()
        {
            return new List<Entity>(sdk.entities.Values);
        }

        public void Message(Dictionary<string, object> msg)
        {
            if (!sdk.IsConnected()) return;
            sdk.Message(msg);
        }

        public bool IsConnected()
        {
            return sdk.IsConnected();
        }

        void OnApplicationQuit()
        {
            if (sdk != null && sdk.IsConnected())
            {
                sdk.Logout();
                Debug.Log("Player Disconnected");
            }
        }
    }
}
