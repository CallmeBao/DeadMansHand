// THE LAST KING — Online PvP: phần bổ sung của LastKingGameController (partial class).
//
// Nguyên tắc tích hợp (xem docs/ONLINE_PVP_DESIGN.md §7):
//  * Khi `online == false` mọi nhánh PvE chạy y như bản gốc; các điểm móc trong LastKingGameController.cs đều là
//    "if (online) { ...; return; }" ở đầu hàm hoặc ánh xạ ghế (Me/Opp) vốn bằng 0/1 ở PvE.
//  * Giao diện luôn hiển thị người chơi tại máy này ở bên TRÁI (ghế nhìn = 0). Ghế luật của máy chủ (mySeat) chỉ được
//    dùng khi đọc MatchState; đổi sang ghế nhìn bằng ViewSeat() trước khi gọi vật lý/hiệu ứng.
//  * Máy chủ có thẩm quyền: client không tự tính kết quả, không tự hết giờ, không tự sang lượt. Mọi thứ đi theo sự kiện.
using System;
using System.Collections;
using System.Collections.Generic;
using TheLastKing.Online;
using UnityEngine;
using UnityEngine.UI;

namespace TheLastKing
{
    public sealed partial class LastKingGameController
    {
        // ------------------------------------------------------------------ trạng thái chế độ online
        bool online;
        int mySeat;
        string opponentLabel = "AI";
        OnlineClient net;
        bool lockPending, opponentLocked, onlineMenuOpen, linkLost, opponentLinkLost, timerFrozen;
        float onlineDeadline, onlineTotalSeconds = 25f, frozenLeft;
        float savedPlacementTimeout;
        bool physicsTuned;
        /// <summary>Thời gian tối đa để tự tay đặt Xu Máu ở chế độ online (đối thủ đang chờ), ngắn hơn PvE.</summary>
        const float OnlinePlacementSeconds = 8f;
        readonly List<OnlineEvent> pendingOnline = new List<OnlineEvent>();

        /// <summary>Trận online vừa bắt đầu (giao diện ẩn sảnh).</summary>
        public event Action OnlineMatchBegan;
        /// <summary>Bảng kết quả của trận online đã hiện (giao diện thêm nút Đấu lại / Rời phòng).</summary>
        public event Action OnlineEndShown;
        /// <summary>Controller đã thoát chế độ online và trả bàn về trạng thái chờ (giao diện khôi phục nhãn gốc).</summary>
        public event Action OnlineClosed;

        public bool IsOnline { get { return online; } }

        // Ghế "của tôi/đối thủ". Ở PvE: Me = 0, Opp = 1 nên toàn bộ mã gốc giữ nguyên ý nghĩa.
        int Me { get { return online ? mySeat : 0; } }
        int Opp { get { return 1 - Me; } }
        string OpponentLabel { get { return opponentLabel; } }
        /// <summary>Ghế luật -> ghế nhìn (0 = bên trái = người chơi tại máy này). Dùng cho đĩa cân/xu.</summary>
        int ViewSeat(int serverSeat) { return serverSeat == Me ? 0 : 1; }

        // ------------------------------------------------------------------ giao diện gọi vào

        /// <summary>Giao diện chuyển sự kiện cấp trận cho controller. Được xử lý theo thứ tự trong Update().</summary>
        public void OnlineEnqueue(OnlineClient client, OnlineEvent e)
        {
            net = client;
            pendingOnline.Add(e);
        }

        /// <summary>Người chơi chủ động rời phòng/trận (nút "Rời trận", "Rời phòng").</summary>
        public void OnlineLeave()
        {
            if (net != null) net.LeaveRoom();
            OnlineReset(true);
        }

        /// <summary>Mất kết nối vĩnh viễn: dọn bàn và về màn hình chính.</summary>
        public void OnlineForceExit() { OnlineReset(true); }

        /// <summary>Từ bảng kết quả (PvE hoặc online) quay về màn hình chính để chọn chế độ khác.</summary>
        public void ReturnToWelcome()
        {
            if (online && net != null) net.LeaveRoom();
            OnlineReset(true);
        }

