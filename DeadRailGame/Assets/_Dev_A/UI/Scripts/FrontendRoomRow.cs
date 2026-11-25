using System;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

public sealed class FrontendRoomRow : MonoBehaviour
{
    public Text roomName, players, map, ping;
    public Button selectButton, joinButton;
    public Image highlight;
    public SessionInfo Session { get; private set; }
    public bool CanJoin => Session != null && Session.IsOpen && Session.PlayerCount < Session.MaxPlayers;

    public void Bind(SessionInfo session, Action<FrontendRoomRow> select, Action<FrontendRoomRow> join)
    {
        Session = session;
        roomName.text = session.Name;
        players.text = $"{session.PlayerCount} / {session.MaxPlayers}";
        map.text = session.Properties.TryGetValue("map", out SessionProperty value) && value.IsString
            ? FrontendLanguage.Translate(value) : "--";
        // Session discovery does not expose a per-room RTT. Never display fabricated latency.
        ping.text = "--";
        selectButton.onClick.RemoveAllListeners();
        joinButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => select(this));
        joinButton.onClick.AddListener(() => join(this));
        joinButton.interactable = CanJoin;
        SetSelected(false);
    }

    public void SetSelected(bool selected) => highlight.color = selected
        ? new Color(1f, 0.88f, 0.73f, 0.17f) : new Color(1f, 0.94f, 0.85f, 0.035f);
}
