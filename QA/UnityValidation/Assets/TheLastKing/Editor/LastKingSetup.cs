using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheLastKing.Editor
{
    /// <summary>Authoring tool: all visible objects and references are saved in the scene and prefabs.</summary>
    [InitializeOnLoad]
    public static class LastKingSetup
    {
        const string Root = "Assets/TheLastKing/";
        public const string ScenePath = Root + "Scenes/LastKingTable.unity";
        const string RequestPath = "QA/TheLastKing/setup-request.txt";
        static readonly Color Gold = new Color32(219,193,122,255);
        static readonly Color Cream = new Color32(236,219,181,255);
        static readonly Color Muted = new Color32(171,157,130,255);
        static readonly Color Blood = new Color32(181,74,53,255);
        static readonly Color Panel = new Color32(23,18,11,235);
        static Sprite square, circle;
        static Font font;
        static bool working;

        static LastKingSetup() { EditorApplication.update += PollRequest; }
        static void PollRequest()
        {
            if (working || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            working = true;
            try
            {
                string request = File.ReadAllText(RequestPath);
                SetupInternal(request.Trim().Equals("rebuild", StringComparison.OrdinalIgnoreCase));
                File.Delete(RequestPath);
                File.WriteAllText("QA/TheLastKing/setup-complete.txt", DateTime.UtcNow.ToString("O") + "\n" + ScenePath);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.WriteAllText("QA/TheLastKing/setup-error.txt", error.ToString());
                File.Move(RequestPath, RequestPath + ".failed-" + DateTime.UtcNow.Ticks);
            }
            finally { working = false; }
        }

        [MenuItem("The Last King/Open Table")]
        public static void Setup() { SetupInternal(false); }

        static void SaveUserScenes()
        {
            Directory.CreateDirectory(Root + "Scenes");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.isDirty) continue;
                string path = scene.path;
                if (string.IsNullOrEmpty(path)) path = AssetDatabase.GenerateUniqueAssetPath(Root + "Scenes/RecoveredUntitled.unity");
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new IOException("Could not preserve open scene: " + scene.name);
            }
        }

        static void SetupInternal(bool rebuild)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before opening the authoring scene.");
            SaveUserScenes();
            Directory.CreateDirectory("QA/TheLastKing");
            if (File.Exists(ScenePath) && !rebuild)
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            if (File.Exists(ScenePath))
                File.Copy(ScenePath, "QA/TheLastKing/LastKingTable-backup-" + DateTime.UtcNow.Ticks + ".unity");
            foreach (string folder in new[] { "Art", "Audio", "Prefabs", "Scenes" }) Directory.CreateDirectory(Root + folder);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            square = MakeShape("WhiteSquare", false); circle = MakeShape("SolidCircle", true);
            Sprite room = ImportSingle(Root + "Art/LastKingRoom.png");
            Sprite chair = ImportSingle(Root + "Art/OpponentChair.png");
            Sprite[] cards = ImportCards();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0,0,-10);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(13,11,9,255);
            var controllerObject = new GameObject("Match Controller");
            var game = controllerObject.AddComponent<LastKingGameController>();
            game.WorldCamera = camera; game.CardSprites = cards;
            game.AudioSource = controllerObject.AddComponent<AudioSource>(); game.AudioSource.playOnAwake = false;
            game.SelectSound = MakeTone("CardSelect", 370, .055f, .12f);
            game.RevealSound = MakeTone("CardReveal", 145, .22f, .20f);
            game.CoinSound = MakeTone("BloodCoin", 1080, .18f, .18f);
            game.EndSound = MakeTone("FinalVerdict", 58, .75f, .24f);
            Transform world = new GameObject("World").transform;
            SpriteRenderer background = SpriteObject("Room Background", world, room, Vector2.zero, Vector2.one, Color.white, -30);
            background.transform.localScale = new Vector3(16f / room.bounds.size.x, 9f / room.bounds.size.y, 1);
            BuildChair(world, game, chair);
            BuildScale(world, game, camera);
            BuildDeck(world);
            BuildCanvas(game, cards);
            ConfigurePlayer();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = controllerObject;
            Debug.Log("THE LAST KING: editable scene, Canvas, four prefabs and audio assets created at " + ScenePath);
        }

        static void BuildChair(Transform world, LastKingGameController game, Sprite sprite)
        {
            var root = new GameObject("Opponent Chair"); root.transform.SetParent(world); root.transform.position = new Vector3(0,.65f,0);
            var figure = SpriteObject("Opponent — free hands", root.transform, sprite, Vector2.zero, Vector2.one, Color.white, -10);
            float scale = 4.2f / sprite.bounds.size.y; figure.transform.localScale = Vector3.one * scale;
            // The generated source has free wrists; no restraints are added during card play.
            game.OpponentVisual = root.transform; game.OpponentTint = figure;
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "Prefabs/OpponentChair.prefab", InteractionMode.AutomatedAction);
        }

        static void BuildScale(Transform world, LastKingGameController game, Camera camera)
        {
            var scaleRoot = new GameObject("Scale Assembly"); scaleRoot.transform.SetParent(world);
            scaleRoot.transform.position = new Vector3(-4.4f,.15f,0);
            SpriteObject("Pedestal", scaleRoot.transform, square, new Vector2(0,-1.72f), new Vector2(1.25f,.19f), new Color32(73,57,34,255), 2);
            SpriteObject("Pedestal lip", scaleRoot.transform, square, new Vector2(0,-1.60f), new Vector2(.85f,.07f), Gold, 3);
            SpriteObject("Column", scaleRoot.transform, square, new Vector2(0,-.80f), new Vector2(.12f,1.6f), new Color32(162,126,64,255), 2);
            SpriteObject("Pivot ring", scaleRoot.transform, circle, Vector2.zero, new Vector2(.28f,.28f), Gold, 7);
            SpriteRenderer beamVisual = SpriteObject("Balance Beam", scaleRoot.transform, square, Vector2.zero, new Vector2(2.35f,.09f), Gold, 5);
            // Collider dimensions are expressed in world units, so physics objects remain unscaled.
            var beamObject = new GameObject("Beam Body"); beamObject.transform.SetParent(scaleRoot.transform, false);
            beamVisual.transform.SetParent(beamObject.transform, true);
            var beam = beamObject.AddComponent<Rigidbody2D>(); beam.mass = 2; beam.gravityScale = 1;
            beam.angularDamping = 2; beam.interpolation = RigidbodyInterpolation2D.Interpolate;
            var beamCollider = beamObject.AddComponent<BoxCollider2D>(); beamCollider.size = new Vector2(2.35f,.09f);
            beam.centerOfMass = new Vector2(0,-.12f);
            var pivot = beamObject.AddComponent<HingeJoint2D>(); pivot.autoConfigureConnectedAnchor = false;
            pivot.connectedAnchor = new Vector2(-4.4f,.15f); pivot.useLimits = true;
            pivot.limits = new JointAngleLimits2D { min = -14, max = 14 }; pivot.enableCollision = false;
            var pans = new Rigidbody2D[2]; var sensors = new Collider2D[2]; var deposits = new Transform[2];
            PhysicsMaterial2D material = MakeMaterial();
            for (int seat = 0; seat < 2; seat++)
            {
                float x = seat == 0 ? -1.05f : 1.05f;
                var panObject = new GameObject(seat == 0 ? "Player Pan" : "Opponent Pan");
                panObject.transform.SetParent(scaleRoot.transform, false); panObject.transform.localPosition = new Vector3(x,-.8f,0);
                Rigidbody2D body = panObject.AddComponent<Rigidbody2D>(); body.mass = .7f; body.gravityScale = 1;
                body.angularDamping = 2; body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var hinge = panObject.AddComponent<HingeJoint2D>(); hinge.connectedBody = beam; hinge.autoConfigureConnectedAnchor = false;
                hinge.anchor = new Vector2(0,.8f); hinge.connectedAnchor = new Vector2(x,0); hinge.enableCollision = false;
                BodyBox("Pan floor", panObject.transform, new Vector2(0,0), new Vector2(1.25f,.08f), Gold, material);
                BodyBox("Left rim", panObject.transform, new Vector2(-.61f,.13f), new Vector2(.06f,.30f), Gold, material);
                BodyBox("Right rim", panObject.transform, new Vector2(.61f,.13f), new Vector2(.06f,.30f), Gold, material);
                DrawStrut("Left chain", panObject.transform, new Vector2(-.58f,.14f), new Vector2(0,.8f), .025f, Muted, 4);
                DrawStrut("Right chain", panObject.transform, new Vector2(.58f,.14f), new Vector2(0,.8f), .025f, Muted, 4);
                var sensorObject = new GameObject("Deposit Sensor"); sensorObject.transform.SetParent(panObject.transform, false);
                sensorObject.transform.localPosition = new Vector3(0,.16f,0);
                var sensor = sensorObject.AddComponent<BoxCollider2D>(); sensor.isTrigger = true; sensor.size = new Vector2(1.15f,.4f);
                var deposit = new GameObject("Deposit Point"); deposit.transform.SetParent(panObject.transform, false); deposit.transform.localPosition = new Vector3(0,.45f,0);
                pans[seat] = body; sensors[seat] = sensor; deposits[seat] = deposit.transform;
            }
            var pickup = new GameObject("Coin Pickup Point"); pickup.transform.SetParent(world); pickup.transform.position = new Vector3(-2.1f,-1.7f,0);
            SpriteObject("Coin tray", world, square, new Vector2(-2.1f,-1.9f), new Vector2(.9f,.16f), new Color32(64,47,27,255), 3);
            var shelf = new GameObject("Pickup Shelf Collider"); shelf.transform.SetParent(world); shelf.transform.position = new Vector3(-2.1f,-1.94f,0);
            var shelfCollider = shelf.AddComponent<BoxCollider2D>(); shelfCollider.size = new Vector2(.9f,.08f); shelfCollider.sharedMaterial = material;
            var coinRoot = new GameObject("Deposited Coins"); coinRoot.transform.SetParent(world);
            var coin = new GameObject("Blood Coin");
            SpriteRenderer coinSprite = coin.AddComponent<SpriteRenderer>(); coinSprite.sprite = circle; coinSprite.color = Gold; coinSprite.sortingOrder = 12;
            coin.transform.localScale = Vector3.one * .22f;
            var coinBody = coin.AddComponent<Rigidbody2D>(); coinBody.mass = .09f; coinBody.linearDamping = .15f; coinBody.angularDamping = .3f;
            coinBody.interpolation = RigidbodyInterpolation2D.Interpolate; coinBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var coinCollider = coin.AddComponent<CircleCollider2D>(); coinCollider.radius = .5f; coinCollider.sharedMaterial = material;
            SpriteObject("Stamped center", coin.transform, circle, Vector2.zero, new Vector2(.56f,.56f), Blood, 13);
            GameObject coinPrefab = PrefabUtility.SaveAsPrefabAsset(coin, Root + "Prefabs/Coin.prefab"); UnityEngine.Object.DestroyImmediate(coin);
            LastKingPhysics physics = game.gameObject.AddComponent<LastKingPhysics>();
            physics.WorldCamera = camera; physics.CoinPrefab = coinPrefab; physics.PickupPoint = pickup.transform;
            physics.Beam = beam; physics.Pans = pans; physics.PanSensors = sensors; physics.DepositPoints = deposits; physics.CoinRoot = coinRoot.transform;
            physics.CoinMasses = new[] { .09f,.10f,.11f,.125f,.14f }; physics.PlacementTimeout = 15; physics.AutoPlaceDelay = .8f;
            physics.DragBounds = new Rect(-7.8f,-2.6f,7.1f,6.3f); physics.RecoveryY = -3;
            game.Physics = physics;
            PrefabUtility.SaveAsPrefabAssetAndConnect(scaleRoot, Root + "Prefabs/ScaleAssembly.prefab", InteractionMode.AutomatedAction);
        }

        static void BuildDeck(Transform world)
        {
            Transform deck = new GameObject("Table Deck").transform; deck.SetParent(world); deck.position = new Vector3(2.9f,-1.4f,0);
            for (int i = 0; i < 5; i++)
            {
                SpriteObject("Card edge " + i, deck, square, new Vector2(.025f*i,.025f*i), new Vector2(.64f,.88f), i == 4 ? new Color32(77,38,29,255) : Muted, i + 2);
            }
            SpriteObject("Deck seal", deck, circle, new Vector2(.1f,.1f), new Vector2(.27f,.27f), Gold, 8);
        }

        static void BuildCanvas(LastKingGameController game, Sprite[] cards)
        {
            var canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = .5f;
            Transform root = canvasObject.transform;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Image header = Box("Header", root, 800,38,1600,76,Panel);
            Label("Title", header.transform, "THE LAST KING", 800,32,580,42,31,Gold,TextAnchor.MiddleCenter);
            game.TurnText = Label("Turn", root, "LƯỢT 01", 45,22,300,32,20,Cream);
            game.InitiativeText = Label("Initiative", root, "QUYỀN CHỦ ĐỘNG",45,54,440,24,13,Muted);
            game.PauseButton = Button("Pause",root,"TẠM DỪNG",1470,38,190,40,19);
            Box("Header rule",root,800,77,1540,2,Gold);
            Image stats = Box("Public Ledger",root,1405,329,310,450,Panel);
            Label("Ledger heading",root,"SỔ CÁI CÔNG KHAI",1275,123,265,28,18,Gold);
            game.DeckText = Label("Deck Count",root,"BỘ BÀI  18",1275,167,265,32,20,Cream);
            game.PlayerHandText = Label("Player Public Hand",root,"BẠN\nVua 0 · Dân 0 · Nô lệ 0",1275,212,265,64,17,Cream);
            game.OpponentHandText = Label("Opponent Public Hand",root,"ĐỐI THỦ\nVua 0 · Dân 0 · Nô lệ 0",1275,285,265,64,17,Cream);
            Box("Ledger line",root,1405,364,260,1,new Color32(101,78,40,255));
            game.PlayerCoinText = Label("Player Blood Coins",root,"BẠN     0 / 5 XU MÁU",1275,386,265,31,19,Gold);
            game.OpponentCoinText = Label("Opponent Blood Coins",root,"ĐỐI THỦ   0 / 5 XU MÁU",1275,428,265,31,19,Gold);
            game.PlayerBpmText = Label("Player Heartbeat",root,"70 BPM",1275,480,125,26,16,Muted);
            game.OpponentBpmText = Label("Opponent Heartbeat",root,"70 BPM",1410,480,125,26,16,Muted);
            Label("Scale player label",root,"BẠN",255,560,145,28,16,Gold,TextAnchor.MiddleCenter);
            Label("Scale opponent label",root,"ĐỐI THỦ",465,560,145,28,16,Gold,TextAnchor.MiddleCenter);
            game.HistoryText = Label("Public History",root,"LỊCH SỬ\nChưa có lượt đã lật.",45,122,475,102,16,Muted);
            game.DepositHint = Label("Deposit Hint",root,"KÉO XU MÁU VÀO ĐĨA CÂN CỦA BẠN",301,590,510,72,20,Gold,TextAnchor.MiddleCenter);
            game.AutoPlaceButton = Button("Auto Place",root,"TỰ ĐỘNG ĐẶT XU",290,678,300,44,18);
            Box("Status band",root,800,649,640,62,Panel);
            game.StatusText = Label("Status",root,"Một ngai vàng. Hai người chơi.",800,649,620,51,19,Cream,TextAnchor.MiddleCenter);
            game.TimerText = Label("Timer",root,"25",1118,650,80,44,28,Gold,TextAnchor.MiddleCenter);
            Box("Timer Track",root,800,688,640,4,new Color32(67,55,31,255));
            game.TimerFill = Box("Timer Fill",root,800,688,640,4,Gold); game.TimerFill.type = Image.Type.Filled;
            game.TimerFill.fillMethod = Image.FillMethod.Horizontal; game.TimerFill.fillOrigin = 0;
            game.CardButtons = new Button[3]; game.CardImages = new Image[3]; game.CardLabels = new Text[3]; game.SelectionMarks = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                float x = 575 + 180 * i;
                var card = Box("Card " + (i+1),root,x,794,132,190,Color.white); card.sprite = cards[i]; card.preserveAspect = false;
                card.raycastTarget = true;
                Button button = card.gameObject.AddComponent<Button>(); button.targetGraphic = card;
                ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1,.94f,.77f);
                colors.pressedColor = new Color(.8f,.7f,.5f); colors.selectedColor = Color.white; colors.disabledColor = new Color(.35f,.32f,.28f); button.colors = colors;
                var label = Label("Card Name",card.transform,new[] { "VUA", "DÂN", "NÔ LỆ" }[i],66,172,120,25,17,new Color32(42,25,13,255),TextAnchor.MiddleCenter,true);
                var mark = Box("Selected Edge",card.transform,66,2,132,4,Gold,true);
                Label("Selected Caption",mark.transform,"ĐÃ CHỌN",66,-18,140,26,13,Gold,TextAnchor.MiddleCenter,true);
                mark.gameObject.SetActive(false);
                game.CardButtons[i]=button; game.CardImages[i]=card; game.CardLabels[i]=label; game.SelectionMarks[i]=mark.gameObject;
                if (i == 0) PrefabUtility.SaveAsPrefabAssetAndConnect(card.gameObject,Root+"Prefabs/CardButton.prefab",InteractionMode.AutomatedAction);
            }
            game.LockButton = Button("Lock Card",root,"KHÓA BÀI",1133,794,215,62,24);
            game.LockButtonText = game.LockButton.GetComponentInChildren<Text>();
            Label("Lock Hint",root,"Chọn một lá rồi khóa\nHai lá lật cùng lúc",1134,842,218,45,14,Muted,TextAnchor.MiddleCenter);
            Label("Rules Strip",root,"VUA > DÂN > NÔ LỆ > VUA   ·   5 XU MÁU: THẤT BẠI",1405,615,300,72,16,Muted,TextAnchor.MiddleCenter);
            BuildReveal(root,game);
            game.ScreenFlash = Box("Screen Flash",root,800,450,1600,900,new Color(1,0,0,0)); game.ScreenFlash.raycastTarget=false;
            game.StartPanel = Overlay(root,"Start Panel");
            Label("Welcome Title",game.StartPanel.transform,"THE LAST KING",800,220,950,70,52,Gold,TextAnchor.MiddleCenter,true);
            Label("Welcome Subtitle",game.StartPanel.transform,"MỘT NGƯỜI RỜI BÀN. MỘT NGƯỜI GIỮ NGÔI.",800,304,950,42,23,Cream,TextAnchor.MiddleCenter,true);
            Label("Welcome Rules",game.StartPanel.transform,"Chọn bài, khóa lựa chọn, chờ phán quyết.\nVua thắng Dân · Dân thắng Nô lệ · Nô lệ thắng Vua.\nNgười thua đặt Xu Máu lên cân. Xu thứ năm kết thúc trận.\nSổ cái bên phải cho biết những gì cả hai bên cùng biết.",800,426,920,170,23,Cream,TextAnchor.MiddleCenter,true);
            game.StartButton = Button("Start Match",game.StartPanel.transform,"BƯỚC VÀO BÀN",800,597,390,66,25,true);
            Label("Welcome Controls",game.StartPanel.transform,"CHUỘT: CHỌN BÀI / KÉO XU     ·     ESC: TẠM DỪNG",800,689,1000,36,17,Muted,TextAnchor.MiddleCenter,true);
            game.EndPanel = Overlay(root,"End Panel");
            game.EndTitle = Label("End Title",game.EndPanel.transform,"PHÁN QUYẾT",800,302,1000,80,50,Gold,TextAnchor.MiddleCenter,true);
            game.EndReasonText = Label("End Reason",game.EndPanel.transform,"",800,427,900,145,25,Cream,TextAnchor.MiddleCenter,true);
            game.RestartButton = Button("Restart Match",game.EndPanel.transform,"VÁN ĐẤU MỚI",800,605,390,66,25,true);
            game.PausePanel = Overlay(root,"Pause Panel");
            Label("Pause Title",game.PausePanel.transform,"TẠM DỪNG",800,340,700,72,48,Gold,TextAnchor.MiddleCenter,true);
            game.ResumeButton = Button("Resume Match",game.PausePanel.transform,"TIẾP TỤC",800,510,350,66,25,true);
            game.StartPanel.SetActive(true); game.EndPanel.SetActive(false); game.PausePanel.SetActive(false);
        }

        static void BuildReveal(Transform root, LastKingGameController game)
        {
            var panel = Box("Reveal Panel",root,800,410,510,345,new Color32(18,14,10,235)); game.RevealPanel=panel.gameObject;
            game.PlayerReveal=Box("Player Revealed Card",panel.transform,140,140,110,180,Color.white,true);
            game.OpponentReveal=Box("Opponent Revealed Card",panel.transform,370,140,110,180,Color.white,true);
            Label("Player Reveal Label",panel.transform,"BẠN",140,248,130,28,15,Gold,TextAnchor.MiddleCenter,true);
            Label("Opponent Reveal Label",panel.transform,"ĐỐI THỦ",370,248,130,28,15,Gold,TextAnchor.MiddleCenter,true);
            Label("Reveal Divider",panel.transform,"/",255,135,60,70,40,Muted,TextAnchor.MiddleCenter,true);
            game.RevealText=Label("Reveal Verdict",panel.transform,"PHÁN QUYẾT",255,305,470,55,21,Cream,TextAnchor.MiddleCenter,true);
            game.RevealPanel.SetActive(false);
        }

        static GameObject Overlay(Transform root,string name)
        {
            var image=Box(name,root,800,450,1600,900,new Color32(12,10,8,242)); image.raycastTarget=true; return image.gameObject;
        }

        static RectTransform RectObject(string name,Transform parent,float x,float y,float width,float height,bool local=false)
        {
            var go = new GameObject(name,typeof(RectTransform)); var rt=go.GetComponent<RectTransform>(); rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=new Vector2(0,1); rt.pivot=new Vector2(.5f,.5f);
            rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(width,height); return rt;
        }
        static Image Box(string name,Transform parent,float x,float y,float width,float height,Color color,bool local=false)
        {
            var rt=RectObject(name,parent,x,y,width,height,local); var image=rt.gameObject.AddComponent<Image>(); image.color=color;
            image.sprite=square; image.raycastTarget=false; return image;
        }
        static Text Label(string name,Transform parent,string content,float x,float y,float width,float height,int size,Color color,TextAnchor anchor=TextAnchor.UpperLeft,bool centered=false)
        {
            // Most labels use top-left coordinates; centered labels use their rectangle center.
            bool middle=anchor==TextAnchor.MiddleCenter;
            var rt=RectObject(name,parent,middle?x:x+width*.5f,middle?y:y+height*.5f,width,height);
            var text=rt.gameObject.AddComponent<Text>(); text.font=font; text.text=content; text.fontSize=size; text.color=color;
            text.alignment=anchor; text.raycastTarget=false; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Overflow;
            return text;
        }
        static Button Button(string name,Transform parent,string caption,float x,float y,float width,float height,int size,bool local=false)
        {
            Image image=Box(name,parent,x,y,width,height,Gold,local); image.raycastTarget=true;
            Button button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            ColorBlock colors=button.colors; colors.highlightedColor=new Color(1,.91f,.7f); colors.pressedColor=new Color(.73f,.60f,.33f);
            colors.disabledColor=new Color(.3f,.27f,.22f); button.colors=colors;
            Label("Caption",image.transform,caption,width*.5f,height*.5f,width-12,height-4,size,new Color32(35,25,14,255),TextAnchor.MiddleCenter,true);
            return button;
        }

        static SpriteRenderer SpriteObject(string name,Transform parent,Sprite sprite,Vector2 position,Vector2 size,Color color,int order)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position;
            go.transform.localScale=new Vector3(size.x,size.y,1); var renderer=go.AddComponent<SpriteRenderer>(); renderer.sprite=sprite;
            renderer.color=color; renderer.sortingOrder=order; return renderer;
        }
        static void BodyBox(string name,Transform parent,Vector2 position,Vector2 size,Color color,PhysicsMaterial2D material)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position;
            SpriteObject("Metal",go.transform,square,Vector2.zero,size,color,6);
            var collider=go.AddComponent<BoxCollider2D>(); collider.size=size; collider.sharedMaterial=material;
        }
        static void DrawStrut(string name,Transform parent,Vector2 from,Vector2 to,float width,Color color,int order)
        {
            Vector2 delta=to-from; var renderer=SpriteObject(name,parent,square,(from+to)*.5f,new Vector2(width,delta.magnitude),color,order);
            renderer.transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg-90);
        }
        static PhysicsMaterial2D MakeMaterial()
        {
            string path=Root+"Art/CoinMetal.physicsMaterial2D";
            var existing=AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path); if(existing) return existing;
            var material=new PhysicsMaterial2D("Coin metal") { friction=.6f,bounciness=.03f };
            AssetDatabase.CreateAsset(material,path); return material;
        }
        static Sprite MakeShape(string name,bool round)
        {
            string path=Root+"Art/"+name+".png";
            if(!File.Exists(path))
            {
                var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
                for(int y=0;y<32;y++) for(int x=0;x<32;x++) texture.SetPixel(x,y,!round||new Vector2(x-15.5f,y-15.5f).sqrMagnitude<=240 ? Color.white : Color.clear);
                texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            }
            Sprite sprite=ImportSingle(path,32); return sprite;
        }
        static Sprite ImportSingle(string path,float ppu=100)
        {
            if(!File.Exists(path)) throw new FileNotFoundException("Required art is missing",path);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single; importer.spritePixelsPerUnit=ppu; importer.filterMode=FilterMode.Point;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.mipmapEnabled=false; importer.alphaIsTransparency=true; importer.maxTextureSize=2048;
            var settings=new TextureImporterSettings(); importer.ReadTextureSettings(settings); settings.spriteMeshType=SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static Sprite[] ImportCards()
        {
            string path=Root+"Art/CardAtlas.png";
            ImportSingle(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.spriteImportMode=SpriteImportMode.Multiple;
            var factories=new SpriteDataProviderFactories(); factories.Init();
            var provider=factories.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var previous=provider.GetSpriteRects();
            var rects=new[] {
                new SpriteRect { name="King",rect=new Rect(34,122,470,778),alignment=SpriteAlignment.Center,pivot=new Vector2(.5f,.5f) },
                new SpriteRect { name="Citizen",rect=new Rect(532,122,474,778),alignment=SpriteAlignment.Center,pivot=new Vector2(.5f,.5f) },
                new SpriteRect { name="Slave",rect=new Rect(1032,122,472,778),alignment=SpriteAlignment.Center,pivot=new Vector2(.5f,.5f) }
            };
            foreach(var rect in rects) { var existing=previous.FirstOrDefault(r=>r.name==rect.name); rect.spriteID=existing!=null?existing.spriteID:GUID.Generate(); }
            provider.SetSpriteRects(rects);
            var names=provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names.SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport(); var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            return new[] { sprites.First(s=>s.name=="King"),sprites.First(s=>s.name=="Citizen"),sprites.First(s=>s.name=="Slave") };
        }
        static AudioClip MakeTone(string name,float frequency,float seconds,float volume)
        {
            string path=Root+"Audio/"+name+".wav";
            if(!File.Exists(path))
            {
                const int rate=22050; int samples=(int)(rate*seconds);
                using(var stream=File.Create(path)) using(var writer=new BinaryWriter(stream))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
                    for(int i=0;i<samples;i++) { double t=(double)i/rate,envelope=Math.Exp(-7*t/seconds)*Math.Min(1,t*120); double wave=Math.Sin(2*Math.PI*frequency*t)+.25*Math.Sin(2*Math.PI*frequency*2.71*t); writer.Write((short)(wave*volume*envelope*short.MaxValue)); }
                }
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport); return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        static void ConfigurePlayer()
        {
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;
            var scenes=EditorBuildSettings.scenes.ToList(); if(!scenes.Any(s=>s.path==ScenePath))scenes.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=scenes.ToArray();
        }
        [MenuItem("The Last King/Run Checks")]
        public static void RunChecks()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before running editor checks.");
            string rules=LastKingRuleChecks.RunAll();
            Debug.Log(rules);
            SaveUserScenes();
            if(SceneManager.GetActiveScene().path!=ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var game=UnityEngine.Object.FindAnyObjectByType<LastKingGameController>();
            if(!game) throw new InvalidOperationException("Serialized LastKingGameController is missing.");
            foreach(var field in typeof(LastKingGameController).GetFields(BindingFlags.Instance|BindingFlags.Public))
            {
                if(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    if(!(field.GetValue(game) as UnityEngine.Object)) throw new InvalidOperationException("Missing serialized reference: "+field.Name);
                }
                else if(field.FieldType.IsArray)
                {
                    var values=field.GetValue(game) as Array;
                    if(values==null||values.Length!=3) throw new InvalidOperationException("Expected three entries: "+field.Name);
                    foreach(var value in values) if(!(value as UnityEngine.Object)) throw new InvalidOperationException("Empty array reference: "+field.Name);
                }
            }
            LastKingPhysics physics=game.Physics;
            if(!physics.CoinPrefab||!physics.CoinPrefab.GetComponent<Rigidbody2D>()||!physics.CoinPrefab.GetComponent<CircleCollider2D>()) throw new InvalidOperationException("Coin prefab needs a rigidbody and circular collider.");
            if(!physics.Beam||!physics.Beam.GetComponent<HingeJoint2D>()) throw new InvalidOperationException("Scale beam hinge is missing.");
            if(physics.Pans==null||physics.Pans.Length!=2||physics.PanSensors==null||physics.PanSensors.Length!=2||physics.DepositPoints==null||physics.DepositPoints.Length!=2) throw new InvalidOperationException("Scale needs two serialized pan/sensor/deposit entries.");
            for(int i=0;i<2;i++)
            {
                if(!physics.Pans[i]||!physics.PanSensors[i]||!physics.DepositPoints[i]||!physics.PanSensors[i].isTrigger||!physics.Pans[i].GetComponent<HingeJoint2D>()) throw new InvalidOperationException("Invalid physical pan "+i);
                if(physics.Pans[i].transform.lossyScale!=Vector3.one) throw new InvalidOperationException("Physical pans must have unit scale.");
            }
            if(!UnityEngine.Object.FindAnyObjectByType<Canvas>()||!UnityEngine.Object.FindAnyObjectByType<EventSystem>()) throw new InvalidOperationException("Editable Canvas or EventSystem missing.");
            foreach(string name in new[]{"CardButton","Coin","ScaleAssembly","OpponentChair"}) if(!AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/"+name+".prefab")) throw new InvalidOperationException("Missing prefab: "+name);
            Directory.CreateDirectory("QA/TheLastKing");
            File.WriteAllText("QA/TheLastKing/editor-checks-passed.txt",rules+"\nPASS: serialized scene references, Canvas, scale rig and four prefabs.\n");
            Debug.Log("THE LAST KING: rule and scene structure checks passed.");
        }
        [MenuItem("The Last King/Build Windows")]
        public static void BuildWindows()
        {
            Setup();RunChecks();Directory.CreateDirectory("Build/TheLastKing");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},locationPathName="Build/TheLastKing/TheLastKing.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("The Last King build failed: "+report.summary.result);
            Directory.CreateDirectory("QA/TheLastKing");File.WriteAllText("QA/TheLastKing/build-passed.txt","Windows x64 build passed\n"+DateTime.UtcNow.ToString("O"));
        }
    }
}
