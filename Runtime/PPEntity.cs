using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Planetary {

    [AddComponentMenu("PP/Entity")]
    public class PPEntity : MonoBehaviour
    {
        protected Entity entity;
        public string Type;
        public bool useServerPosition = true;
        [SerializeField] public bool useSync = false; // Checkbox to enable sync instead of spawn
        [SerializeField] public string SyncID = ""; // New field for matching
        
        private bool synced = false; // Internal flag to prevent re-syncing

        internal PPMaster Master;
        internal string UUID  = "";


        protected void Start()
        {
            Debug.Log("Start: ");
            Debug.Log(Type);
            entity = Master.GetEntity(UUID);
            
            updatePosition();
        }

        protected void FixedUpdate()
        {
            entity = Master.GetEntity(UUID);
            if (useServerPosition) { updatePosition(); };
        }

        private void updatePosition() {
            if (entity != null) {
                Vector3 pos = GetServerPosition();
                if (Master.TwoDimensions) {
                   transform.position = new Vector3(pos.x, pos.y, pos.z);
                } else {
                   transform.position = new Vector3(pos.x, pos.z, pos.y);
                }
            }
        }

        public string GetUUID() {
            return UUID;
        }

        public Vector3 GetServerPosition() {
            if (entity == null) {
                return new Vector3();
            }
            return new Vector3((float)entity.x, (float)entity.y, (float)entity.z);
        }

        public Dictionary<string, object> GetServerData() {
            if (entity == null) {
                return new Dictionary<string, object>();
            }
            return entity.data;
        }
    }
}