using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal sealed class FrontendLanguage
{
    private const string LanguagePreference = "DeadRail.Frontend.Language";
    internal static bool Korean => PlayerPrefs.GetInt(LanguagePreference, 0) == 1;
    private readonly List<Action> updates = new List<Action>();
    private readonly Font font;

    internal FrontendLanguage(Font font)
    {
        this.font = font;
    }

    internal static void Set(int option)
    {
        PlayerPrefs.SetInt(LanguagePreference, option == 1 ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static readonly Dictionary<string, string> KoreanText = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Play"] = "플레이",
        ["Settings"] = "설정",
        ["How to play"] = "플레이 방법",
        ["Keep the train running"] = "열차 운행 목표",
        ["Choose a role in the lobby. Check the routes on the radar and keep the train supplied with fuel and power between stops."] =
            "로비에서 역할을 고르세요. 팀원들과 레이더로 경로를 확인하고 연료와 전력을 확보해 다음 정차지까지 열차를 운행하세요.",
        ["Approach the junction"] = "분기점 통과",
        ["Read the speed sign with the searchlight. The driver matches that speed while the gunner hits the track switch with the roof harpoon to select a branch."] =
            "탐조등으로 표지판을 비춰 진입 속도를 읽으세요. 운전 담당은 속도를 맞추고, 전투 담당은 지붕의 작살로 선로 전환기를 맞혀 갈 길을 선택하세요.",
        ["Supply stop"] = "첫 정차지에서 보급하기",
        ["At the first stop, press F to pick up the fuel can. This brings zombies to the train. Guard the rear cargo door while the carrier steps aboard and presses F there to load it."] =
            "연료통 옆에서 F를 누르면 운반을 시작하고 좀비가 몰려옵니다. 운반 담당은 열차에 올라 뒤쪽 화물문에서 F로 연료통을 싣고, 나머지 팀원은 화물문을 지키세요.",
        ["Get everyone aboard"] = "팀원 구조와 재출발",
        ["Press F near a downed crewmate to revive them. If everyone goes down, the run ends. After the defense, regroup on the train so the driver can depart."] =
            "쓰러진 팀원 곁에서 F를 눌러 일으키세요. 전원이 쓰러지면 운행에 실패합니다. 방어를 마치면 모두 열차에 오르세요. 전원이 탑승해야 운전 담당이 출발할 수 있습니다.",
        ["Left click"] = "마우스 왼쪽",
        ["Toggle zoom"] = "확대 / 원래 시야",
        ["CREDITS"] = "제작진",
        ["Developer A  /  Network, systems & UI"] = "개발자 A  /  네트워크 · 시스템 · UI",
        ["Photon matchmaking, P1/P4 terminal and radar UI, blackout transitions and resource consumption."] =
            "Photon 룸 매치메이킹, P1/P4 터미널·레이더 UI, 정전 상태 전환과 글리치 연출, 자원 소모 로직",
        ["Developer B  /  Physics, action & animation"] = "개발자 B  /  물리 · 액션 · 애니메이션",
        ["Scrolling track, P2/P3 hitscan and harpoon, Animator and IK setup, battery pickup and drop physics."] =
            "트레드밀 맵 스크롤, P2/P3 히트스캔 사격·작살총, Animator·IK 세팅, 배터리 들기·놓기 물리",
        ["Lobby list"] = "방 목록",
        ["Create room"] = "방 만들기",
        ["Join room"] = "방 참가",
        ["Join"] = "참가",
        ["Back"] = "뒤로",
        ["ROOM NAME"] = "방 이름",
        ["PLAYERS"] = "인원",
        ["MAP"] = "맵",
        ["PING"] = "지연 시간",
        ["Depot"] = "차량기지",
        ["PLAYER COUNT"] = "플레이어 수",
        ["Enter room name..."] = "방 이름 입력...",
        ["2 or 4 players"] = "2인 또는 4인",
        ["Enter a room name to continue."] = "방 이름을 입력하세요.",
        ["Joining room"] = "방 참가 중",
        ["Cancel"] = "취소",
        ["Pause menu"] = "게임 메뉴",
        ["Drop / install battery"] = "배터리 내려놓기 / 사용",
        ["Close console / pause"] = "콘솔 닫기 / 일시정지",
        ["Continue"] = "계속",
        ["Leave game"] = "게임 떠나기",
        ["Leave the game?"] = "게임을 떠날까요?",
        ["Leaving as host ends the session for everyone."] = "호스트가 떠나면 모든 플레이어의 게임이 종료됩니다.",
        ["You will leave the current session and return to the title."] = "현재 게임에서 나가 타이틀 화면으로 돌아갑니다.",
        ["Could not leave the session. Please try again."] = "게임에서 나갈 수 없습니다. 다시 시도하세요.",
        ["Leaving game..."] = "게임에서 나가는 중...",
        ["Connecting to server..."] = "서버에 연결 중...",
        ["Connecting to lobby..."] = "로비에 연결 중...",
        ["No rooms found. Create a room to begin."] = "방이 없습니다. 방을 만들어 시작하세요.",
        ["Creating room..."] = "방을 만드는 중...",
        ["Joining room..."] = "방에 참가하는 중...",
        ["Loading game scene..."] = "게임을 불러오는 중...",
        ["Preparing game..."] = "게임 준비 중...",
        ["Preparing your player..."] = "플레이어 준비 중...",
        ["Entering game..."] = "게임에 입장하는 중...",
        ["Ready"] = "준비 완료",
        ["Cancelling..."] = "취소 중...",
        ["Disconnected. Refresh the lobby to reconnect."] = "연결이 끊겼습니다. 방 목록을 새로고침하세요.",
        ["Could not connect to the lobby. Please refresh to retry."] = "로비에 연결할 수 없습니다. 새로고침해 다시 시도하세요.",
        ["Could not join the room. Please try again."] = "방에 참가할 수 없습니다. 다시 시도하세요.",
        ["Could not connect to the room. Please try again."] = "방에 연결할 수 없습니다. 다시 시도하세요.",
        ["Choose a room for 2 or 4 players."] = "인원은 2인 또는 4인을 선택하세요.",
        ["This room is full. Choose another room."] = "방이 가득 찼습니다. 다른 방을 선택하세요.",
        ["This room is no longer available. Please refresh."] = "방을 찾을 수 없습니다. 방 목록을 새로고침하세요.",
        ["That room name is already in use. Choose another name."] = "이미 사용 중인 방 이름입니다. 다른 이름을 입력하세요.",
        ["LOBBY"] = "대기 로비",
        ["ROOM"] = "방",
        ["Waiting for players..."] = "플레이어를 기다리는 중...",
        ["Waiting for the host..."] = "호스트의 시작을 기다리는 중...",
        ["Click a free seat to select its role"] = "빈 카드를 눌러 역할을 선택하세요",
        ["Select seat"] = "자리 선택",
        ["You"] = "나",
        ["Host"] = "호스트",
        ["Player {0}"] = "플레이어 {0}",
        ["Driver"] = "운전",
        ["Searchlight"] = "탐조등",
        ["Gunner"] = "전투",
        ["Radar"] = "레이더",
        ["Driver / Radar"] = "운전 / 레이더",
        ["Searchlight / Gunner"] = "탐조등 / 전투",
        ["CHAT"] = "채팅",
        ["Type a message..."] = "메시지 입력...",
        ["[System] Room created. Waiting for players..."] = "[시스템] 방을 만들었습니다. 플레이어를 기다리는 중...",
        ["[System] You joined the room."] = "[시스템] 방에 참가했습니다.",
        ["Leave room"] = "방 나가기",
        ["Start game"] = "게임 시작",
        ["Audio"] = "오디오",
        ["System default output"] = "시스템 기본 출력",
        ["Microphone"] = "마이크",
        ["Microphone inactive"] = "마이크 비활성",
        ["Voice chat unavailable"] = "음성 채팅 사용 불가",
        ["ON"] = "켜짐",
        ["OFF"] = "꺼짐",
        ["Graphics / Display"] = "그래픽 / 화면",
        ["Controls"] = "조작법",
        ["Accessibility"] = "접근성",
        ["Language"] = "언어",
        ["Resolution"] = "해상도",
        ["Frame rate limit (VSync off)"] = "프레임 제한 (수직 동기화 끔)",
        ["Vertical sync"] = "수직 동기화",
        ["Display mode"] = "화면 모드",
        ["Windowed"] = "창 모드",
        ["Borderless"] = "테두리 없는 창",
        ["Fullscreen"] = "전체 화면",
        ["Menu background blur"] = "메뉴 배경 흐림",
        ["Run in background"] = "백그라운드에서 실행",
        ["Graphics quality"] = "그래픽 품질",
        ["Low"] = "낮음",
        ["Medium"] = "중간",
        ["High"] = "높음",
        ["Ultra"] = "매우 높음",
        ["Master volume"] = "전체 음량",
        ["Mute audio"] = "음소거",
        ["Audio output"] = "오디오 출력",
        ["System default"] = "시스템 기본값",
        ["Move / look"] = "이동 / 시점",
        ["W A S D / Mouse"] = "W A S D / 마우스",
        ["Sprint / jump"] = "달리기 / 점프",
        ["Interact"] = "상호작용",
        ["Equip weapon / flashlight"] = "총기 / 손전등 장착",
        ["Fire"] = "발사",
        ["Left mouse button"] = "마우스 왼쪽 버튼",
        ["Reload / console rescan"] = "재장전 / 콘솔 재탐색",
        ["Inventory"] = "인벤토리",
        ["Higher-contrast settings text"] = "설정 글자 대비 높이기",
        ["Larger settings text"] = "설정 글자 크기 키우기"
    };

    internal static string Translate(string english) =>
        Korean && english != null && KoreanText.TryGetValue(english, out string korean) ? korean : english;

    internal static string ChatLine(string line)
    {
        if (line.StartsWith("[System]", StringComparison.Ordinal)) return Translate(line);
        int separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? Translate(line.Substring(0, separator)) + line.Substring(separator) : line;
    }

    internal void Bind(Text text, string english)
    {
        if (text == null) return;
        Font original = text.font;
        void Apply()
        {
            if (text == null) return;
            text.font = Korean && font != null ? font : original;
            text.text = Translate(english);
        }
        updates.Add(Apply);
        Apply();
    }

    internal void BindArtwork(Graphic graphic, string english, Rect area, float sampleX, int fontSize)
    {
        if (graphic == null) return;
        Texture texture;
        Rect uv;
        if (graphic is RawImage raw)
        {
            texture = raw.texture;
            uv = raw.uvRect;
        }
        else if (graphic is Image image && image.sprite != null)
        {
            texture = image.sprite.texture;
            Rect pixels = image.sprite.textureRect;
            uv = new Rect(pixels.x / texture.width, pixels.y / texture.height,
                pixels.width / texture.width, pixels.height / texture.height);
        }
        else return;

        var background = new GameObject("Korean caption", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        background.transform.SetParent(graphic.transform, false);
        background.rectTransform.anchorMin = new Vector2(area.xMin, 1f - area.yMax);
        background.rectTransform.anchorMax = new Vector2(area.xMax, 1f - area.yMin);
        background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
        background.raycastTarget = false;
        background.texture = texture;
        background.uvRect = new Rect(uv.x + uv.width * sampleX, uv.y + uv.height * (1f - area.yMax),
            uv.width * 0.025f, uv.height * area.height);

        var caption = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        caption.transform.SetParent(background.transform, false);
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
        caption.raycastTarget = false;
        caption.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        caption.fontSize = fontSize;
        caption.fontStyle = FontStyle.Bold;
        caption.alignment = TextAnchor.MiddleCenter;
        caption.color = new Color(0.96f, 0.92f, 0.87f);
        caption.resizeTextForBestFit = true;
        caption.resizeTextMinSize = 12;
        caption.resizeTextMaxSize = fontSize;
        void Apply()
        {
            if (background == null) return;
            background.material = graphic.material;
            caption.text = Translate(english);
            background.gameObject.SetActive(Korean);
        }
        updates.Add(Apply);
        Apply();
    }

    internal void Refresh()
    {
        foreach (Action update in updates) update();
    }
}
