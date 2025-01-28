using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Planetary {

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

        void Awake()
        {
            foreach (GameObject pf in Prefabs) {
                PPEntity sse = pf.GetComponent<PPEntity>();
                if (sse == null) {
                    Debug.LogError("Prefab lacks Entity component", pf);
                }
                PrefabMap[sse.Type] = pf;
            }
            sdk = new SDK(GameID, HandleChunk);
            Player.GetComponent<PPEntity>().Master = this;
        }

        public void Init(string username, string password) {
            sdk.Connect(username, password);
        }

        public void Join() {
            if (!sdk.IsConnected()) {
                Debug.LogError("must connect before joining");
            }
            sdk.Join();
        }

        private void HandleChunk(Chunk cnk) {
            if (ChunkPrefab == null) return;
            if (Chunks.ContainsKey(cnk.id)) {
                PPChunk ppchunk = Chunks[cnk.id].GetComponent<PPChunk>();
                ppchunk.data = cnk.data;
            } else {
                GameObject go = Instantiate(ChunkPrefab);
                go.transform.position = new Vector3(cnk.x*ChunkSize, 0, cnk.y*ChunkSize);
                PPChunk ppchunk = go.GetComponent<PPChunk>();
                ppchunk.data = cnk.data;
                ppchunk.id = cnk.id;
                ppchunk.x = cnk.x;
                ppchunk.y = cnk.y;
                Chunks[cnk.id] = go;
            }
            List<ulong> toRemove = new List<ulong>();
            foreach ((ulong id, GameObject other) in Chunks) {
                PPChunk ppchunk = other.GetComponent<PPChunk>();
                if (Mathf.Abs(ppchunk.x-cnk.x) > 3 || Mathf.Abs(ppchunk.y-cnk.y) > 3) {
                    Destroy(other);
                    toRemove.Add(id);
                }
            }
            foreach (ulong id in toRemove) {
                Chunks.Remove(id);
            }
        }

        void FixedUpdate()
        {
            if (!sdk.IsConnected()) {
                return;
            }
            
            sdk.Update();
            foreach ((string uuid, Entity e) in sdk.entities) {
                if (!Entities.ContainsKey(uuid)) {
                    GameObject g;
                    if (sdk.UUID == uuid) { 
                        g = Player;
                    } else if (PrefabMap.ContainsKey(e.type)) {
                        g = Instantiate(PrefabMap[e.type]);
                    } else {
                        continue;
                    }
                    Entities[uuid] = g;
                    PPEntity ppe = g.GetComponent<PPEntity>();
                    ppe.UUID = uuid;
                    ppe.Master = this;
                }
            }
            
            List<string> toRemove = new List<string>();
            foreach ((string uuid, GameObject e) in Entities) {
                if (!sdk.entities.ContainsKey(uuid)) {
                    if (uuid != sdk.UUID) {
                        Destroy(e);
                    }
                    toRemove.Add(uuid);
                }
            }

            foreach (string uuid in toRemove) {
                Entities.Remove(uuid);
            }
        }

        public Entity GetEntity(string uuid) {
            if (!sdk.entities.ContainsKey(uuid)) {
                return null;
            }
            return sdk.entities[uuid];
        }

        public List<Entity> GetEntities(string uuid) {
            return new List<Entity>(sdk.entities.Values);
        }

        public void Message(Dictionary<string, object> msg) {
            if (!sdk.IsConnected()) {
                return;
            }
            sdk.Message(msg);
        }
    }

}