using System.Collections.Generic;
using UnityEngine;

namespace Planetary
{
    [AddComponentMenu("PP/Chunk")]
    public class PPChunk : MonoBehaviour
    {
        // It's recommended to initialize the dictionary to avoid null issues.
        internal PPMaster Master;
        internal ulong id;
        internal long x;
        internal long y;
        internal Dictionary<string, object> data = new Dictionary<string, object>();

        // Use 'private' explicitly unless inherited protected access is needed.
        private void Start()
        {
            // Initialization logic if needed
        }

        private void FixedUpdate()
        {
            // Physics update logic if needed
        }

        public Dictionary<string, object> GetServerData()
        {
            return data;
        }
    }
}
