using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;
using UnityEngine.Networking.NetworkSystem;

public class NetworkPlayer : NetworkBehaviour {

    const short RequestFire = 2002;

    public TextMesh label;
    public float moveSpeed;

    [SyncVar]
    public ulong steamId;

    public GameObject bulletPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();

        StartCoroutine(SetNameWhenReady());
    }

    IEnumerator SetNameWhenReady()
    {
        var id = GetComponent<NetworkIdentity>();

        while (id.clientAuthorityOwner == null)
        {
            yield return null;
        }

        steamId = UNETSteamworks.NetworkManager.Instance.GetSteamIDForConnection(id.clientAuthorityOwner).m_SteamID;

    }

    void Update()
    {
        if (hasAuthority)
        {
            var input = new Vector3( Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical") );
            transform.Translate(input * Time.deltaTime*moveSpeed, Space.World);

            if (Input.GetButtonDown("Fire1"))
            {
                CmdFire();
            }
        }
      
        GetComponent<Rigidbody>().isKinematic = !hasAuthority;

        label.text = SteamFriends.GetFriendPersonaName(new CSteamID(steamId));

    }
        

    [Command]
    public void CmdFire()
    {
        if (NetworkServer.active)
        {
            var bullet = GameObject.Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            NetworkServer.Spawn(bullet);
        }
    
    }


}
