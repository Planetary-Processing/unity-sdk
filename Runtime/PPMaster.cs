using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using UnityEngine.SceneManagement;

namespace Planetary
{
    [AddComponentMenu("PP/Master")]
    public class PPMaster : MonoBehaviour
    {
        private SDK sdk;
        public GameObject Player;
        public bool UseScenePlayer = false;
        public string ScenePlayerName = "";
        private GameObject playerInstance;

        public GameObject ChunkPrefab;
        public GameObject[] Prefabs;
        public string[] Scenes;

        private Dictionary<string, GameObject> PrefabMap = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> SceneMap = new Dictionary<string, GameObject>();
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

            foreach (GameObject pf in Prefabs) {
                PPEntity sse = pf.GetComponent<PPEntity>();
                if (sse == null)
                {
                    Debug.LogError("Prefab lacks Entity component", pf);
                    continue;
                }
                PrefabMap[sse.Type] = pf;
            }

            if (Scenes != null && Scenes.Length > 0)
            {
                foreach (var sceneName in Scenes)
                {
                    if (!string.IsNullOrEmpty(sceneName))
                    {
                        StartCoroutine(LoadSceneEntities(sceneName));
                    }
                }
            }

            if (ServerToClientObject) {
                var callbackComponent = ServerToClientObject.GetComponent<MonoBehaviour>();
                Action<Dictionary<String, object>> eventCallback = (Action<Dictionary<String, object>>)Delegate.CreateDelegate(
                    typeof(Action<Dictionary<String, object>>), callbackComponent, "ServerToClient");
                
                sdk = new SDK(GameID, HandleChunk, eventCallback);
            }
            else {
                sdk = new SDK(GameID, HandleChunk);
            }

            if (UseScenePlayer && !string.IsNullOrEmpty(ScenePlayerName)) {
                StartCoroutine(LoadPlayerFromScene());
            }
            else if (Player != null) {
                playerInstance = Player;
                var ppe = Player.GetComponent<PPEntity>();
                if (ppe != null) {
                    ppe.Master = this;
                }
                else {
                    Debug.LogWarning("Player prefab is missing PPEntity.");
                }
            } else {
                Debug.LogError("Player not assigned and scene player not enabled.");
            }
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
                    GameObject g = null;

                    if (sdk.UUID == uuid)
                    {
                        g = playerInstance != null ? playerInstance : Player;
                    }
                    else if (PrefabMap.ContainsKey(e.type))
                    {
                        g = Instantiate(PrefabMap[e.type]);
                    }
                    else if (SceneMap.ContainsKey(e.type))
                    {
                        g = Instantiate(SceneMap[e.type]);
                        g.SetActive(true);
                    }
                    else
                    {
                        Debug.LogWarning($"No prefab or scene entity found for type '{e.type}'");
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

        public void Message(Dictionary<string, object> msg) // using Arbitrary 
        {
            if (!sdk.IsConnected()) return;
            sdk.Message(msg);
        }

        public void Message(string uuid, Dictionary<string, object> msg) // using Message {string TargetUUID = 1; string Data = 2;}
        {
            if (!sdk.IsConnected()) return;
            sdk.DirectMessage(uuid, msg);
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

        private IEnumerator LoadSceneEntities(string sceneName)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            yield return asyncLoad;

            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (!loadedScene.IsValid())
            {
                Debug.LogError($"Failed to load entity scene '{sceneName}'. Make sure it's added to Build Settings.");
                yield break;
            }

            GameObject[] rootObjects = loadedScene.GetRootGameObjects();

            foreach (var obj in rootObjects)
            {
                var ppe = obj.GetComponent<PPEntity>();
                if (ppe != null)
                {
                    string type = ppe.Type;
                    if (!SceneMap.ContainsKey(type))
                    {
                        SceneMap[type] = obj;
                        obj.SetActive(false);
                        ppe.Master = this;
                    }
                    else
                    {
                        Debug.LogWarning($"Duplicate entity type '{type}' found in scene '{sceneName}'. Skipping.");
                    }
                }
            }
        }

        private IEnumerator LoadPlayerFromScene()
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(ScenePlayerName, LoadSceneMode.Additive);
            yield return asyncLoad;

            Scene loadedScene = SceneManager.GetSceneByName(ScenePlayerName);
            GameObject[] rootObjects = loadedScene.GetRootGameObjects();
            foreach (var obj in rootObjects)
            {
                if (obj.CompareTag("Player"))
                {
                    playerInstance = obj;
                    break;
                }
            }

            if (playerInstance != null)
            {
                var ppe = playerInstance.GetComponent<PPEntity>();
                if (ppe != null)
                {
                    ppe.Master = this;
                }
                else
                {
                    Debug.LogWarning("Scene player object is missing PPEntity component.");
                }
            }
            else
            {
                Debug.LogError("No player GameObject found in scene. Make sure it is tagged 'Player'.");
            }
        }
    }
}
