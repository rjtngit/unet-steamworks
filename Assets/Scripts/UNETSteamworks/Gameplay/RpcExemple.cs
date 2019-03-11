using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class RpcExemple : NetworkBehaviour {

	Vector3 startPos;
	Vector3 otherPos;
	
	void Start()
    {
		startPos = transform.position;
		otherPos = startPos;
		otherPos.x = -otherPos.x;

        StartCoroutine(RepeatRpcMove());
    }

	IEnumerator RepeatRpcMove() {
		while(isServer) {
			yield return new WaitForSeconds(1f);
			RpcMove();
		}
	}

	[ClientRpc]
	void RpcMove() {
		if (transform.position.x == startPos.x)	
			transform.position = otherPos;
		else
			transform.position = startPos;			
	}
}
