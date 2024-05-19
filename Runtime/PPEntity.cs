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

        [HideInInspector] public PPMaster Master;
        [HideInInspector] public string UUID;


        // Start is called before the first frame update
        void Start()
        {
            entity = Master.GetEntity(UUID);
            updatePosition();
        }

        // Update is called once per frame
        protected void FixedUpdate()
        {
            entity = Master.GetEntity(UUID);
            if (useServerPosition) { updatePosition(); };
        }

        private void updatePosition() {
            if (entity != null) {
                transform.position = GetServerPosition();
            }
        }

        public Vector3 GetServerPosition() {
            if (entity == null) {
                return new Vector3();
            }
            return new Vector3((float)entity.x, (float)entity.z, (float)entity.y);
        }

        public Dictionary<string, dynamic> GetServerData() {
            if (entity == null) {
                return new Dictionary<string, dynamic>();
            }
            return entity.data;
        }

    }
}