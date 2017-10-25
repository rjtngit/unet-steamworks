using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkPlayer : NetworkBehaviour {

    public TextMesh label;
    public float moveSpeed;

    void Update()
    {
        var inputMovement = new Vector3( Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical") );
        transform.Translate(inputMovement*Time.deltaTime*moveSpeed, Space.World);
    }
}
