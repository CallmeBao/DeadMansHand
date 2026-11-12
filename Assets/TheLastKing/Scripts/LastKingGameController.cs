using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TheLastKing
{
    /// <summary>Connects the serialized Canvas, scene assets, rules and physical coin interaction.</summary>
    public sealed class LastKingGameController : MonoBehaviour
    {
        public LastKingPhysics Physics;
        public Camera WorldCamera;
        public Sprite[] CardSprites;
        public Button[] CardButtons;
        public Image[] CardImages;
        public Text[] CardLabels;
        public GameObject[] SelectionMarks;
        public Button LockButton, AutoPlaceButton, PauseButton, StartButton, RestartButton, ResumeButton;
        public Text TurnText, StatusText, HistoryText, PlayerHandText, OpponentHandText, DeckText, InitiativeText;
        public Text PlayerCoinText, OpponentCoinText, PlayerBpmText, OpponentBpmText, TimerText, LockButtonText, DepositHint;
        public Image TimerFill;
        public GameObject RevealPanel, StartPanel, EndPanel, PausePanel;
        public Image PlayerReveal, OpponentReveal;
        public Text RevealText, EndTitle, EndReasonText;
        public Transform OpponentVisual;
        public SpriteRenderer OpponentTint;
        public Image ScreenFlash;
        public AudioSource AudioSource;
        public AudioClip SelectSound, RevealSound, CoinSound, EndSound;
        [Header("Match presentation")]
        [Min(5)] public float SelectionSeconds = 25;
        [Min(.3f)] public float RevealSeconds = 2.6f;
        public bool SoundEnabled = true;

        enum Stage { Welcome, Choosing, Revealing, Depositing, Ending, Finished }
        Stage stage;
        LastKingMatch match;
        readonly System.Random random = new System.Random();
        readonly List<string> history = new List<string>();
        int selected = -1, aiChoice;
        float timer;
        bool paused, coinDeposited, smoke, smokeFailed;
        string smokeOutput;
        Vector3 opponentPosition, cameraPosition;
        Quaternion opponentRotation;
        Color opponentColor;
        readonly Color gold = new Color(.859f,.757f,.478f);
        readonly Color blood = new Color(1f,.48f,.38f);
        readonly Color cream = new Color(.973f,.937f,.863f);

        void Start()
        {
            Time.timeScale = 1;
            Application.targetFrameRate = 60;
            if (OpponentVisual) { opponentPosition = OpponentVisual.localPosition; opponentRotation = OpponentVisual.localRotation; }
            if (OpponentTint) opponentColor = OpponentTint.color;
            if (WorldCamera) cameraPosition = WorldCamera.transform.position;
            for (int i = 0; i < CardButtons.Length; i++)
            {
                int index = i;
                CardButtons[i].onClick.AddListener(() => SelectCard(index));
            }
            StartButton.onClick.AddListener(NewMatch);
            RestartButton.onClick.AddListener(NewMatch);
            LockButton.onClick.AddListener(LockSelection);
            AutoPlaceButton.onClick.AddListener(() => { if (stage == Stage.Depositing && !paused) Physics.AutoPlace(); });
            PauseButton.onClick.AddListener(TogglePause);
            ResumeButton.onClick.AddListener(TogglePause);
            Physics.OnDeposited += OnCoinDeposited;
            match = new LastKingMatch();
            RenderState(match.State);
            SetActive(StartPanel, true); SetActive(EndPanel, false); SetActive(PausePanel, false);
            SetActive(RevealPanel, false); SetActive(AutoPlaceButton.gameObject, false);
            SetActive(DepositHint.gameObject, false);
            SetFlash(0, gold);
            stage = Stage.Welcome;
            SetText(StatusText, "Một ngai vàng. Hai người chơi. Năm Xu Máu là giới hạn.");
            UpdateControls();
            smoke = Array.IndexOf(Environment.GetCommandLineArgs(), "-lastKingSmoke") >= 0;
            if (smoke)
            {
                smokeOutput = Path.GetFullPath(Path.Combine(Application.dataPath, "../QA/TheLastKing"));
                Directory.CreateDirectory(smokeOutput);
                Application.logMessageReceived += OnSmokeLog;
                StartCoroutine(SmokeRun());
            }
        }

        public void NewMatch()
        {
            StopAllCoroutines();
            BeginMatch();
        }

        void BeginMatch()
        {
            paused = false; Time.timeScale = 1;
            Physics.ResetCoins();
            match = new LastKingMatch(); history.Clear(); selected = -1;
            if (OpponentVisual) { OpponentVisual.localPosition = opponentPosition; OpponentVisual.localRotation = opponentRotation; }
            if (OpponentTint) OpponentTint.color = opponentColor;
            if (WorldCamera) WorldCamera.transform.position = cameraPosition;
            SetFlash(0, gold);
            SetActive(StartPanel, false); SetActive(EndPanel, false); SetActive(PausePanel, false);
            BeginTurn();
        }

        void BeginTurn()
        {
            if (match.State.IsFinished) { StartCoroutine(EndMatch()); return; }
            selected = -1;
            // Commit before enabling player input. The AI only receives the public snapshot.
            aiChoice = LastKingAI.ChooseCard(match.State, 1, random);
            timer = SelectionSeconds;
            stage = Stage.Choosing;
            SetActive(RevealPanel, false); SetActive(AutoPlaceButton.gameObject, false);
            SetActive(DepositHint.gameObject, false);
            SetText(StatusText, "Chọn 1 lá rồi KHÓA BÀI. AI đã khóa kín lựa chọn.");
            RenderState(match.State);
            UpdateControls();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if (paused) return;
            if (stage == Stage.Choosing)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectCard(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SelectCard(1);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SelectCard(2);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) LockSelection();
                if (stage == Stage.Choosing)
                {
                    timer = Mathf.Max(0, timer - Time.deltaTime);
                    SetText(TimerText, Mathf.CeilToInt(timer) + " GIÂY");
                    if (TimerFill) { TimerFill.fillAmount = timer / SelectionSeconds; TimerFill.color = timer <= 5 ? blood : gold; }
                    if (timer <= 0)
                    {
                        if (selected < 0) selected = random.Next(match.State.Players[0].Hand.Count);
                        LockSelection();
                    }
                }
            }
            else if (stage == Stage.Depositing)
            {
                SetText(TimerText, "ĐẶT XU");
                if (TimerFill) TimerFill.fillAmount = 1;
            }
            for (int i = 0; i < CardButtons.Length; i++)
            {
                if (!CardButtons[i].gameObject.activeSelf) continue;
                var rect = CardButtons[i].transform as RectTransform;
                bool hover = stage == Stage.Choosing && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
                float size = stage == Stage.Choosing && (selected == i || hover) ? 1.055f : 1;
                rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one * size, Time.deltaTime * 14);
            }
        }

        public void SelectCard(int index)
        {
            if (paused || stage != Stage.Choosing || index < 0 || index >= match.State.Players[0].Hand.Count) return;
            selected = index; Play(SelectSound); UpdateControls();
        }

        public void LockSelection()
        {
            if (paused || stage != Stage.Choosing || selected < 0) return;
            stage = Stage.Revealing;
            TurnResult result = match.ResolveTurn(selected, aiChoice);
            UpdateControls();
            StartCoroutine(ResolvePresentation(result));
        }

        IEnumerator ResolvePresentation(TurnResult result)
        {
            RenderState(result.Before);
            SetText(TurnText, "LƯỢT " + result.After.TurnIndex);
            SetActive(RevealPanel, true);
            PlayerReveal.sprite = CardSprites[(int)result.PlayedCards[0].Kind];
            OpponentReveal.sprite = CardSprites[(int)result.PlayedCards[1].Kind];
            string message = Describe(result);
            SetText(RevealText, message);
            SetText(StatusText, result.PairWinnerSeat == 0 ? "BẠN THẮNG LƯỢT" : result.PairWinnerSeat == 1 ? "AI THẮNG LƯỢT" : "HAI LÁ HÒA NHAU");
            Play(RevealSound);
            yield return new WaitForSeconds(RevealSeconds);
            history.Add("L" + result.After.TurnIndex + "   " + CardName(result.PlayedCards[0].Kind) + " / " + CardName(result.PlayedCards[1].Kind) +
                (result.CoinRecipientSeat < 0 ? "  ·  không xu" : "  ·  +1 xu " + SeatName(result.CoinRecipientSeat)));
            if (history.Count > 4) history.RemoveAt(0);
            RenderState(result.After);
            SetText(TurnText, "LƯỢT " + result.After.TurnIndex);
            SetText(StatusText, message);
            if (result.CoinRecipientSeat >= 0)
            {
                stage = Stage.Depositing; coinDeposited = false;
                SetActive(RevealPanel, false);
                int owner = result.CoinRecipientSeat;
                SetActive(AutoPlaceButton.gameObject, owner == 0);
                SetActive(DepositHint.gameObject, true);
                SetText(DepositHint, owner == 0 ? "BỐC XU · THẢ VÀO ĐĨA BÊN TRÁI\nGiữ chuột trái để nhấc · Thả chuột để buông" : "AI đang đặt Xu Máu lên đĩa bên phải…");
                if (!Physics.StartCoin(owner, result.After.Players[owner].BloodCoins))
                    throw new InvalidOperationException("A resolved Blood Coin could not enter the physical placement phase.");
                UpdateControls();
                float waiting = 0; bool helped = false;
                while (!coinDeposited)
                {
                    waiting += Time.deltaTime;
                    if (!helped && ((owner == 1 && waiting > 1.1f) || (smoke && waiting > .3f) || waiting > 24))
                    { Physics.AutoPlace(); helped = true; }
                    if (waiting > 40) throw new TimeoutException("Coin placement did not complete.");
                    yield return null;
                }
                SetActive(AutoPlaceButton.gameObject, false);
                SetActive(DepositHint.gameObject, false);
                yield return new WaitForSeconds(.5f);
            }
            if (result.After.IsFinished) yield return EndMatch();
            else BeginTurn();
        }

        void OnCoinDeposited(int owner) { coinDeposited = true; Play(CoinSound); }

        IEnumerator EndMatch()
        {
            stage = Stage.Ending; UpdateControls(); SetActive(RevealPanel, false);
            SetActive(AutoPlaceButton.gameObject, false); SetActive(DepositHint.gameObject, false);
            SetText(StatusText, match.State.IsDraw ? "TRẬN ĐẤU HÒA" : "KẾT QUẢ ĐÃ ĐỊNH · GHẾ ĐIỆN KÍCH HOẠT");
            if (!match.State.IsDraw)
            {
                Play(EndSound);
                bool opponentLost = match.State.WinnerSeat == 0;
                float elapsed = 0;
                while (elapsed < 1.7f)
                {
                    elapsed += Time.deltaTime;
                    float envelope = Mathf.Clamp01(1 - elapsed / 1.7f);
                    SetFlash(.22f * envelope, opponentLost ? gold : blood);
                    if (opponentLost && OpponentVisual)
                        OpponentVisual.localRotation = opponentRotation * Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 26) * 1.4f * envelope);
                    if (!opponentLost && WorldCamera)
                        WorldCamera.transform.position = cameraPosition + new Vector3(Mathf.Sin(elapsed * 21), Mathf.Cos(elapsed * 17), 0) * .035f * envelope;
                    yield return null;
                }
                if (opponentLost && OpponentTint) OpponentTint.color = new Color(.34f,.30f,.25f,1);
                if (OpponentVisual) OpponentVisual.localRotation = opponentRotation;
                if (WorldCamera) WorldCamera.transform.position = cameraPosition;
                SetFlash(0, gold);
            }
            stage = Stage.Finished;
            SetText(EndTitle, match.State.IsDraw ? "KHÔNG AI LÊN NGÔI" : match.State.WinnerSeat == 0 ? "BẠN LÀ VUA CUỐI CÙNG" : "NGÔI VUA ĐÃ ĐỔI CHỦ");
            SetText(EndReasonText, EndDescription(match.State) + "\n\n" + match.State.TurnIndex + " lượt đấu  ·  Xu Máu: Bạn " + match.State.Players[0].BloodCoins + " — AI " + match.State.Players[1].BloodCoins);
            SetActive(EndPanel, true); UpdateControls();
        }

        void RenderState(MatchState state)
        {
            SetText(TurnText, "LƯỢT " + (state.IsFinished ? state.TurnIndex : state.TurnIndex + 1));
            SetText(PlayerHandText, "BẠN\n" + CountHand(state.Players[0]));
            SetText(OpponentHandText, "ĐỐI THỦ\n" + CountHand(state.Players[1]));
            SetText(PlayerCoinText, "BẠN:  " + state.Players[0].BloodCoins + " / 5 XU MÁU");
            SetText(OpponentCoinText, "AI:  " + state.Players[1].BloodCoins + " / 5 XU MÁU");
            SetText(PlayerBpmText, "BẠN: " + (70 + state.Players[0].BloodCoins * 16) + " BPM");
            SetText(OpponentBpmText, "AI: " + (70 + state.Players[1].BloodCoins * 16) + " BPM");
            SetText(DeckText, "DÂN TRONG XẤP: " + state.DeckCount + "\nĐÃ TIÊU HỦY: " + state.DestroyedCount);
            SetText(InitiativeText, "ƯU TIÊN RÚT CUỐI: " + (state.InitiativeSeat == 0 ? "BẠN" : "AI"));
            SetText(HistoryText, history.Count == 0 ? "Lịch sử lượt sẽ hiện tại đây." : string.Join("\n", history.ToArray()));
            for (int i = 0; i < CardButtons.Length; i++)
            {
                bool present = i < state.Players[0].Hand.Count;
                CardButtons[i].gameObject.SetActive(present);
                if (!present) continue;
                CardKind kind = state.Players[0].Hand[i].Kind;
                CardImages[i].sprite = CardSprites[(int)kind];
                CardImages[i].preserveAspect = true;
                SetText(CardLabels[i], (i + 1) + "  ·  " + CardName(kind));
            }
            if (PlayerCoinText) PlayerCoinText.color = state.Players[0].BloodCoins >= 4 ? blood : cream;
            if (OpponentCoinText) OpponentCoinText.color = state.Players[1].BloodCoins >= 4 ? blood : cream;
            UpdateControls();
        }

        void UpdateControls()
        {
            bool choosing = stage == Stage.Choosing && !paused;
            for (int i = 0; i < CardButtons.Length; i++)
            {
                CardButtons[i].interactable = choosing;
                if (i < SelectionMarks.Length && SelectionMarks[i]) SelectionMarks[i].SetActive(choosing && selected == i);
            }
            LockButton.interactable = choosing && selected >= 0;
            SetText(LockButtonText, "KHÓA BÀI\n<size=15>ENTER / SPACE</size>");
            PauseButton.interactable = stage != Stage.Welcome;
        }

        void TogglePause()
        {
            if (stage == Stage.Welcome) return;
            paused = !paused; Time.timeScale = paused ? 0 : 1;
            SetActive(PausePanel, paused); UpdateControls();
        }

        static string CountHand(PlayerState player)
        {
            int[] counts = new int[3]; foreach (Card c in player.Hand) counts[(int)c.Kind]++;
            return counts[0] + " VUA  ·  " + counts[1] + " DÂN  ·  " + counts[2] + " NÔ LỆ";
        }
        static string SeatName(int seat) { return seat == 0 ? "BẠN" : "AI"; }
        public static string CardName(CardKind kind) { return kind == CardKind.King ? "VUA" : kind == CardKind.Citizen ? "DÂN" : "NÔ LỆ"; }
        static string Describe(TurnResult r)
        {
            CardKind a = r.PlayedCards[0].Kind, b = r.PlayedCards[1].Kind;
            if (r.After.IsFinished && r.After.EndReason == MatchEndReason.BloodCoins)
                return (a == b ? "THUẾ MÁU: " : "") + SeatName(r.CoinRecipientSeat) + " nhận Xu Máu thứ 5. Trận kết thúc trước bước chuyển hóa và tiêu hủy bài.";
            if (a == b)
            {
                if (a == CardKind.Slave) return "Bạo loạn vô nghĩa: hai Nô lệ bị tiêu hủy. Không nhận xu.";
                return r.CoinRecipientSeat >= 0 ? "THUẾ MÁU: " + SeatName(r.CoinRecipientSeat) + " giữ Vua nhưng đánh Dân. +1 Xu Máu." : "Hai Dân bị tiêu hủy. Không có người giữ Vua trên tay để chịu thuế.";
            }
            CardKind winning = r.PlayedCards[r.PairWinnerSeat].Kind;
            string consequence = winning == CardKind.King ? "Dân thua chuyển thành Nô lệ." : winning == CardKind.Slave ? "Nô lệ thành Vua; Vua cũ bị tiêu hủy." : "Nô lệ bị tiêu hủy; Dân trở về tay.";
            return SeatName(r.CoinRecipientSeat) + " nhận 1 Xu Máu. " + consequence;
        }
        static string EndDescription(MatchState state)
        {
            if (state.IsDraw) return "Không xác định người thắng. Cả hai được giữ lại cho một ván khác.";
            string loser = state.WinnerSeat == 0 ? "AI" : "Bạn";
            switch (state.EndReason)
            {
                case MatchEndReason.BloodCoins: return loser + " đã nhận đủ 5 Xu Máu.";
                case MatchEndReason.SocialCollapse: return "VUA KHÔNG NGAI: không còn lá Dân hoạt động. " + loser + " đang giữ Vua và bị xử thua.";
                case MatchEndReason.EmptyHand: return loser + " đã hết bài khi xấp chung cạn.";
                default: return "Trận đấu đã kết thúc.";
            }
        }
        static void SetText(Text text, string value) { if (text) text.text = value; }
        static void SetActive(GameObject obj, bool value) { if (obj) obj.SetActive(value); }
        void SetFlash(float alpha, Color tint) { if (ScreenFlash) { tint.a = alpha; ScreenFlash.color = tint; ScreenFlash.raycastTarget = false; } }
        void Play(AudioClip clip) { if (SoundEnabled && AudioSource && clip) AudioSource.PlayOneShot(clip); }
        void OnDestroy()
        {
            if (Physics) Physics.OnDeposited -= OnCoinDeposited;
            Application.logMessageReceived -= OnSmokeLog;
            Time.timeScale = 1;
        }

        void OnSmokeLog(string message, string stack, LogType type)
        {
            if (!smoke || smokeFailed || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)) return;
            smokeFailed = true;
            File.WriteAllText(Path.Combine(smokeOutput, "smoke-result.txt"), "FAIL: " + message + "\n" + stack);
            Application.Quit(2);
        }

        IEnumerator SmokeRun()
        {
            string output = smokeOutput;
            Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-welcome.png"));
            yield return new WaitForSeconds(.3f);
            BeginMatch();
            paused = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-table.png"));
            yield return new WaitForSeconds(.3f);
            yield return LastKingPhysicsChecks.Run(Physics, message => Debug.Log(message));
            File.WriteAllText(Path.Combine(output, "physics-passed.txt"), "PASS: real Unity Physics2D — wrong-pan rejection, distinct masses, pause, unassisted gravity settling, beam movement, ten-coin capacity, recovery, exactly-once deposits and reset.");
            BeginMatch();
            RevealSeconds = .5f;
            bool sawCoin = false, sawTransform = false;
            int attempts = 0;
            float deadline = Time.realtimeSinceStartup + 150;
            while (attempts < 4 && Time.realtimeSinceStartup < deadline && !smokeFailed && (!sawCoin || !sawTransform || stage != Stage.Finished))
            {
                if (stage == Stage.Finished) { attempts++; if (sawCoin && sawTransform) break; BeginMatch(); }
                if (stage == Stage.Choosing)
                {
                    int choice = LastKingAI.ChooseCard(match.State, 0, new System.Random(700 + match.State.TurnIndex + attempts));
                    // Deterministic smoke exercises transformation on the first turn.
                    if (match.State.TurnIndex == 0) { choice = 0; aiChoice = 1; }
                    int oldCitizens = LastKingRules.ActiveCitizenCount(match.State);
                    SelectCard(choice); LockSelection();
                    if (LastKingRules.ActiveCitizenCount(match.State) != oldCitizens) sawTransform = true;
                }
                if (stage == Stage.Depositing && !sawCoin)
                {
                    yield return new WaitForSeconds(.4f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-coin-placement.png"));
                    sawCoin = true;
                }
                yield return null;
            }
            yield return new WaitForSeconds(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "04-result.png"));
            bool passed = !smokeFailed && stage == Stage.Finished && sawCoin && sawTransform;
            File.WriteAllText(Path.Combine(output, "smoke-result.txt"), passed ? "PASS: serialized Canvas, card selection, transformation, physical coin placement, complete match and chair ending." : "FAIL: runtime flow did not reach all checkpoints.");
            yield return new WaitForSeconds(.5f);
            Application.Quit(passed ? 0 : 2);
        }
    }
}
