using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RevolverMVP
{
    // Presentation and local authority. Rules and rotational simulation are separate pure C# modules.
    public sealed partial class MatchController : MonoBehaviour
    {
        enum Phase { Ready, Choosing, Reveal, Loading, AwaitSpin, Spinning, Trigger, Outcome, Finished }
        Phase phase;
        readonly PlayerState[] players = { new PlayerState(), new PlayerState() };
        readonly System.Random random = new System.Random();
        List<CardKind> hand, aiHand;
        readonly List<CardKind> seenPlayer = new List<CardKind>();
        readonly List<CardKind> seenAI = new List<CardKind>();
        readonly List<string> history = new List<string>();
        readonly double[] angles = new double[2];
        CylinderDynamics cylinder;
        BulletDragPhysics bullet;
        int game, turn, selected = -1, aiChoice, loser, winner = -1, revealedResult;
        CardKind revealedPlayer, revealedAI;
        float timer, phaseAge, zoom, flash, spinStartTime;
        bool paused, reduced, muted, flickHeld, smokeMode;
        Vector2 lastFlick;
        float flickImpulse;
        string status = "Một bàn. Hai người. Chỉ một người rời đi.";
        Font font;
        
        AudioSource audioSource;
        AudioClip tick, shot, heartbeat;
        float nextBeat;
        int previousSlot;
        static readonly Color Gold = new Color(.83f,.66f,.39f), Cream = new Color(.9f,.88f,.81f), Muted = new Color(.48f,.57f,.56f);
        float Scale { get { return Mathf.Min(Screen.width / CanvasWidth, Screen.height / CanvasHeight); } }
        Vector2 Offset { get { return new Vector2((Screen.width - CanvasWidth * Scale)/2, (Screen.height - CanvasHeight*Scale)/2); } }
        Vector2 Pointer { get { return (new Vector2(Input.mousePosition.x, Screen.height-Input.mousePosition.y)-Offset)/Scale; } }
        Vector2 WorldPointer { get { var p=Pointer; return new Vector2((p.x-CylinderCenter.x)/PixelsPerWorldUnit, (CylinderCenter.y-p.y)/PixelsPerWorldUnit); } }
        bool Penalty { get { return loser >= 0 && phase >= Phase.Loading && phase <= Phase.Outcome; } }

        void Start()
        {
            Time.timeScale=1; Time.fixedDeltaTime=1f/120; Application.targetFrameRate=60;
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            InitializeView();
            bullet=new GameObject("Spring-damper hand and physical cartridge").AddComponent<BulletDragPhysics>();
            bullet.transform.SetParent(transform); bullet.Initialize(); bullet.Hide();
            audioSource=gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake=false;
            tick=Tone(620,.045f,.16f); shot=Tone(75,.28f,.5f); heartbeat=Tone(52,.12f,.18f);
            smokeMode=Array.IndexOf(Environment.GetCommandLineArgs(),"-mvpSmoke")>=0;
            if(smokeMode) StartCoroutine(Smoke());
        }

        void NewMatch()
        {
            foreach(var p in players) p.Reset(); angles[0]=angles[1]=0;
            winner=-1; loser=-1; cylinder=null; zoom=0; game=0; paused=false; Time.timeScale=1; bullet.Hide(); Deal();
        }
        void Enter(Phase next) { phase=next; phaseAge=0; flickHeld=false; }
        void Deal()
        {
            game++; turn=0; hand=CardRules.NewHand(); aiHand=CardRules.NewHand();
            seenPlayer.Clear(); seenAI.Clear(); history.Clear(); ChooseTurn();
        }
        void ChooseTurn()
        {
            turn++; selected=-1;
            // AI commits before player input; the selector receives only public evidence.
            aiChoice=CardAI.Choose(aiHand,seenPlayer,random);
            timer=15; Enter(Phase.Choosing); status="Chọn một lá rồi KHÓA. Đối thủ đã khóa kín.";
        }
        void Lock()
        {
            if(phase!=Phase.Choosing) return;
            if(selected<0) selected=random.Next(hand.Count);
            revealedPlayer=hand[selected]; revealedAI=aiHand[aiChoice];
            hand.RemoveAt(selected); aiHand.RemoveAt(aiChoice); seenPlayer.Add(revealedPlayer); seenAI.Add(revealedAI); selected=-1;
            revealedResult=CardRules.Compare(revealedPlayer,revealedAI);
            history.Add(turn.ToString("00")+"    "+Name(revealedPlayer)+"  /  "+Name(revealedAI));
            status=revealedResult==0?"Hai lá hòa nhau.":revealedResult>0?"Bạn thắng ván bài. Đối thủ nhận hình phạt.":"Bạn thua ván bài. Đến lượt khẩu súng của bạn.";
            Sound(tick); Enter(Phase.Reveal);
        }
        void EndReveal()
        {
            if(revealedResult!=0) { BeginPenalty(revealedResult>0?1:0); return; }
            if(CardRules.IsEarlyDraw(hand,aiHand,turn)) {
                for(int i=0;i<hand.Count;i++) { history.Add("LẬT NỐT    "+Name(hand[i])+" / "+Name(aiHand[i])); seenPlayer.Add(hand[i]); seenAI.Add(aiHand[i]); }
                status="HÒA VÁN — lật các lá còn lại, chia lại. Không thêm đạn.";
                Enter(Phase.Outcome); loser=-1; timer=2.5f;
            } else ChooseTurn();
        }
        void BeginPenalty(int who)
        {
            loser=who; cylinder=null; timer=30; Enter(Phase.Loading);
            bullet.Begin(players[who].BulletCount,new Vector2(6f,-2.76f));
            status=who==0?"Giữ viên đạn đồng, kéo vào một khe trống và thả.":"Đối thủ đang nạp thêm một viên đạn…";
        }
        Vector2 SlotPosition(int i)
        {
            double a=CylinderDynamics.SlotAngle(angles[loser],i);
            return new Vector2((float)Math.Cos(a)*1.25f,(float)Math.Sin(a)*1.25f);
        }
        bool TryLoad()
        {
            if(phase!=Phase.Loading) return false;
            for(int i=0;i<6;i++) if(!players[loser].Chambers[i] && Vector2.Distance(bullet.Position,SlotPosition(i))<.28f) {
                Load(i); return true;
            }
            return false;
        }
        void Load(int slot)
        {
            if(phase!=Phase.Loading || players[loser].Chambers[slot]) return;
            players[loser].Chambers[slot]=true; bullet.Hide(); Enter(Phase.AwaitSpin); timer=20;
            Sound(tick); status=loser==0?"Giữ và vuốt cạnh ổ đạn để xoay. Lực vuốt quyết định mô-men.":"Đối thủ chuẩn bị xoay ổ đạn…";
        }
        int EmptySlot() { for(int i=0;i<6;i++) if(!players[loser].Chambers[i]) return i; throw new InvalidOperationException("Cylinder full before loading"); }
        void Spin(float impulse)
        {
            if(phase!=Phase.AwaitSpin) return;
            cylinder=new CylinderDynamics(players[loser].Chambers,angles[loser]); cylinder.Start(impulse);
            Enter(Phase.Spinning); status="Ổ đạn đang quay — trọng lượng đạn kéo trọng tâm xuống dưới."; previousSlot=-1;
        }
        void Fire()
        {
            if(phase!=Phase.Trigger || cylinder==null || !cylinder.Settled) return;
            bool fired=players[loser].Chambers[cylinder.FinalSlot];
            if(fired) {
                winner=1-loser; flash=reduced?.15f:1; Sound(shot); Enter(Phase.Finished);
                status=winner==0?"BẠN SỐNG SÓT. Trận đấu kết thúc.":"ĐỐI THỦ SỐNG SÓT. Trận đấu kết thúc.";
            } else {
                players[loser].Survive(); Sound(tick); Enter(Phase.Outcome); timer=2.5f;
                status="CẠCH! Khe 12 giờ trống. Giữ nguyên đạn, sợ hãi tăng 15%.";
            }
        }

        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if(Debug.isDebugBuild && Input.GetKeyDown(KeyCode.F8) && !previewMode && !smokeMode) {
                previewMode=true; StartCoroutine(PreviewSequence());
            }
            if(Debug.isDebugBuild && Input.GetKeyDown(KeyCode.F12)) {
                string previewDir=Path.Combine(Application.dataPath,"..","Preview"); Directory.CreateDirectory(previewDir);
                ScreenCapture.CaptureScreenshot(Path.Combine(previewDir,"manual-"+DateTime.Now.ToString("HHmmss")+".png"));
            }
            zoom=Mathf.MoveTowards(zoom,Penalty?1:0,Time.unscaledDeltaTime*(reduced?20:2.8f));
            flash=Mathf.MoveTowards(flash,0,Time.unscaledDeltaTime*2);
            if(paused) return;
            phaseAge+=Time.deltaTime;
            if(phase==Phase.Choosing) { timer-=Time.deltaTime; if(timer<=0) Lock(); }
            else if(phase==Phase.Reveal && phaseAge>1.7f) EndReveal();
            else if(phase==Phase.Loading) {
                timer-=Time.deltaTime;
                if(loser==1 && phaseAge>1.6f) Load(EmptySlot());
                else if(timer<=0) { Load(EmptySlot()); status="Hết giờ nạp: tự nạp vào khe trống đầu tiên."; }
                else if(loser==0 && !smokeMode && !previewMode) {
                    if(Input.GetMouseButtonDown(0) && Vector2.Distance(WorldPointer,bullet.Position)<.4f) bullet.Grab(WorldPointer);
                    if(bullet.IsHeld) bullet.MoveTarget(WorldPointer);
                    if(Input.GetMouseButtonUp(0) && bullet.IsHeld) { bullet.Release(); TryLoad(); }
                }
            } else if(phase==Phase.AwaitSpin) {
                timer-=Time.deltaTime;
                if(loser==1 && phaseAge>1.2f || timer<=0) Spin((float)(22+random.NextDouble()*45));
                else if(loser==0 && !smokeMode && !previewMode) {
                    float radius=WorldPointer.magnitude;
                    if(Input.GetMouseButtonDown(0) && radius>1.4f && radius<2.1f) { flickHeld=true; lastFlick=WorldPointer; spinStartTime=Time.time; flickImpulse=0; }
                    if(flickHeld && Input.GetMouseButton(0)) {
                        Vector2 current=WorldPointer, velocity=(current-lastFlick)/Mathf.Max(Time.deltaTime,.001f);
                        float torque=lastFlick.x*velocity.y-lastFlick.y*velocity.x;
                        flickImpulse+=torque*Time.deltaTime; lastFlick=current;
                    }
                    if(flickHeld && Input.GetMouseButtonUp(0)) {
                        flickHeld=false;
                        float impulse=Mathf.Clamp(flickImpulse*3/Mathf.Max(.05f,Time.time-spinStartTime),-80,80);
                        if(Mathf.Abs(impulse)>1) Spin(impulse);
                        else status="Vuốt một đoạn dọc theo vành ổ đạn; chỉ bấm chưa tạo lực xoay.";
                    }
                }
            } else if(phase==Phase.Trigger) {
                timer-=Time.deltaTime; if(loser==1 && phaseAge>1.6f || timer<=0) Fire();
            } else if(phase==Phase.Outcome && phaseAge>timer) Deal();
            if(Penalty && loser>=0 && Time.time>=nextBeat) {
                nextBeat=Time.time+60f/Mathf.Lerp(70,160,players[loser].BulletCount/5f); Sound(heartbeat);
            }
        }
        void FixedUpdate()
        {
            if(paused || phase!=Phase.Spinning) return;
            cylinder.Step(Time.fixedDeltaTime); angles[loser]=cylinder.Angle;
            int slot=(int)Math.Floor(cylinder.Angle/(Math.PI/3));
            if(slot!=previousSlot) { Sound(tick); previousSlot=slot; }
            if(cylinder.Settled) { Enter(Phase.Trigger); timer=10; status="Chốt đã khóa. Búa kim hỏa nhắm vào khe ở vị trí 12 giờ."; }
        }
        void TogglePause() { paused=!paused; Time.timeScale=paused?0:1; if(paused) { bullet.Release(); flickHeld=false; } }
        static string Name(CardKind c) { return c==CardKind.King?"VUA":c==CardKind.Citizen?"DÂN":"NÔ LỆ"; }

        AudioClip Tone(float hz,float seconds,float volume)
        {
            int count=(int)(44100*seconds); float[] data=new float[count];
            for(int i=0;i<count;i++) data[i]=Mathf.Sin(i*hz*2*Mathf.PI/44100)*Mathf.Exp(-7f*i/count)*volume;
            var clip=AudioClip.Create("Procedural cue",count,1,44100,false); clip.SetData(data,0); return clip;
        }
        void Sound(AudioClip clip) { if(!muted && audioSource && clip) audioSource.PlayOneShot(clip); }
        void OnDestroy() { Time.timeScale=1; DisposeView(); if(tick) Destroy(tick); if(shot) Destroy(shot); if(heartbeat) Destroy(heartbeat); }

        IEnumerator Smoke()
        {
            string dir=Path.Combine(Application.dataPath,"..","Smoke"); Directory.CreateDirectory(dir);
            yield return null; NewMatch();
            yield return new WaitForSeconds(.2f); ScreenCapture.CaptureScreenshot(Path.Combine(dir,"01-cards.png"));
            // Integration checks for card branches, timers, pause and reset before exercising physics.
            selected=0; aiChoice=0; Lock(); EndReveal();
            selected=3; aiChoice=3; Lock(); EndReveal();
            if(phase!=Phase.Outcome || loser!=-1) throw new Exception("SMOKE: early draw failed");
            NewMatch(); timer=0; yield return null;
            if(phase!=Phase.Reveal) throw new Exception("SMOKE: timeout failed");
            NewMatch(); selected=0; aiChoice=1; Lock(); EndReveal();
            if(phase!=Phase.Loading || loser!=1) throw new Exception("SMOKE: AI penalty failed");
            // Watch the entire AI loading/spinning/trigger chain.
            float aiDeadline=Time.time+20;
            while(phase!=Phase.Outcome && phase!=Phase.Finished && Time.time<aiDeadline) yield return null;
            if(phase!=Phase.Outcome && phase!=Phase.Finished) throw new Exception("SMOKE: AI flow stalled");
            NewMatch(); selected=1; aiChoice=0; Lock(); EndReveal();
            if(phase!=Phase.Loading || loser!=0) throw new Exception("SMOKE: player penalty failed");
            TogglePause(); float stopped=timer;
            yield return new WaitForSecondsRealtime(.15f);
            if(timer!=stopped || Time.timeScale!=0) throw new Exception("SMOKE: pause failed");
            TogglePause(); NewMatch();
            // Exercise spring loading and persistent rounds until the first fired chamber (at most six).
            for(int loss=0;loss<6;loss++) {
                BeginPenalty(0); yield return new WaitForSeconds(.4f);
                int slot=EmptySlot(); bullet.Grab(bullet.Position); bullet.MoveTarget(SlotPosition(slot));
                float deadline=Time.time+5;
                while(Vector2.Distance(bullet.Position,SlotPosition(slot))>=.24f && Time.time<deadline) yield return null;
                if(loss==0) ScreenCapture.CaptureScreenshot(Path.Combine(dir,"02-loading.png"));
                bullet.Release();
                if(!TryLoad()) { Debug.LogError("SMOKE: spring loading did not converge"); Application.Quit(2); yield break; }
                Spin(35); while(phase==Phase.Spinning) yield return null;
                if(phase!=Phase.Trigger) { Application.Quit(3); yield break; }
                if(loss==0) { ScreenCapture.CaptureScreenshot(Path.Combine(dir,"03-trigger.png")); yield return new WaitForSeconds(.2f); }
                Fire(); if(phase==Phase.Finished) break;
            }
            if(phase!=Phase.Finished) { Debug.LogError("SMOKE: full cylinder did not end match"); Application.Quit(4); yield break; }
            yield return new WaitForSeconds(.6f); ScreenCapture.CaptureScreenshot(Path.Combine(dir,"04-result.png"));
            File.WriteAllText(Path.Combine(dir,"passed.txt"),"PASS: physical spring load, persistent chambers, cylinder settling, trigger and match end.");
            yield return new WaitForSeconds(.5f); Application.Quit(0);
        }
    }
}
