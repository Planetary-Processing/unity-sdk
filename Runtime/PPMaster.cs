using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Planetary {

    [AddComponentMenu("PP/Master")]
    public class PPMaster : MonoBehaviour
    {
        private SDK sdk;
        public GameObject[] Prefabs;
        public GameObject Player;
        private Dictionary<string, GameObject> PrefabMap = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> Entities = new Dictionary<string, GameObject>();
        public ulong GameID;

        // Start is called before the first frame update
        void Start()
        {
            foreach (GameObject pf in Prefabs) {
                PPEntity sse = pf.GetComponent<PPEntity>();
                if (sse == null) {
                    Debug.LogError("Prefab lacks Entity component", pf);
                }
                PrefabMap[sse.Type] = pf;
            }
            sdk = new SDK(GameID);
            sdk.Connect("", "");
            sdk.Join();
            PPPlayer ppp = Player.GetComponent<PPPlayer>();
            ppp.Master = this;
        }

        // Update is called once per frame
        void Update()
        {
            sdk.Update();

            if (!sdk.IsConnected()) {
                return;
            }
            foreach ((string uuid, Entity e) in sdk.entities) {
                if (!Entities.ContainsKey(uuid)) {
                    if (sdk.UUID == uuid) { 
                        PPPlayer ppp = Player.GetComponent<PPPlayer>();
                        ppp.UUID = uuid;
                        Entities[sdk.UUID] = Player;
                    } else if (PrefabMap.ContainsKey(e.type)) {
                        GameObject g = Instantiate(PrefabMap[e.type]);
                        Entities[uuid] = g;
                        PPEntity ppe = g.GetComponent<PPEntity>();
                        ppe.UUID = uuid;
                        ppe.Master = this;
                    }
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

        internal void Message(Dictionary<string, dynamic> msg) {
            sdk.Message(msg);
        }
    }

}