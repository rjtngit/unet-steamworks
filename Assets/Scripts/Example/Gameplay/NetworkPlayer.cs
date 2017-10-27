using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;
using UnityEngine.Networking.NetworkSystem;

public class NetworkPlayer : NetworkBehaviour {

    public GameObject bulletPrefab;
    public TextMesh label;
    public float moveSpeed;

    [SyncVar]
    public ulong steamId;

    public override void OnStartServer()
    {
        base.OnStartServer();

        StartCoroutine(SetNameWhenReady());
    }

    IEnumerator SetNameWhenReady()
    {
        // Wait for client to get authority, then retrieve the player's Steam ID
        var id = GetComponent<NetworkIdentity>();
        while (id.clientAuthorityOwner == null)
        {
            yield return null;
        }

        steamId = SteamNetworkManager.Instance.GetSteamIDForConnection(id.clientAuthorityOwner).m_SteamID;

    }

    void Update()
    {
        if (hasAuthority)
        {
            // Only allow input for client with authority 
            var input = new Vector3( Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical") );
            transform.Translate(input * Time.deltaTime*moveSpeed, Space.World);

            if (Input.GetButtonDown("Fire1"))
            {
                CmdFire();
            }
        }
      
        // Disable physics for peer objects
        GetComponent<Rigidbody>().isKinematic = !hasAuthority;

        // Update player name
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