        // ------------------------------------------------------------------ vòng cập nhật

        void OnlineUpdate()
        {
            PumpOnlineEvents();
            if (!online) return;

            if (Input.GetKeyDown(KeyCode.Escape)) OnlineToggleMenu();
            UpdateOnlineTimerText();

            if (stage == Stage.Choosing && !lockPending && !onlineMenuOpen && !linkLost)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectCard(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SelectCard(1);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SelectCard(2);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) LockSelection();
            }

            for (int i = 0; i < CardButtons.Length; i++)
            {
                if (!CardButtons[i].gameObject.activeSelf) continue;
                var rect = CardButtons[i].transform as RectTransform;
                bool pickable = stage == Stage.Choosing && !lockPending && !onlineMenuOpen && !linkLost;
                bool hover = pickable && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
                float size = (stage == Stage.Choosing || stage == Stage.Locked) && (selected == i || hover) ? 1.055f : 1;
                rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one * size, Time.deltaTime * 14);
            }
        }

        /// <summary>Đồng hồ hiển thị bám theo hạn của máy chủ (không đếm lùi tự do) nên không trôi khi mở menu hoặc giật frame.</summary>
        void UpdateOnlineTimerText()
        {
            if (stage == Stage.Choosing || stage == Stage.Locked)
            {
                float left = timerFrozen ? frozenLeft : Mathf.Max(0f, onlineDeadline - Time.realtimeSinceStartup);
                SetText(TimerText, Mathf.CeilToInt(left) + " GIÂY");
                if (TimerFill) { TimerFill.fillAmount = Mathf.Clamp01(left / onlineTotalSeconds); TimerFill.color = left <= 5 ? blood : gold; }
            }
            else if (stage == Stage.Depositing)
            {
                SetText(TimerText, "ĐẶT XU");
                if (TimerFill) TimerFill.fillAmount = 1;
            }
            else if (stage == Stage.Waiting)
            {
                SetText(TimerText, "…");
                if (TimerFill) TimerFill.fillAmount = 0;
            }
        }

        void SetOnlineDeadline(int remainingMs, int totalMs)
        {
            onlineDeadline = Time.realtimeSinceStartup + Mathf.Max(0, remainingMs) / 1000f;
            onlineTotalSeconds = Mathf.Max(1f, totalMs / 1000f);
        }

        void FreezeTimer()
        {
            if (timerFrozen) return;
            frozenLeft = Mathf.Max(0f, onlineDeadline - Time.realtimeSinceStartup);
            timerFrozen = true;
        }

        void UnfreezeTimer()
        {
            if (!timerFrozen || linkLost || opponentLinkLost) return;
            onlineDeadline = Time.realtimeSinceStartup + frozenLeft;
            timerFrozen = false;
        }

        // ------------------------------------------------------------------ hàng đợi sự kiện

        static bool IsUrgentOnlineEvent(OnlineEventKind k)
        {
            return k == OnlineEventKind.MatchAborted || k == OnlineEventKind.LinkLost || k == OnlineEventKind.LinkRestored
                || k == OnlineEventKind.OpponentConnection || k == OnlineEventKind.ServerError;
        }

        /// <summary>
        /// Sự kiện "dòng chảy" (MatchStarted, TurnBegan, TurnResolved, Resynced...) phải chờ phần trình diễn đang chạy xong
        /// để không giẫm lên hoạt ảnh; sự kiện khẩn (hủy trận, đứt mạng...) được xử lý ngay.
        /// </summary>
        void PumpOnlineEvents()
        {
            bool blocked = false;
            for (int i = 0; i < pendingOnline.Count;)
            {
                OnlineEvent e = pendingOnline[i];
                bool busy = stage == Stage.Revealing || stage == Stage.Depositing || stage == Stage.Ending;
                if (!IsUrgentOnlineEvent(e.Kind) && (blocked || busy)) { blocked = true; i++; continue; }
                pendingOnline.RemoveAt(i);
                HandleOnlineEvent(e);
            }
        }

        void HandleOnlineEvent(OnlineEvent e)
        {
            if (!online && e.Kind != OnlineEventKind.MatchStarted) return;      // sự kiện muộn của trận đã thoát
            switch (e.Kind)
            {
                case OnlineEventKind.MatchStarted: OnlineBeginMatch(); break;
                case OnlineEventKind.TurnBegan: OnlineBeginTurn(e); break;
                case OnlineEventKind.OpponentLocked: opponentLocked = true; RefreshOnlineStatus(); break;
                case OnlineEventKind.LockConfirmed: OnlineLockConfirmed(e); break;
                case OnlineEventKind.TurnResolved: OnlineTurnResolved(e); break;
                case OnlineEventKind.MatchAborted: OnlineReset(false); break;
                case OnlineEventKind.OpponentConnection:
                    opponentLinkLost = !e.Flag;
                    if (opponentLinkLost) FreezeTimer(); else UnfreezeTimer();
                    break;
                case OnlineEventKind.LinkLost:
                    linkLost = true; FreezeTimer();
                    SetText(StatusText, "MẤT KẾT NỐI — đang thử nối lại. Trận được giữ chỗ cho bạn.");
                    UpdateControls();
                    break;
                case OnlineEventKind.LinkRestored: linkLost = false; UnfreezeTimer(); break;
                case OnlineEventKind.Resynced: OnlineApplyResync(e); break;
                case OnlineEventKind.ServerError:
                    if (lockPending) { lockPending = false; UpdateControls(); }
                    SetText(StatusText, e.Text);
                    break;
            }
        }

        // ------------------------------------------------------------------ vòng đời trận online

        void OnlineBeginMatch()
        {
            online = true;
            mySeat = net.Seat;
            opponentLabel = net.OpponentName.ToUpperInvariant();
            StopAllCoroutines();
            paused = false; onlineMenuOpen = false; linkLost = false; opponentLinkLost = false; timerFrozen = false;
            Time.timeScale = 1;
            TuneOnlinePhysics(true);
            Physics.ResetCoins();
            match = net.Mirror;
            history.Clear(); selected = -1; lockPending = false; opponentLocked = false;
            ResetTableVisuals();
            SetActive(StartPanel, false); SetActive(EndPanel, false); SetActive(PausePanel, false);
            SetActive(RevealPanel, false); SetActive(AutoPlaceButton.gameObject, false); SetActive(DepositHint.gameObject, false);
            stage = Stage.Waiting;
            RenderState(match.State);
            SetText(StatusText, "VÁN ONLINE · BẠN GIỮ " + (mySeat == 0 ? "NGAI VÀNG (VUA)" : "NÔ LỆ") + " · đang chờ lượt đầu…");
            UpdateControls();
            if (OnlineMatchBegan != null) OnlineMatchBegan();
        }

        void OnlineBeginTurn(OnlineEvent e)
        {
            match = net.Mirror;
            SetOnlineDeadline(e.RemainingMs, e.TotalMs);
            timerFrozen = false;
            if (e.Refresh && (stage == Stage.Choosing || stage == Stage.Locked)) return;   // chỉ làm mới đồng hồ (sau nối lại)
            selected = -1; lockPending = false; opponentLocked = false;
            stage = Stage.Choosing;
            SetActive(RevealPanel, false); SetActive(AutoPlaceButton.gameObject, false); SetActive(DepositHint.gameObject, false);
            RenderState(match.State);
            RefreshOnlineStatus();
            UpdateControls();
        }

        void RefreshOnlineStatus()
        {
            if (stage == Stage.Choosing)
                SetText(StatusText, opponentLocked
                    ? OpponentLabel + " ĐÃ KHÓA. Chọn 1 lá rồi KHÓA BÀI."
                    : "Chọn 1 lá rồi KHÓA BÀI. " + OpponentLabel + " cũng đang chọn kín, đồng thời.");
            else if (stage == Stage.Locked)
                SetText(StatusText, opponentLocked ? "Cả hai đã khóa — chuẩn bị lật bài…" : "ĐÃ KHÓA. Đang chờ " + OpponentLabel + " khóa bài…");
        }

        void OnlineSelectCard(int index)
        {
            if (stage != Stage.Choosing || lockPending || onlineMenuOpen || linkLost || net == null) return;
            IReadOnlyList<Card> hand = match.State.Players[mySeat].Hand;
            if (index < 0 || index >= hand.Count) return;
            selected = index; Play(SelectSound); UpdateControls();
            net.Select(hand[index].Id);       // lựa chọn TẠM: để máy chủ có lá hợp lệ khi hết giờ; không bao giờ gửi cho đối thủ
        }

        void OnlineLock()
        {
            if (stage != Stage.Choosing || lockPending || selected < 0 || onlineMenuOpen || linkLost || net == null) return;
            IReadOnlyList<Card> hand = match.State.Players[mySeat].Hand;
            if (selected >= hand.Count) return;
            if (net.Lock(hand[selected].Id))
            {
                lockPending = true;           // chỉ coi là đã khóa khi nhận LockConfirmed từ máy chủ
                SetText(StatusText, "Đang gửi yêu cầu khóa…");
                UpdateControls();
            }
        }

        void OnlineLockConfirmed(OnlineEvent e)
        {
            if (stage != Stage.Choosing) return;
            lockPending = false;
            int idx = MatchStateCodec.IndexOfCard(match.State.Players[mySeat].Hand, e.CardId);
            if (idx >= 0) selected = idx;
            stage = Stage.Locked;
            RefreshOnlineStatus();
            UpdateControls();
        }

        void OnlineTurnResolved(OnlineEvent e)
        {
            lockPending = false;
            match = net.Mirror;                      // OnlineClient có thể đã thay gương nếu phát hiện lệch pha
            selected = -1;
            if (e.Result == null)
            {
                // Không phát lại được lượt này (lệch pha): bỏ qua hoạt ảnh, lấy trạng thái chuẩn của máy chủ.
                stage = Stage.Waiting;
                RenderState(e.Snapshot);
                SetText(StatusText, "Đã đồng bộ lại trạng thái với máy chủ.");
                net.PresentationDone();
                UpdateControls();
                if (e.Snapshot.IsFinished) StartCoroutine(EndMatch());
                return;
            }
            string note = (e.TimeoutMask & (1 << mySeat)) != 0 ? "HẾT GIỜ — máy chủ chốt lá bạn đã chọn.\n"
                        : (e.TimeoutMask & (1 << (1 - mySeat))) != 0 ? OpponentLabel + " HẾT GIỜ — máy chủ chốt lá của họ.\n" : "";
            stage = Stage.Revealing;
            UpdateControls();
            StartCoroutine(ResolvePresentation(e.Result));
            // StartCoroutine chạy đồng bộ tới lệnh yield đầu tiên nên RevealText đã có nội dung của lượt này.
            if (note.Length > 0 && RevealText) RevealText.text = note + RevealText.text;
        }

        /// <summary>Gọi cuối ResolvePresentation ở chế độ online: báo máy chủ đã trình diễn xong rồi chờ lượt kế tiếp (hoặc kết thúc trận).</summary>
        IEnumerator OnlinePresentationDone(TurnResult result)
        {
            if (net != null) net.PresentationDone();
            if (result.After.IsFinished)
            {
                yield return EndMatch();
                yield break;
            }
            stage = Stage.Waiting;
            SetText(StatusText, "Chờ lượt kế tiếp…");
            UpdateControls();
        }

        void OnlineEndScreenShown()
        {
            if (OnlineEndShown != null) OnlineEndShown();
        }

        /// <summary>Bấm "Đấu lại" ở bảng kết quả: báo sẵn sàng; máy chủ bắt đầu khi cả hai cùng sẵn sàng (đổi ghế).</summary>
        void OnlineRematch()
        {
            if (net != null) net.SetReady(true);
        }

        void OnlineToggleMenu()
        {
            onlineMenuOpen = !onlineMenuOpen;      // KHÔNG đụng Time.timeScale: trận online không dừng được
            SetActive(PausePanel, onlineMenuOpen);
            UpdateControls();
        }

        /// <summary>Khôi phục trạng thái chính xác sau khi nối lại (máy chủ gửi Resync).</summary>
        void OnlineApplyResync(OnlineEvent e)
        {
            ResyncMsg r = e.Resync;
            match = net.Mirror;
            mySeat = r.Seat;
            opponentLabel = net.OpponentName.ToUpperInvariant();
            linkLost = false; lockPending = false; selected = -1;
            opponentLocked = r.OpponentLocked; opponentLinkLost = !r.OpponentConnected;
            SetActive(RevealPanel, false); SetActive(AutoPlaceButton.gameObject, false); SetActive(DepositHint.gameObject, false);
            RenderState(match.State);
            if (r.Phase == MatchPhase.Selecting)
            {
                SetOnlineDeadline(r.RemainingMs, r.TotalMs);
                timerFrozen = false;
                if (opponentLinkLost) FreezeTimer();
                if (r.IAmLocked)
                {
                    selected = MatchStateCodec.IndexOfCard(match.State.Players[mySeat].Hand, r.MyLockedId);
                    stage = Stage.Locked;
                }
                else stage = Stage.Choosing;
                RefreshOnlineStatus();
            }
            else
            {
                stage = Stage.Waiting;
                SetText(StatusText, "Đã nối lại. Chờ lượt kế tiếp…");
                if (match.State.IsFinished)
                {
                    if (net != null) net.PresentationDone();
                    StartCoroutine(EndMatch());
                }
            }
            UpdateControls();
        }

        string LockCaption()
        {
            if (online && stage == Stage.Locked) return "ĐÃ KHÓA\n<size=15>CHỜ " + OpponentLabel + "</size>";
            if (online && lockPending) return "ĐANG KHÓA…";
            return "KHÓA BÀI\n<size=15>ENTER / SPACE</size>";
        }

        // ------------------------------------------------------------------ dọn bàn / thoát chế độ online

        void ResetTableVisuals()
        {
            if (OpponentVisual) { OpponentVisual.localPosition = opponentPosition; OpponentVisual.localRotation = opponentRotation; }
            if (OpponentTint) OpponentTint.color = opponentColor;
            if (WorldCamera) WorldCamera.transform.position = cameraPosition;
            SetFlash(0, gold);
        }

        void TuneOnlinePhysics(bool on)
        {
            if (on && !physicsTuned)
            {
                savedPlacementTimeout = Physics.PlacementTimeout;
                Physics.PlacementTimeout = Mathf.Min(Physics.PlacementTimeout, OnlinePlacementSeconds);
                physicsTuned = true;
            }
            else if (!on && physicsTuned)
            {
                Physics.PlacementTimeout = savedPlacementTimeout;
                physicsTuned = false;
            }
        }

        /// <summary>Thoát chế độ online, trả bàn về "chờ vào bàn" như lúc mới mở game (nhãn AI, vật lý và đồng hồ khôi phục).</summary>
        void OnlineReset(bool showStart)
        {
            StopAllCoroutines();
            pendingOnline.Clear();
            TuneOnlinePhysics(false);
            online = false; mySeat = 0; opponentLabel = "AI"; net = null;
            lockPending = false; opponentLocked = false; onlineMenuOpen = false; linkLost = false; opponentLinkLost = false; timerFrozen = false;
            paused = false; Time.timeScale = 1;
            Physics.ResetCoins();
            match = new LastKingMatch(); history.Clear(); selected = -1;
            ResetTableVisuals();
            SetActive(RevealPanel, false); SetActive(EndPanel, false); SetActive(PausePanel, false);
            SetActive(AutoPlaceButton.gameObject, false); SetActive(DepositHint.gameObject, false);
            RenderState(match.State);
            stage = Stage.Welcome;
            SetText(StatusText, "Một ngai vàng. Hai người chơi. Năm Xu Máu là giới hạn.");
            UpdateControls();
            if (showStart) SetActive(StartPanel, true);
            if (OnlineClosed != null) OnlineClosed();
        }
    }
}
