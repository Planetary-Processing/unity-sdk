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

        internal PPMaster Master;
        internal string UUID  = "";


        protected void Start()
        {
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
                transform.position = new Vector3(pos.x, pos.z, pos.y);
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

        public Dictionary<string, dynamic> GetServerData() {
            if (entity == null) {
                return new Dictionary<string, dynamic>();
            }
            return entity.data;
        }
    }
}