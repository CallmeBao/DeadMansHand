using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace RevolverMVP
{
    // All positions are in a 1024x576 reference canvas. Physics uses the same mapping.
    public sealed partial class MatchController
    {
        const float CanvasWidth=1024, CanvasHeight=576, PixelsPerWorldUnit=68;
        static readonly Vector2 CylinderCenter=new Vector2(512,298);
        BrassSkin skin;
        Texture2D roomArt, cardAtlas, paper;
        readonly Rect[] cardUV = new Rect[3];
        bool previewMode;
        float lampPulse;
        GUIStyle textStyle;
        static readonly Color Brass=new Color(.78f,.62f,.34f);
        static readonly Color Paper=new Color(.87f,.77f,.55f);
        static readonly Color Blood=new Color(.69f,.21f,.13f);
        static readonly Color Dark=new Color(.035f,.025f,.014f);

        void InitializeView()
        {
            skin=new BrassSkin();
            roomArt=Resources.Load<Texture2D>("Art/room");
            cardAtlas=Resources.Load<Texture2D>("Art/cards");
            if(roomArt) roomArt.filterMode=FilterMode.Point;
            if(cardAtlas) cardAtlas.filterMode=FilterMode.Point;
            cardUV[0]=new Rect(34f/1536,122f/1024,470f/1536,778f/1024);
            cardUV[1]=new Rect(532f/1536,122f/1024,474f/1536,778f/1024);
            cardUV[2]=new Rect(1032f/1536,122f/1024,472f/1536,778f/1024);
            paper=new Texture2D(64,96,TextureFormat.RGBA32,false);
            var rng=new System.Random(37);
            for(int y=0;y<96;y++) for(int x=0;x<64;x++) {
                float noise=(float)rng.NextDouble()*.13f;
                paper.SetPixel(x,y,new Color(.72f+noise,.59f+noise,.35f+noise));
            }
            paper.filterMode=FilterMode.Point; paper.Apply();
            previewMode=Array.IndexOf(Environment.GetCommandLineArgs(),"-uiPreview")>=0;
            if(previewMode) StartCoroutine(PreviewSequence());
        }
        void DisposeView() { if(skin!=null) skin.Dispose(); if(paper) Destroy(paper); }
        Vector2 ToCanvas(Vector2 world) { return CylinderCenter+new Vector2(world.x,-world.y)*PixelsPerWorldUnit; }
        int Bpm(int who) { return Mathf.RoundToInt(Mathf.Lerp(70,160,Mathf.Clamp01(players[who].BulletCount/5f))); }

        void OnGUI()
        {
            if(skin==null || font==null) return;
            GUI.matrix=Matrix4x4.identity;
            GUI.color=Color.white;
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.blackTexture);
            GUI.matrix=Matrix4x4.TRS(new Vector3(Offset.x,Offset.y,0),Quaternion.identity,new Vector3(Scale,Scale,1));
            GUI.skin.font=font;
            if(textStyle==null) textStyle=new GUIStyle(GUI.skin.label);
            DrawBackdrop();
            bool closeup=Penalty && loser>=0;
            if(closeup) DrawPenaltyScreen(); else DrawTableScreen();
            DrawUtility();
            if(flash>0) skin.Fill(new Rect(0,0,1024,576),new Color(.55f,.035f,.015f,flash*.6f));
            if(paused) DrawPause();
            GUI.color=Color.white; GUI.matrix=Matrix4x4.identity;
        }

        void DrawBackdrop()
        {
            if(roomArt) GUI.DrawTexture(new Rect(0,0,1024,576),roomArt,ScaleMode.StretchToFill);
            else {
                skin.Fill(new Rect(0,0,1024,576),new Color(.12f,.085f,.025f));
                skin.Panel(new Rect(145,293,734,88));
            }
            // Light breath is intentionally subtle, with an accessibility switch.
            lampPulse=reduced?0:(Mathf.Sin(Time.time*1.7f)+Mathf.Sin(Time.time*.47f))*.012f;
            skin.Fill(new Rect(0,0,1024,576),new Color(.035f,.02f,0,.10f+lampPulse));
            if(phase==Phase.Finished) {
                skin.Fill(new Rect(winner==0?800:0,158,224,380),new Color(0,0,0,.7f));
            }
        }

        void DrawHud(bool penalty)
        {
            skin.Fill(new Rect(0,0,1024,74),new Color(0,0,0,.46f));
            MiniCylinder(new Vector2(37,32),players[0].Chambers);
            MiniCylinder(new Vector2(817,32),players[1].Chambers);
            if(penalty) {
                Text("NHỊP TIM: "+Bpm(0)+" BPM",new Rect(71,13,235,26),17,Paper);
                Text("NHỊP TIM: "+Bpm(1)+" BPM",new Rect(853,13,165,26),16,Paper);
                skin.FearMeter(new Rect(72,43,156,6),players[0].Fear);
                skin.FearMeter(new Rect(854,43,154,6),players[1].Fear);
            } else {
                Text("SÚNG CỦA BẠN",new Rect(71,11,151,29),18,Paper);
                for(int i=0;i<6;i++) {
                    skin.Disc(new Vector2(80+i*19,51),6,players[0].Chambers[i]?Brass:new Color(.18f,.14f,.07f));
                    if(players[0].Chambers[i]) skin.Disc(new Vector2(80+i*19,50),3,Paper);
                }
                Text("SỢ HÃI: "+Mathf.RoundToInt(players[0].Fear*100)+"%",new Rect(16,76,200,29),20,Blood);
                Text("NHỊP TIM: "+Bpm(0)+" BPM",new Rect(226,12,168,23),15,Paper);
                skin.Heartbeat(new Rect(224,39,153,32),Time.time*Bpm(0)/60,new Color(1,.43f,.12f));
                skin.Panel(new Rect(397,10,231,73));
                Text("VÁN "+Mathf.Max(1,game)+" · LƯỢT "+Mathf.Max(1,turn)+"/4",new Rect(408,24,210,42),22,Paper,TextAnchor.MiddleCenter);
                skin.Panel(new Rect(639,23,108,48));
                Text(phase==Phase.Choosing?Mathf.CeilToInt(Mathf.Max(0,timer))+" GIÂY":"CÒ QUAY",new Rect(648,32,90,28),17,Paper,TextAnchor.MiddleCenter);
                Text("SÚNG AI",new Rect(853,10,165,27),18,Paper);
                Text("ĐẠN:",new Rect(853,39,45,22),14,Paper);
                for(int i=0;i<6;i++) skin.Disc(new Vector2(910+i*18,49),5,players[1].Chambers[i]?Blood:new Color(.21f,.18f,.1f));
                Text(phase==Phase.Choosing?"AI: ĐÃ KHÓA KÍN":phase==Phase.Reveal?"AI: LẬT BÀI":"AI: CHỜ VÁN MỚI",new Rect(791,77,222,26),17,new Color(.58f,.54f,.42f),TextAnchor.MiddleRight);
            }
        }

        void DrawTableScreen()
        {
            DrawHud(false);
            if(phase==Phase.Ready) {
                skin.Panel(new Rect(286,188,452,157));
                Text("DEAD MAN’S HAND",new Rect(300,207,424,43),32,Paper,TextAnchor.MiddleCenter);
                Text("VUA > DÂN > NÔ LỆ > VUA",new Rect(300,258,424,28),18,Brass,TextAnchor.MiddleCenter);
                Text("Thắng bằng lá bài. Sống sót bằng đôi tay.",new Rect(300,295,424,26),16,Paper,TextAnchor.MiddleCenter);
                if(ActionButton("BẮT ĐẦU / PLAY",new Rect(377,408,270,69),!paused)) NewMatch();
                return;
            }
            if(phase==Phase.Finished) {
                skin.Panel(new Rect(274,180,476,197),true);
                Text(winner==0?"BẠN SỐNG SÓT":"BẠN ĐÃ THUA",new Rect(300,203,424,53),35,Paper,TextAnchor.MiddleCenter);
                Text("Trận đấu kết thúc ở ván "+game+".",new Rect(300,264,424,30),19,Brass,TextAnchor.MiddleCenter);
                Text("Đạn trong ổ của bạn: "+players[0].BulletCount+"/6",new Rect(300,306,424,27),17,Paper,TextAnchor.MiddleCenter);
                if(ActionButton("CHƠI LẠI / REPLAY",new Rect(377,417,270,66),!paused)) NewMatch();
                return;
            }

            if(phase==Phase.Reveal) {
                DrawCard(revealedPlayer,new Rect(311,215,75,103));
                DrawCard(revealedAI,new Rect(638,215,75,103));
            } else {
                CardBack(new Rect(311,215,75,103));
                CardBack(new Rect(638,215,75,103));
            }
            skin.Panel(new Rect(254,340,517,69));
            Text("LỊCH SỬ VÁN",new Rect(273,357,170,31),19,Paper,TextAnchor.MiddleCenter);
            if(seenPlayer.Count==0) Text("Chưa có bài được lật",new Rect(459,362,289,26),15,new Color(.56f,.49f,.33f),TextAnchor.MiddleCenter);
            for(int i=0;i<seenPlayer.Count;i++) {
                DrawCard(seenPlayer[i],new Rect(448+i*60,351,24,44),false);
                if(i<seenAI.Count) DrawCard(seenAI[i],new Rect(474+i*60,351,24,44),false);
            }
            if(phase==Phase.Reveal || phase==Phase.Outcome)
                Text(status,new Rect(252,411,526,26),13,Paper,TextAnchor.MiddleCenter);
            else Text("Chọn một lá. Khóa trước khi hết giờ.",new Rect(255,411,520,22),13,Brass,TextAnchor.MiddleCenter);
            DrawCardFan();
            bool canLock=phase==Phase.Choosing && selected>=0 && !paused;
            if(ActionButton("KHÓA BÀI\n/ LOCK",new Rect(834,432,170,127),canLock)) Lock();
        }

        void DrawCardFan()
        {
            if(hand==null) return;
            int hover=-1;
            for(int i=0;i<hand.Count;i++) {
                float angle=(i-(hand.Count-1)*.5f)*3.8f;
                float x=512+(i-(hand.Count-1)*.5f)*101-46;
                Rect rect=new Rect(x,435+(selected==i?-13:0),92,128);
                bool over=RotatedContains(rect,angle,Pointer);
                if(over) hover=i;
                Matrix4x4 old=GUI.matrix;
                BrassSkin.RotateAt(angle,rect.center);
                if(selected==i || over && phase==Phase.Choosing) {
                    for(int spread=8;spread>=2;spread-=2) skin.Fill(new Rect(rect.x-spread,rect.y-spread,rect.width+2*spread,rect.height+2*spread),new Color(1,.81f,.19f,.075f));
                    skin.Fill(new Rect(rect.x-3,rect.y-3,rect.width+6,rect.height+6),new Color(1,.83f,.26f));
                }
                DrawCard(hand[i],rect);
                bool enabled=GUI.enabled; GUI.enabled=phase==Phase.Choosing && !paused;
                if(GUI.Button(rect,GUIContent.none,GUIStyle.none)) { selected=i; Sound(tick); }
                GUI.enabled=enabled; GUI.matrix=old;
            }
            if(hover>=0 && phase==Phase.Choosing && !paused) skin.Hand(Pointer+new Vector2(3,8),.55f);
        }

        void DrawPenaltyScreen()
        {
            DrawHud(true);
            Text("HÌNH PHẠT: CÒ QUAY NGA",new Rect(251,48,522,43),31,Blood,TextAnchor.MiddleCenter);
            string instruction=phase==Phase.Loading?"Kéo viên đạn vào khe trống, thả khi đã khớp":
                phase==Phase.AwaitSpin?"Giữ vành ngoài, vuốt dọc rồi thả để xoay":
                phase==Phase.Spinning?"Đang quay · chờ ổ đạn khóa vào chốt":
                phase==Phase.Trigger?"Chốt đã khóa · bóp cò để phán quyết":"CẠCH! Sống sót · đạn được giữ lại";
            Text(instruction,new Rect(222,90,580,26),16,Paper,TextAnchor.MiddleCenter);
            skin.Panel(new Rect(249,115,527,356));
            skin.Fill(new Rect(266,135,493,319),new Color(.05f,.033f,.01f,.32f));
            var owner=players[loser];
            skin.Cylinder(CylinderCenter,1.25f*PixelsPerWorldUnit/.65f,angles[loser],owner.Chambers);
            Text("KIM HỎA",new Rect(437,139,150,29),19,Blood,TextAnchor.MiddleCenter);
            // Pin stays fixed while the cylinder rotates beneath it.
            skin.Line(new Vector2(512,174),new Vector2(512,196),10,Blood);
            skin.Line(new Vector2(512,174),new Vector2(502,184),5,Blood);
            skin.Line(new Vector2(512,174),new Vector2(522,184),5,Blood);
            for(int i=0;i<6;i++) {
                Vector2 p=ToCanvas(SlotPosition(i));
                Vector2 label=Vector2.Lerp(CylinderCenter,p,1.35f);
                Text((i+1).ToString(),new Rect(label.x-10,label.y-12,20,24),15,Paper,TextAnchor.MiddleCenter);
                if(phase==Phase.Loading && loser==0 && !owner.Chambers[i] && Vector2.Distance(bullet.Position,SlotPosition(i))<.32f)
                    skin.Disc(p,7,new Color(1,.88f,.34f,.7f));
            }
            Text(loser==0?"SÚNG CỦA BẠN":"SÚNG CỦA AI",new Rect(283,417,206,27),16,Brass);
            Text(owner.BulletCount+" / 6 VIÊN",new Rect(577,417,163,27),16,Brass,TextAnchor.MiddleRight);
            Text(phase==Phase.Spinning?Math.Abs(cylinder.AngularVelocity).ToString("0.0")+" rad/s":
                phase==Phase.Outcome?"AN TOÀN":Mathf.CeilToInt(Mathf.Max(0,timer))+" GIÂY",
                new Rect(652,145,92,25),13,Paper,TextAnchor.MiddleRight);

            skin.Panel(new Rect(834,432,170,126));
            Text("KHAY ĐẠN",new Rect(833,399,171,28),20,Paper,TextAnchor.MiddleCenter);
            Text("ĐỘ RUN TAY: "+Mathf.RoundToInt(Mathf.Clamp01(owner.BulletCount/5f)*100)+"%",
                new Rect(23,461,250,30),19,Paper);
            skin.FearMeter(new Rect(22,495,164,33),owner.BulletCount/5f);
            skin.Heartbeat(new Rect(190,477,119,50),Time.time*Bpm(loser)/60,new Color(1,.38f,.13f));
            Text("LỰC KÉO: "+(owner.BulletCount>=3?"NẶNG":"NHẸ"),new Rect(23,533,248,27),17,Paper);
            bool canFire=phase==Phase.Trigger && loser==0 && !paused;
            if(ActionButton(phase==Phase.Spinning?"ĐANG XOAY…":"BÓP CÒ / PULL TRIGGER",new Rect(386,497,253,59),canFire)) Fire();
            if(phase==Phase.Loading) {
                Vector2 b=ToCanvas(bullet.Position);
                skin.Bullet(b,33,-25);
                if(bullet.IsHeld) {
                    // Visual spring links the real cursor to the independently simulated cartridge.
                    Vector2 start=b, end=Pointer; Vector2 delta=end-start;
                    if(delta.magnitude>8) {
                        Vector2 normal=new Vector2(-delta.y,delta.x).normalized;
                        Vector2 last=start;
                        for(int k=1;k<=24;k++) {
                            Vector2 next=Vector2.Lerp(start,end,k/24f)+normal*Mathf.Sin(k*Mathf.PI*.75f)*5;
                            skin.Line(last,next,1.6f,Brass); last=next;
                        }
                    }
                    skin.Hand(b+new Vector2(8,8),.95f);
                }
            } else Text("ĐÃ NẠP",new Rect(850,479,140,40),18,new Color(.43f,.37f,.23f),TextAnchor.MiddleCenter);
            if(loser==1) Text("AI ĐANG THỰC HIỆN",new Rect(327,473,370,22),13,Brass,TextAnchor.MiddleCenter);
        }

        void DrawUtility()
        {
            // Compact utilities stay away from the five playable cards and the physical tray.
            if(SmallButton(paused?"TIẾP TỤC":"TẠM DỪNG",new Rect(12,112,110,24))) TogglePause();
            if(SmallButton(muted?"ÂM: TẮT":"ÂM: BẬT",new Rect(12,140,110,24))) muted=!muted;
            if(SmallButton(reduced?"HIỆU ỨNG: ÍT":"HIỆU ỨNG: ĐỦ",new Rect(12,168,110,24))) reduced=!reduced;
        }
        void DrawPause()
        {
            skin.Fill(new Rect(0,0,1024,576),new Color(0,0,0,.79f));
            skin.Panel(new Rect(294,173,436,231));
            Text("ĐÃ TẠM DỪNG",new Rect(316,202,392,47),30,Paper,TextAnchor.MiddleCenter);
            Text("Đồng hồ và vật lý đều dừng.",new Rect(316,257,392,35),17,Brass,TextAnchor.MiddleCenter);
            if(ActionButton("TIẾP TỤC",new Rect(374,317,276,62),true)) TogglePause();
        }

        void DrawCard(CardKind kind,Rect rect,bool caption=true)
        {
            skin.Fill(new Rect(rect.x-2,rect.y-2,rect.width+4,rect.height+4),new Color(.025f,.019f,.012f));
            if(cardAtlas) GUI.DrawTextureWithTexCoords(rect,cardAtlas,cardUV[(int)kind],true);
            else { GUI.DrawTexture(rect,paper); Text(Name(kind),rect,18,Dark,TextAnchor.MiddleCenter); }
            if(caption && rect.width>50) {
                Rect band=new Rect(rect.x+4,rect.yMax-27,rect.width-8,21);
                GUI.DrawTexture(band,paper);
                Text(Name(kind),band,rect.width>85?15:12,Dark,TextAnchor.MiddleCenter,false);
            }
        }
        void CardBack(Rect rect)
        {
            skin.Fill(new Rect(rect.x-3,rect.y-3,rect.width+6,rect.height+6),Dark);
            GUI.DrawTexture(rect,paper);
            skin.Line(new Vector2(rect.x+5,rect.y+6),new Vector2(rect.xMax-6,rect.y+6),2,Brass);
            skin.Line(new Vector2(rect.x+5,rect.yMax-6),new Vector2(rect.xMax-6,rect.yMax-6),2,Brass);
            Text("?",rect,63,new Color(.18f,.1f,.015f),TextAnchor.MiddleCenter,false);
        }
        void MiniCylinder(Vector2 pos,bool[] loaded) { skin.Cylinder(pos,23,0,loaded); }
        bool ActionButton(string label,Rect rect,bool enabled)
        {
            Color old=GUI.color;
            if(!enabled) GUI.color=new Color(.55f,.5f,.4f,1);
            else if(rect.Contains(Pointer)) GUI.color=new Color(1.2f,1.15f,1,1);
            skin.Panel(rect,true); Text(label,new Rect(rect.x+10,rect.y+7,rect.width-20,rect.height-14),rect.height>90?23:18,Paper,TextAnchor.MiddleCenter);
            GUI.color=old;
            bool oldEnabled=GUI.enabled; GUI.enabled=enabled;
            bool pressed=GUI.Button(rect,GUIContent.none,GUIStyle.none);
            GUI.enabled=oldEnabled; return pressed;
        }
        bool SmallButton(string label,Rect rect)
        {
            skin.Fill(rect,new Color(.025f,.015f,0,.66f));
            Text(label,rect,11,new Color(.65f,.56f,.36f),TextAnchor.MiddleCenter);
            return !paused && GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }
        void Text(string value,Rect rect,int size,Color color,TextAnchor alignment=TextAnchor.UpperLeft,bool shadow=true)
        {
            textStyle.font=font; textStyle.fontSize=size; textStyle.fontStyle=FontStyle.Bold;
            textStyle.alignment=alignment; textStyle.wordWrap=true; textStyle.padding=new RectOffset();
            if(shadow) {
                textStyle.normal.textColor=new Color(0,0,0,.95f);
                GUI.Label(new Rect(rect.x+2,rect.y+2,rect.width,rect.height),value,textStyle);
            }
            textStyle.normal.textColor=color; GUI.Label(rect,value,textStyle);
        }
        static bool RotatedContains(Rect rect,float degrees,Vector2 p)
        {
            float a=-degrees*Mathf.Deg2Rad; Vector2 d=p-rect.center;
            return rect.Contains(rect.center+new Vector2(d.x*Mathf.Cos(a)-d.y*Mathf.Sin(a),d.x*Mathf.Sin(a)+d.y*Mathf.Cos(a)));
        }

        IEnumerator PreviewSequence()
        {
            // Reproducible art QA, independent of the gameplay smoke run.
            string dir=Path.Combine(Application.dataPath,"..","Preview"); Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(2);
            NewMatch(); selected=3; players[0].Chambers[2]=true; players[0].Chambers[3]=true;
            players[0].Survive(); players[0].Survive();
            players[1].Chambers[1]=true; game=3; turn=2;
            seenPlayer.Add(CardKind.Citizen); seenAI.Add(CardKind.Citizen);
            yield return new WaitForSeconds(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,"01-table.png"));
            yield return new WaitForSeconds(2);
            BeginPenalty(0);
            yield return new WaitForSeconds(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,"02-loading.png"));
            yield return new WaitForSeconds(2);
            bullet.Grab(bullet.Position); bullet.MoveTarget(SlotPosition(1)+new Vector2(.5f,-.5f));
            yield return new WaitForSeconds(1);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,"03-hand.png"));
            yield return new WaitForSeconds(1);
            bullet.Release(); Load(1); Spin(24);
            while(phase==Phase.Spinning) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,"04-trigger.png"));
            yield return new WaitForSeconds(1);
            NewMatch(); File.WriteAllText(Path.Combine(dir,"finished.txt"),"Visual preview sequence complete.");
            // Leave a normal interactive match for manual input verification.
            previewMode=false; smokeMode=false;
        }
    }
}

