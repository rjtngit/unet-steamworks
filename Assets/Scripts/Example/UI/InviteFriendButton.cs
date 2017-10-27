using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InviteFriendButton : MonoBehaviour {

    // Hooked up in Inspector 
    public void OnClick()
    {
        SteamNetworkManager.Instance.InviteFriendsToLobby();
    }
}
