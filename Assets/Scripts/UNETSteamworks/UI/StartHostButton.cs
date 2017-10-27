using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StartHostButton : MonoBehaviour {

    public void Start()
    {
        StartCoroutine(DoDisableButton());
    }

    IEnumerator DoDisableButton()
    {
        // Disable button when connection starts
        while (
            UNETSteamworks.NetworkManager.Instance.lobbyConnectionState != UNETSteamworks.NetworkManager.SessionConnectionState.CONNECTING &&
            UNETSteamworks.NetworkManager.Instance.lobbyConnectionState != UNETSteamworks.NetworkManager.SessionConnectionState.CONNECTED
        )
        {
            yield return null;
        }

        GetComponent<Button>().interactable = false;
    }

    // Hooked up in Inspector 
    public void OnClick()
    {
        UNETSteamworks.NetworkManager.Instance.CreateLobbyAndInviteFriend();
    }
}
