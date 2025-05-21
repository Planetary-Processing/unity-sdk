using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Planetary {

    [AddComponentMenu("PP/Chunk")]
    public class PPChunk : MonoBehaviour
    {
        internal PPMaster Master;
        internal ulong id;
        internal long x;
        internal long y;
        internal Dictionary<string, object> data;

        protected void Start()
        {
        }

        protected void FixedUpdate()
        {
        }

        public Dictionary<string, object> GetServerData() {
            return data;
        }
    }
}