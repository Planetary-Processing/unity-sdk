using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Planetary {

    [AddComponentMenu("PP/Player")]
    public class PPPlayer : PPEntity
    {

        void Start() {
            Type = "player";
        }
        
    new void FixedUpdate()
        {
            if (entity == null) {
                Debug.Log("null entity");
            }
            base.FixedUpdate();
        }

        public void Message(Dictionary<string, dynamic> msg) {
            Master.Message(msg);
        }
    }

}