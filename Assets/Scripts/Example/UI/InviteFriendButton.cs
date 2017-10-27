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
            SteamNetworkManager.Instance.lobbyConnectionState != SteamNetworkManager.SessionConnectionState.CONNECTING &&
            SteamNetworkManager.Instance.lobbyConnectionState != SteamNetworkManager.SessionConnectionState.CONNECTED
        )
        {
            yield return null;
        }

        GetComponent<Button>().interactable = false;
    }

    // Hooked up in Inspector 
    public void OnClick()
    {
        SteamNetworkManager.Instance.CreateLobbyAndInviteFriend();
    }
}
