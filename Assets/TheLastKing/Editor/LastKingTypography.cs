using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheLastKing.Editor
{
    /// <summary>Edits saved UI objects in place; never rebuilds the authored scene.</summary>
    [InitializeOnLoad]
    public static class LastKingTypography
    {
        public const string FontPath = "Assets/TheLastKing/Fonts/Roboto-Bold.ttf";
        const string Request = "QA/TheLastKing/typography-request.txt";
        static readonly Color Ink = new Color32(29,23,15,255);
        static readonly Color Gold = new Color32(245,215,145,255);
        static readonly Color Cream = new Color32(248,239,220,255);
        static bool busy;

        static LastKingTypography() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            busy = true;
            try
            {
                bool build = File.ReadAllText(Request).Trim().Equals("build", StringComparison.OrdinalIgnoreCase);
                Apply();
                if (build) LastKingSetup.BuildWindows();
                File.Delete(Request);
            }
            catch (Exception ex)
            {
                File.WriteAllText("QA/TheLastKing/typography-error.txt", ex.ToString());
                File.Move(Request, Request + ".failed-" + DateTime.UtcNow.Ticks);
                Debug.LogException(ex);
            }
            finally { busy = false; }
        }

        [MenuItem(LastKingSetup.GameTitle + "/Improve UI Readability")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before editing typography.");
            Directory.CreateDirectory("QA/TheLastKing");
            Scene scene = SceneManager.GetSceneByPath(LastKingSetup.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(LastKingSetup.ScenePath, OpenSceneMode.Additive);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not preserve the scene before typography changes.");
            File.Copy(LastKingSetup.ScenePath, "QA/TheLastKing/LastKingTable-before-font-" + DateTime.UtcNow.Ticks + ".unity");

            string prefabPath = "Assets/TheLastKing/Prefabs/CardButton.prefab";
            GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ApplyLabels(prefab.GetComponentsInChildren<Text>(true));
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            int count = ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            string result = "Updated " + count + " serialized UI labels to Roboto Bold; Vietnamese glyphs verified.\n" + LastKingSetup.ScenePath;
            File.WriteAllText("QA/TheLastKing/typography-complete.txt", result);
            Debug.Log("THE LAST KING TYPOGRAPHY: " + result);
            // Authoring previews only. This does not enter Play mode or run a match.
            try { CapturePreviews(scene); }
            catch (Exception ex) { File.WriteAllText("QA/TheLastKing/typography-preview-error.txt", ex.Message); }
        }

        public static int ApplyToScene(Scene scene)
        {
            Text[] labels = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Text>(true)).ToArray();
            ApplyLabels(labels);
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LastKingGameController>(true)).First();
            Rect(game.HistoryText.rectTransform,282.5f,192,475,140);
            Rect(game.DeckText.rectTransform,1407.5f,196,265,58);
            Rect(game.PlayerHandText.rectTransform,1407.5f,270,265,64);
            Rect(game.OpponentHandText.rectTransform,1407.5f,342,265,64);
            Rect(game.PlayerCoinText.rectTransform,1407.5f,422,265,32);
            Rect(game.OpponentCoinText.rectTransform,1407.5f,464,265,32);
            Rect(game.PlayerBpmText.rectTransform,1337.5f,519,125,30);
            Rect(game.OpponentBpmText.rectTransform,1472.5f,519,125,30);
            Rect(game.StatusText.rectTransform,800,640,600,80);
            Rect(game.TimerText.rectTransform,1177,645,126,44);
            Rect(game.DepositHint.rectTransform,260,604,420,86);
            Rect(game.RevealPanel.transform as RectTransform,800,405,510,390);
            Rect(game.RevealText.rectTransform,255,332,470,102);
            game.PlayerReveal.preserveAspect = game.OpponentReveal.preserveAspect = true;
            SetNamedRect(scene,"Ledger line",1405,386,260,1);
            SetNamedRect(scene,"Status band",800,640,640,90);
            SetNamedRect(scene,"Rules Strip",1405,615,300,96);
            foreach (Text text in labels)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(text))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(text.rectTransform);
                }
                EditorUtility.SetDirty(text);
            }
            return labels.Length;
        }

        static void ApplyLabels(Text[] labels)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) throw new InvalidOperationException("Missing readable font asset: " + FontPath);
            const string glyphs = "ĂăĐđĨĩŨũƠơƯưẠẢẤẦẨẪẬẮẰẲẴẶẸẺẼẾỀỂỄỆỈỊỌỎỐỒỔỖỘỚỜỞỠỢỤỦỨỪỬỮỰỲỴỶỸạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ";
            font.RequestCharactersInTexture(glyphs,24,FontStyle.Normal);
            foreach(char ch in glyphs) if (!font.HasCharacter(ch)) throw new InvalidOperationException("Font lacks Vietnamese glyph: " + ch);
            foreach (Text text in labels)
            {
                bool dark = text.name == "Card Name" || text.name == "Caption";
                bool accent = text.color.r > text.color.b * 1.35f;
                text.font = font;
                text.fontStyle = FontStyle.Normal; // This file already contains the bold face.
                text.resizeTextForBestFit = false;
                text.supportRichText = true;
                text.lineSpacing = 1.12f;
                text.fontSize = Mathf.Max(19,text.fontSize);
                text.color = dark ? Ink : accent ? Gold : Cream;
                switch(text.name)
                {
                    case "Title": text.fontSize=34; break;
                    case "Initiative": text.fontSize=18; break;
                    case "Public History": text.fontSize=19; break;
                    case "Player Public Hand": case "Opponent Public Hand": text.fontSize=20; break;
                    case "Player Heartbeat": case "Opponent Heartbeat": text.fontSize=18; text.lineSpacing=1; break;
                    case "Player Blood Coins": case "Opponent Blood Coins": text.fontSize=21; break;
                    case "Card Name": text.fontSize=20; AddCardBacking(text); break;
                    case "Selected Caption": text.fontSize=18; break;
                    case "Status": text.fontSize=22; break;
                    case "Timer": text.fontSize=24; break;
                    case "Deposit Hint": text.fontSize=20; break;
                    case "Lock Hint": text.fontSize=18; break;
                    case "Welcome Rules": text.fontSize=26; text.lineSpacing=1.2f; break;
                    case "Welcome Controls": text.fontSize=20; break;
                    case "Reveal Verdict": text.fontSize=22; break;
                }
                if (text.name=="Caption" && text.transform.parent.name=="Lock Card") text.fontSize=23;
                if (!dark)
                {
                    var shadow=text.GetComponent<Shadow>(); if(!shadow) shadow=text.gameObject.AddComponent<Shadow>();
                    shadow.effectColor=new Color(0,0,0,.75f); shadow.effectDistance=new Vector2(1,-1); shadow.useGraphicAlpha=true;
                }
                EditorUtility.SetDirty(text);
            }
        }

        static void AddCardBacking(Text text)
        {
            Transform parent=text.transform.parent;
            Transform existing=parent.Find("Readable Card Label");
            Image image;
            if(existing) image=existing.GetComponent<Image>();
            else
            {
                var go=new GameObject("Readable Card Label",typeof(RectTransform),typeof(Image));
                go.transform.SetParent(parent,false); image=go.GetComponent<Image>();
            }
            image.color=new Color32(245,222,171,246); image.raycastTarget=false;
            Rect(image.rectTransform,66,172,128,29);
            int index=text.transform.GetSiblingIndex();
            if(image.transform.GetSiblingIndex()<index) index--;
            image.transform.SetSiblingIndex(index);
        }

        static void Rect(RectTransform rt,float x,float y,float width,float height)
        {
            rt.anchorMin=rt.anchorMax=new Vector2(0,1); rt.pivot=new Vector2(.5f,.5f);
            rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(width,height);
        }
        static void SetNamedRect(Scene scene,string name,float x,float y,float width,float height)
        {
            var rt=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<RectTransform>(true)).FirstOrDefault(r=>r.name==name);
            if(rt) Rect(rt,x,y,width,height);
        }

        static void CapturePreviews(Scene scene)
        {
            var game=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LastKingGameController>(true)).First();
            var canvas=game.StartPanel.GetComponentInParent<Canvas>();
            Camera camera=game.WorldCamera;
            RenderTexture previousTarget=camera.targetTexture, previousActive=RenderTexture.active;
            RenderMode oldMode=canvas.renderMode; Camera oldCamera=canvas.worldCamera; float oldPlane=canvas.planeDistance;
            int oldSorting=canvas.sortingOrder;
            var panels=new[]{game.StartPanel,game.RevealPanel,game.EndPanel,game.PausePanel};
            bool[] active=panels.Select(p=>p.activeSelf).ToArray();
            var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt; canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
                canvas.sortingOrder=100;
                for(int shot=0;shot<2;shot++)
                {
                    foreach(var panel in panels) panel.SetActive(false);
                    game.StartPanel.SetActive(shot==0);
                    Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=rt;
                    pixels.ReadPixels(new UnityEngine.Rect(0,0,1600,900),0,0); pixels.Apply();
                    File.WriteAllBytes("QA/TheLastKing/typography-"+(shot==0?"welcome":"table")+".png",pixels.EncodeToPNG());
                }
            }
            finally
            {
                for(int i=0;i<panels.Length;i++) panels[i].SetActive(active[i]);
                camera.targetTexture=previousTarget; canvas.renderMode=oldMode; canvas.worldCamera=oldCamera; canvas.planeDistance=oldPlane; canvas.sortingOrder=oldSorting;
                RenderTexture.active=previousActive; UnityEngine.Object.DestroyImmediate(pixels); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                Canvas.ForceUpdateCanvases(); EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
