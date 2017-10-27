﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;

public class NetworkPlayer : NetworkBehaviour {

    public TextMesh label;
    public float moveSpeed;

    [SyncVar]
    public ulong steamId;

    public override void OnStartServer()
    {
        base.OnStartServer();

        var id = GetComponent<NetworkIdentity>();

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
            var inputMovement = new Vector3( Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical") );
            transform.Translate(inputMovement*Time.deltaTime*moveSpeed, Space.World);
        }
      
        GetComponent<Rigidbody>().isKinematic = !hasAuthority;

        label.text = SteamFriends.GetFriendPersonaName(new CSteamID(steamId));

    }
}