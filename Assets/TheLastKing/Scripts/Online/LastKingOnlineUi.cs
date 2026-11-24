// THE LAST KING — Online PvP: giao diện dựng bằng code lúc chạy (không sửa scene/prefab đã lưu).
//
// Thành phần được thêm vào Canvas hiện có, dùng lại font của game và cùng lưới tọa độ 1600×900 (gốc trên-trái) như scene:
//   * nút "CHƠI ONLINE" trên Start Panel
//   * Online Menu Panel : tên, máy chủ, TLS, kết nối; sau đó tạo phòng / nhập mã phòng
//   * Online Lobby Panel: mã phòng, trạng thái hai ghế, sẵn sàng, chuỗi thắng
//   * banner trạng thái trong trận (mã phòng, ván, ping, mất kết nối)
//   * nút "RỜI PHÒNG"/"MÀN HÌNH CHÍNH" ở bảng kết quả, nút "RỜI TRẬN" ở menu Esc
// Lớp này sở hữu OnlineClient, bơm sự kiện mỗi frame và chuyển sự kiện cấp trận cho LastKingGameController.
using System;
using TheLastKing.Online;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TheLastKing
{
    public sealed class LastKingOnlineUi : MonoBehaviour
    {
        const string PrefName = "LK.Online.Name";
        const string PrefServer = "LK.Online.Server";
        const string PrefTls = "LK.Online.Tls";
        const string PrefPin = "LK.Online.Pin.";

        static readonly Color Gold = new Color32(219, 193, 122, 255);
        static readonly Color Cream = new Color32(236, 219, 181, 255);
        static readonly Color Muted = new Color32(171, 157, 130, 255);
        static readonly Color Blood = new Color32(255, 122, 97, 255);
        static readonly Color Good = new Color32(117, 183, 175, 255);
        static readonly Color Ink = new Color32(35, 25, 14, 255);

        LastKingGameController game;
        Font font;
        Transform root;
        OnlineClient net;

        // ---- panels & widgets
        GameObject menuPanel, lobbyPanel, connectGroup, roomGroup;
        InputField nameField, serverField, codeField;
        Button connectButton, tlsButton, forgetPinButton, createButton, joinButton, readyButton;
        Text menuStatus, tlsCaption, roomHeader, readyCaption;
        Text lobbyCode, lobbyMe, lobbyOpp, lobbySeries, lobbyNote;
        Text netText, endSeriesText, pauseTitle, restartCaption, endExitCaption;
        Button endExitButton, pauseLeaveButton;

        // ---- trạng thái
        bool useTls;
        volatile string pinnedFingerprint = "";
        volatile string seenFingerprint = "";
        volatile bool pinMismatch;
        string pinKey = "";
        string pendingNotice = "";
        bool lobbyAfterMatch;
        float opponentLostUntil;

        public void Init(LastKingGameController g)
        {
            game = g;
            font = g.StatusText.font;
            root = g.StartPanel.transform.parent;
            BuildStartButton();
            BuildMenuPanel();
            BuildLobbyPanel();
            BuildMatchExtras();
            game.OnlineMatchBegan += OnMatchBegan;
            game.OnlineEndShown += OnEndShown;
            game.OnlineClosed += OnGameClosed;
            menuPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            RestoreMatchExtras();
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.OnlineMatchBegan -= OnMatchBegan;
                game.OnlineEndShown -= OnEndShown;
                game.OnlineClosed -= OnGameClosed;
            }
            DisposeNet();
        }

        // ================================================================== dựng giao diện

        RectTransform MakeRect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        Image Box(string name, Transform parent, float x, float y, float w, float h, Color color, bool raycast)
        {
            var rt = MakeRect(name, parent, x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        GameObject Overlay(string name)
        {
            var image = Box(name, root, 800, 450, 1600, 900, new Color32(12, 10, 8, 244), true);
            return image.gameObject;
        }

        /// <summary>Nhãn có tâm tại (cx, cy).</summary>
        Text Label(string name, Transform parent, string content, float cx, float cy, float w, float h, int size, Color color, TextAnchor anchor)
        {
            var rt = MakeRect(name, parent, cx, cy, w, h);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font; text.text = content; text.fontSize = size; text.color = color;
            text.alignment = anchor; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        Text Label(string name, Transform parent, string content, float cx, float cy, float w, float h, int size, Color color)
        {
            return Label(name, parent, content, cx, cy, w, h, size, color, TextAnchor.MiddleCenter);
        }

        Button MakeButton(string name, Transform parent, string caption, float cx, float cy, float w, float h, int size, UnityAction onClick)
        {
            Image image = Box(name, parent, cx, cy, w, h, Gold, true);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1, .91f, .7f);
            colors.pressedColor = new Color(.73f, .60f, .33f);
            colors.disabledColor = new Color(.3f, .27f, .22f);
            button.colors = colors;
            Label("Caption", image.transform, caption, w * .5f, h * .5f, w - 12, h - 4, size, Ink);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        static Text CaptionOf(Button b)
        {
            return b != null ? b.GetComponentInChildren<Text>(true) : null;
        }

        static void SetCaption(Button b, string text)
        {
            Text t = CaptionOf(b);
            if (t != null) t.text = text;
        }

        Text StretchText(string name, Transform parent, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(14, 4); rt.offsetMax = new Vector2(-14, -4);
            var text = go.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = anchor;
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        InputField MakeInput(string name, Transform parent, float cx, float cy, float w, float h, int size, string placeholder, int limit, string initial, TextAnchor anchor)
        {
            Image bg = Box(name, parent, cx, cy, w, h, new Color32(38, 29, 18, 255), true);
            bg.gameObject.AddComponent<RectMask2D>();
            InputField field = bg.gameObject.AddComponent<InputField>();
            Text text = StretchText("Text", bg.transform, size, Cream, anchor);
            Text hint = StretchText("Placeholder", bg.transform, size, new Color(Muted.r, Muted.g, Muted.b, .7f), anchor);
            hint.text = placeholder;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = bg;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = limit;
            field.text = initial ?? "";
            return field;
        }

        // ------------------------------------------------------------------ Start Panel

        void BuildStartButton()
        {
            Transform start = game.StartPanel.transform;
            MakeButton("Online Match", start, "CHƠI ONLINE (PvP)", 800, 672, 390, 58, 24, OpenMenu);
            Transform controls = start.Find("Welcome Controls");           // nhường chỗ cho nút mới
            if (controls != null)
            {
                var rt = controls as RectTransform;
                if (rt != null) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -758);
            }
        }

        // ------------------------------------------------------------------ Menu kết nối / phòng

        void BuildMenuPanel()
        {
            menuPanel = Overlay("Online Menu Panel");
            Transform p = menuPanel.transform;
            Label("Title", p, "CHƠI ONLINE", 800, 112, 900, 70, 52, Gold);
            Label("Subtitle", p, "Đấu với người thật qua máy chủ. Mỗi trận hai người; ghế Vua / Nô lệ được bốc ngẫu nhiên và đổi sau mỗi ván để công bằng.", 800, 178, 1100, 64, 20, Cream);

            // --- nhóm kết nối
            connectGroup = MakeRect("Connect Group", p, 800, 450, 1600, 900).gameObject;
            Transform c = connectGroup.transform;
            Label("Name Label", c, "TÊN HIỂN THỊ", 800, 262, 640, 26, 17, Muted);
            nameField = MakeInput("Name Field", c, 800, 304, 640, 50, 24, "Người chơi", OnlineProtocol.MaxNameChars,
                PlayerPrefs.GetString(PrefName, ""), TextAnchor.MiddleCenter);
            Label("Server Label", c, "MÁY CHỦ  (IP:CỔNG hoặc tên miền)", 800, 366, 640, 26, 17, Muted);
            serverField = MakeInput("Server Field", c, 800, 408, 640, 50, 24, "127.0.0.1:" + OnlineProtocol.DefaultPort, 80,
                PlayerPrefs.HasKey(PrefServer) ? PlayerPrefs.GetString(PrefServer) : DefaultServer(), TextAnchor.MiddleCenter);
            useTls = PlayerPrefs.GetInt(PrefTls, 0) == 1;
            tlsButton = MakeButton("Tls Toggle", c, "", 800, 484, 460, 46, 20, ToggleTls);
            tlsCaption = CaptionOf(tlsButton);
            RefreshTlsCaption();
            connectButton = MakeButton("Connect", c, "KẾT NỐI", 800, 562, 390, 62, 25, Connect);
            forgetPinButton = MakeButton("Forget Pin", c, "QUÊN CHỨNG CHỈ ĐÃ GHIM", 800, 632, 420, 42, 18, ForgetPin);
            forgetPinButton.gameObject.SetActive(false);

            // --- nhóm phòng (sau khi đã kết nối)
            roomGroup = MakeRect("Room Group", p, 800, 450, 1600, 900).gameObject;
            Transform r = roomGroup.transform;
            roomHeader = Label("Room Header", r, "", 800, 266, 1200, 34, 20, Good);
            createButton = MakeButton("Create Room", r, "TẠO PHÒNG MỚI", 800, 346, 430, 66, 25, CreateRoom);
            Label("Or Label", r, "HOẶC NHẬP MÃ PHÒNG CỦA NGƯỜI BẠN MUỐN ĐẤU", 800, 438, 900, 28, 17, Muted);
            codeField = MakeInput("Code Field", r, 800, 486, 300, 58, 32, "MÃ", OnlineProtocol.RoomCodeLength + 2, "", TextAnchor.MiddleCenter);
            codeField.onValidateInput = ValidateCodeChar;
            joinButton = MakeButton("Join Room", r, "VÀO PHÒNG", 800, 568, 390, 62, 25, JoinRoom);
            MakeButton("Disconnect", r, "NGẮT KẾT NỐI", 800, 648, 300, 46, 19, Back);

            menuStatus = Label("Status", p, "", 800, 726, 1150, 76, 20, Cream);
            MakeButton("Back", p, "QUAY LẠI", 800, 828, 260, 54, 22, Back);
        }

        static char ValidateCodeChar(string text, int index, char c)
        {
            char u = char.ToUpperInvariant(c);
            return OnlineProtocol.RoomCodeAlphabet.IndexOf(u) >= 0 ? u : '\0';
        }

        static string DefaultServer()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, "lastking-server.txt");
                if (System.IO.File.Exists(path))
                {
                    foreach (string line in System.IO.File.ReadAllLines(path))
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith("#", StringComparison.Ordinal)) return t;
                    }
                }
            }
            catch (Exception) { }
            return "127.0.0.1:" + OnlineProtocol.DefaultPort;
        }

        // ------------------------------------------------------------------ Sảnh phòng

        void BuildLobbyPanel()
        {
            lobbyPanel = Overlay("Online Lobby Panel");
            Transform p = lobbyPanel.transform;
            Label("Title", p, "PHÒNG CHỜ", 800, 104, 800, 44, 26, Muted);
            lobbyCode = Label("Code", p, "", 800, 188, 1000, 112, 84, Gold);
            Label("Hint", p, "Gửi mã này cho người bạn muốn đấu. Phòng chỉ có hai chỗ.", 800, 262, 1000, 30, 20, Cream);
            lobbyMe = Label("Me", p, "", 800, 346, 1000, 46, 27, Cream);
            lobbyOpp = Label("Opponent", p, "", 800, 402, 1000, 46, 27, Cream);
            lobbySeries = Label("Series", p, "", 800, 466, 1000, 34, 21, Gold);
            lobbyNote = Label("Note", p, "", 800, 536, 1100, 78, 19, Muted);
            readyButton = MakeButton("Ready", p, "SẴN SÀNG", 800, 640, 430, 68, 26, ToggleReady);
            readyCaption = CaptionOf(readyButton);
            MakeButton("Leave", p, "RỜI PHÒNG", 800, 728, 300, 54, 22, LeaveRoom);
        }

        // ------------------------------------------------------------------ Tiện ích trong trận

        void BuildMatchExtras()
        {
            netText = Label("Online Banner", root, "", 800, 100, 820, 28, 16, Muted);
            netText.gameObject.SetActive(false);

            Transform end = game.EndPanel.transform;
            endSeriesText = Label("Online Series", end, "", 800, 536, 1000, 34, 21, Gold);
            endExitButton = MakeButton("Exit To Menu", end, "MÀN HÌNH CHÍNH", 800, 700, 340, 56, 22, () => game.ReturnToWelcome());
            endExitCaption = CaptionOf(endExitButton);
            restartCaption = CaptionOf(game.RestartButton);

            Transform pause = game.PausePanel.transform;
            Transform title = pause.Find("Pause Title");
            if (title != null) pauseTitle = title.GetComponent<Text>();
            pauseLeaveButton = MakeButton("Leave Match", pause, "RỜI TRẬN (BỎ CUỘC)", 800, 592, 400, 66, 23, () => game.OnlineLeave());
        }

        /// <summary>Đặt giao diện chung về dạng PvE (nhãn gốc, ẩn phần của online).</summary>
        void RestoreMatchExtras()
        {
            if (netText != null) netText.gameObject.SetActive(false);
            if (endSeriesText != null) endSeriesText.text = "";
            if (restartCaption != null) restartCaption.text = "VÁN ĐẤU MỚI";
            if (game.RestartButton != null) game.RestartButton.interactable = true;
            if (endExitCaption != null) endExitCaption.text = "MÀN HÌNH CHÍNH";
            if (pauseTitle != null) pauseTitle.text = "TẠM DỪNG";
            if (pauseLeaveButton != null) pauseLeaveButton.gameObject.SetActive(false);
        }

        // ================================================================== điều khiển màn hình

        void OpenMenu()
        {
            ShowMenu();
            if (net != null && net.IsLive) ShowRoomGroup(); else ShowConnectGroup();
        }

        void ShowMenu()
        {
            lobbyPanel.SetActive(false);
            menuPanel.SetActive(true);
            menuPanel.transform.SetAsLastSibling();
        }

        void ShowConnectGroup()
        {
            connectGroup.SetActive(true); roomGroup.SetActive(false);
            connectButton.interactable = net == null || net.State == OnlineState.Closed || net.State == OnlineState.Idle;
        }

        void ShowRoomGroup()
        {
            connectGroup.SetActive(false); roomGroup.SetActive(true);
            string banner = net != null && net.Welcome != null ? net.Welcome.Banner : "";
            roomHeader.text = "ĐÃ KẾT NỐI" + (banner.Length > 0 ? "  ·  " + banner : "") + (useTls ? "  ·  TLS" : "");
        }

        void ShowLobby()
        {
            menuPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            lobbyPanel.transform.SetAsLastSibling();
            UpdateLobbyTexts();
        }

        void SetStatus(string text, Color color)
        {
            if (menuStatus != null) { menuStatus.text = text ?? ""; menuStatus.color = color; }
        }

        void Back()
        {
            DisposeNet();
            menuPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            game.ReturnToWelcome();          // chắc chắn Start Panel hiện lại
        }

        void DisposeNet()
        {
            if (net != null) { net.Dispose(); net = null; }
        }

        // ================================================================== hành động của người chơi

        void ToggleTls()
        {
            useTls = !useTls;
            RefreshTlsCaption();
        }

        void RefreshTlsCaption()
        {
            if (tlsCaption != null) tlsCaption.text = useTls ? "MÃ HÓA TLS: BẬT" : "MÃ HÓA TLS: TẮT";
        }

        void Connect()
        {
            string host; int port;
            if (!OnlineClient.TryParseAddress(serverField.text, out host, out port))
            {
                SetStatus("Địa chỉ máy chủ không hợp lệ. Ví dụ: 192.168.1.10:7777 hoặc play.example.com", Blood);
                return;
            }
            string name = OnlineProtocol.SanitizeName(nameField.text);
            if (name.Length == 0) name = "Người chơi " + UnityEngine.Random.Range(1000, 10000);
            DisposeNet();
            net = new OnlineClient { DisplayName = name, Build = Application.version };

            pinKey = PrefPin + host.ToLowerInvariant() + ":" + port;
            pinnedFingerprint = PlayerPrefs.GetString(pinKey, "");
            seenFingerprint = ""; pinMismatch = false;
            Func<string, string, bool> check = null;
            if (useTls) check = CheckCertificate;

            forgetPinButton.gameObject.SetActive(false);
            connectButton.interactable = false;
            SetStatus("Đang kết nối tới " + host + ":" + port + (useTls ? " (TLS)" : "") + "…", Cream);
            net.Connect(host, port, useTls, check);
        }

        /// <summary>Chạy trên luồng nền: KHÔNG gọi PlayerPrefs hay API Unity ở đây.</summary>
        bool CheckCertificate(string host, string fingerprint)
        {
            seenFingerprint = fingerprint;
            string pinned = pinnedFingerprint;
            if (pinned.Length == 0) return true;               // lần đầu: tin và ghi nhớ
            if (pinned == fingerprint) return true;
            pinMismatch = true;
            return false;
        }

        void ForgetPin()
        {
            if (pinKey.Length > 0) PlayerPrefs.DeleteKey(pinKey);
            PlayerPrefs.Save();
            forgetPinButton.gameObject.SetActive(false);
            SetStatus("Đã quên chứng chỉ cũ. Bấm KẾT NỐI để tin chứng chỉ hiện tại của máy chủ.", Cream);
        }

        void CreateRoom()
        {
            if (net == null) return;
            SetStatus("Đang tạo phòng…", Cream);
            net.CreateRoom();
        }

        void JoinRoom()
        {
            if (net == null) return;
            SetStatus("Đang vào phòng…", Cream);
            net.JoinRoom(codeField.text);
        }

        void ToggleReady()
        {
            if (net == null || net.Lobby == null) return;
            bool mine = net.Lobby.Slots[net.Lobby.MySlot].Ready;
            net.SetReady(!mine);
        }

        void LeaveRoom()
        {
            if (net == null) return;
            net.LeaveRoom();
            pendingNotice = "";
            lobbyPanel.SetActive(false);
            ShowMenu(); ShowRoomGroup();
            SetStatus("", Cream);
        }

        // ================================================================== vòng cập nhật & sự kiện

        void Update()
        {
            if (menuPanel != null && menuPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Back();
            if (net == null) { RefreshBanner(); return; }
            net.Update();
            OnlineEvent e;
            while (net != null && net.TryDequeue(out e)) HandleEvent(e);
            RefreshBanner();
        }

        void Forward(OnlineEvent e)
        {
            if (net != null) game.OnlineEnqueue(net, e);
        }

        void HandleEvent(OnlineEvent e)
        {
            switch (e.Kind)
            {
                case OnlineEventKind.Connected:
                    SaveSettings();
                    SetStatus("Kết nối thành công.", Good);
                    ShowMenu(); ShowRoomGroup();
                    break;

                case OnlineEventKind.ConnectFailed:
                    DisposeNet();
                    SetStatus(pinMismatch
                        ? "Chứng chỉ của máy chủ KHÁC lần kết nối trước (máy chủ cài lại, hoặc có kẻ chen giữa đường truyền). Nếu bạn tin máy chủ, bấm QUÊN CHỨNG CHỈ rồi kết nối lại."
                        : e.Text, Blood);
                    forgetPinButton.gameObject.SetActive(pinMismatch);
                    ShowConnectGroup();
                    break;

                case OnlineEventKind.Disconnected:
                {
                    bool inMatch = game.IsOnline;
                    DisposeNet();
                    if (inMatch) game.OnlineForceExit();
                    lobbyPanel.SetActive(false);
                    ShowMenu(); ShowConnectGroup();
                    SetStatus(e.Text, Blood);
                    break;
                }

                case OnlineEventKind.LobbyChanged:
                    OnLobbyChanged();
                    break;

                case OnlineEventKind.ServerError:
                    if (game.IsOnline) Forward(e);
                    else if (lobbyPanel.activeSelf) lobbyNote.text = e.Text;
                    else SetStatus(e.Text, Blood);
                    break;

                case OnlineEventKind.MatchAborted:
                    pendingNotice = e.Text;
                    Forward(e);
                    break;

                case OnlineEventKind.OpponentConnection:
                    opponentLostUntil = e.Flag ? 0f : Time.realtimeSinceStartup + e.Number / 1000f;
                    Forward(e);
                    break;

                case OnlineEventKind.MatchStarted:
                case OnlineEventKind.TurnBegan:
                case OnlineEventKind.OpponentLocked:
                case OnlineEventKind.LockConfirmed:
                case OnlineEventKind.TurnResolved:
                case OnlineEventKind.LinkLost:
                case OnlineEventKind.LinkRestored:
                case OnlineEventKind.Resynced:
                    Forward(e);
                    break;
            }
        }

        void OnLobbyChanged()
        {
            if (net == null || net.Lobby == null) return;
            if (game.IsOnline)
            {
                // Đang trong trận / ở bảng kết quả: sảnh chỉ cập nhật dữ liệu, không bật màn hình sảnh.
                if (net.Lobby.Phase == RoomPhase.Lobby) lobbyAfterMatch = true;
                RefreshEndPanel();
                return;
            }
            ShowLobby();
        }

        void SaveSettings()
        {
            PlayerPrefs.SetString(PrefName, nameField.text ?? "");
            PlayerPrefs.SetString(PrefServer, serverField.text ?? "");
            PlayerPrefs.SetInt(PrefTls, useTls ? 1 : 0);
            if (useTls && pinKey.Length > 0 && pinnedFingerprint.Length == 0 && seenFingerprint.Length > 0)
                PlayerPrefs.SetString(pinKey, seenFingerprint);        // ghim lần đầu (TOFU)
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ sảnh

        void UpdateLobbyTexts()
        {
            LobbyStateMsg l = net != null ? net.Lobby : null;
            if (l == null) return;
            int me = l.MySlot, opp = 1 - me;
            LobbySlot a = l.Slots[me], b = l.Slots[opp];
            lobbyCode.text = SpaceOut(l.RoomCode);
            lobbyMe.text = "BẠN — " + a.Name + "     " + (a.Ready ? "[ĐÃ SẴN SÀNG]" : "[chưa sẵn sàng]");
            lobbyOpp.text = b.Present
                ? "ĐỐI THỦ — " + b.Name + "     " + (b.Connected ? (b.Ready ? "[ĐÃ SẴN SÀNG]" : "[chưa sẵn sàng]") : "[mất kết nối]")
                : "ĐỐI THỦ — đang chờ người vào phòng…";
            lobbySeries.text = l.Games > 0
                ? "CHUỖI TRẬN:  BẠN " + l.Wins[me] + " — " + l.Wins[opp] + " " + b.Name.ToUpperInvariant() + "   (" + l.Games + " ván)"
                : "";
            lobbyNote.text = pendingNotice.Length > 0
                ? pendingNotice
                : "Ghế Vua / Nô lệ được bốc ngẫu nhiên ở ván đầu rồi đổi sau mỗi ván. Cả hai bấm SẴN SÀNG để bắt đầu.";
            if (readyCaption != null) readyCaption.text = a.Ready ? "HỦY SẴN SÀNG" : "SẴN SÀNG";
        }

        static string SpaceOut(string s)
        {
            var chars = new char[Math.Max(0, s.Length * 2 - 1)];
            for (int i = 0; i < s.Length; i++) { chars[i * 2] = s[i]; if (i * 2 + 1 < chars.Length) chars[i * 2 + 1] = ' '; }
            return new string(chars);
        }

        // ------------------------------------------------------------------ sự kiện của controller

        void OnMatchBegan()
        {
            menuPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            pendingNotice = "";
            lobbyAfterMatch = false;
            opponentLostUntil = 0f;
            if (pauseTitle != null) pauseTitle.text = "MENU  ·  TRẬN KHÔNG DỪNG";
            if (pauseLeaveButton != null) pauseLeaveButton.gameObject.SetActive(true);
            if (endExitCaption != null) endExitCaption.text = "RỜI PHÒNG";
            if (endSeriesText != null) endSeriesText.text = "";
            if (restartCaption != null) restartCaption.text = "CHỜ ĐỐI THỦ HOÀN TẤT…";
            if (game.RestartButton != null) game.RestartButton.interactable = false;
        }

        void OnEndShown()
        {
            RefreshEndPanel();
        }

        void OnGameClosed()
        {
            RestoreMatchExtras();
            if (net == null) return;
            if (net.State == OnlineState.InRoom) ShowLobby();
            else if (net.IsLive) { ShowMenu(); ShowRoomGroup(); }
        }

        /// <summary>Cập nhật bảng kết quả trận online: chuỗi thắng và nút Đấu lại phụ thuộc trạng thái phòng.</summary>
        void RefreshEndPanel()
        {
            if (!game.IsOnline || !game.EndPanel.activeSelf || net == null) return;
            LobbyStateMsg l = net.Lobby;
            Button restart = game.RestartButton;
            if (l == null) return;
            int me = l.MySlot, opp = 1 - me;
            bool oppPresent = l.Slots[opp].Present && l.Slots[opp].Connected;
            bool iReady = l.Slots[me].Ready, oppReady = l.Slots[opp].Ready;
            if (lobbyAfterMatch && l.Games > 0)
                endSeriesText.text = "CHUỖI PHÒNG " + l.RoomCode + ":  BẠN " + l.Wins[me] + " — " + l.Wins[opp] + " " + l.Slots[opp].Name.ToUpperInvariant() + "   (" + l.Games + " ván)";
            else endSeriesText.text = "";

            if (!lobbyAfterMatch) { restart.interactable = false; restartCaption.text = "CHỜ ĐỐI THỦ HOÀN TẤT…"; }
            else if (!oppPresent) { restart.interactable = false; restartCaption.text = "ĐỐI THỦ ĐÃ RỜI PHÒNG"; }
            else if (iReady) { restart.interactable = false; restartCaption.text = oppReady ? "ĐANG VÀO TRẬN…" : "CHỜ ĐỐI THỦ ĐỒNG Ý…"; }
            else { restart.interactable = true; restartCaption.text = oppReady ? "ĐỐI THỦ MUỐN ĐẤU LẠI — ĐỒNG Ý" : "ĐẤU LẠI · ĐỔI GHẾ"; }
        }

        // ------------------------------------------------------------------ banner

        void RefreshBanner()
        {
            if (netText == null) return;
            bool show = net != null && game != null && game.IsOnline;
            if (netText.gameObject.activeSelf != show) netText.gameObject.SetActive(show);
            if (!show) return;
            if (net.State == OnlineState.Reconnecting)
            {
                netText.text = "MẤT KẾT NỐI — ĐANG NỐI LẠI…";
                netText.color = Blood;
            }
            else if (opponentLostUntil > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(opponentLostUntil - Time.realtimeSinceStartup));
                netText.text = "ĐỐI THỦ MẤT KẾT NỐI — TRẬN TẠM DỪNG, CHỜ TỐI ĐA " + left + " GIÂY";
                netText.color = Blood;
            }
            else
            {
                string code = net.Lobby != null ? net.Lobby.RoomCode : "";
                netText.text = "PHÒNG " + code + "   ·   VÁN " + net.GameNo + "   ·   BẠN MỞ ĐẦU VỚI " + (net.Seat == 0 ? "VUA" : "NÔ LỆ") + "   ·   " + net.RoundTripMs + " ms";
                netText.color = Muted;
            }
        }
    }
}
