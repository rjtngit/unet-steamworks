using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FindMatchButton : MonoBehaviour {

    // Hooked up in Inspector 
    public void OnClick()
    {
        SteamNetworkManager.Instance.FindMatch();
    }

    void Update()
    {
        var state = SteamNetworkManager.Instance.lobbyConnectionState;
        GetComponent<Button>().interactable = state != SteamNetworkManager.SessionConnectionState.CONNECTING && state != SteamNetworkManager.SessionConnectionState.CONNECTED;
    }
}
