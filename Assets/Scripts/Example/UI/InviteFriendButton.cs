using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InviteFriendButton : MonoBehaviour {

    public void Start()
    {
        StartCoroutine(DoDisableButton());
    }

    IEnumerator DoDisableButton()
    {
        // Disable button when connection starts
        while (
            UNETSteamworks.SteamNetworkManager.Instance.lobbyConnectionState != UNETSteamworks.SteamNetworkManager.SessionConnectionState.CONNECTING &&
            UNETSteamworks.SteamNetworkManager.Instance.lobbyConnectionState != UNETSteamworks.SteamNetworkManager.SessionConnectionState.CONNECTED
        )
        {
            yield return null;
        }

        GetComponent<Button>().interactable = false;
    }

    // Hooked up in Inspector 
    public void OnClick()
    {
        UNETSteamworks.SteamNetworkManager.Instance.CreateLobbyAndInviteFriend();
    }
}
