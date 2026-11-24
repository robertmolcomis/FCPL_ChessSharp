using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Chess.Unity
{
    /// <summary>
    /// Main menu: Play (→ Normal / Enhanced), Continue, Replay, Exit and a settings wheel.
    /// Drop it on an empty GameObject in the MainMenu scene and press Play — the UI is built at runtime, like the board.
    /// It only fills in <see cref="GameConfig"/> and loads the game scene; BoardController does the rest.
    /// Mouse, arrow keys + Enter, and Esc (back / close) all work.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Header("Scenes (both must be in the Build Profiles scene list)")]
        [SerializeField] string gameSceneName = "SampleScene";

        [Header("Look")]
        [SerializeField] string title = "Chess Sharp";
        [SerializeField] Color background = new Color(0.16f, 0.17f, 0.20f);
        [SerializeField] Color buttonColor = new Color(0.71f, 0.53f, 0.39f);
        [SerializeField] Color buttonHover = new Color(0.83f, 0.65f, 0.49f);
        [SerializeField] Color buttonPressed = new Color(0.55f, 0.40f, 0.29f);
        [SerializeField] Color textColor = Color.white;

        // Layout, in a 1920×1080 reference canvas (the canvas scales to the real screen).
        static readonly Vector2 MainButtonSize = new Vector2(440, 110);
        const float FirstButtonY = 80f, ButtonSpacing = 140f;

        Font _font;
        Sprite _roundSprite, _gearSprite;

        GameObject _mainPanel, _playPanel, _settingsPanel;
        CanvasGroup _content; // everything except the settings overlay
        Button _playButton, _continueButton, _replayButton, _normalButton, _settingsCloseButton;
        Text _message;
        float _messageUntil;

        const string VolumeKey = "Volume";

        // Before the first scene, so the saved volume also applies when the game scene is opened directly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySavedVolume() => AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);

        void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _roundSprite = MakeRoundedRect();
            _gearSprite = MakeGear();

            EnsureEventSystem();
            BuildUi();
            ShowMain();
        }

        void OnDestroy()
        {
            if (_roundSprite != null) { Destroy(_roundSprite.texture); Destroy(_roundSprite); }
            if (_gearSprite != null) { Destroy(_gearSprite.texture); Destroy(_gearSprite); }
        }

        void Update()
        {
            if (_message != null && _message.gameObject.activeSelf && Time.unscaledTime >= _messageUntil)
                _message.gameObject.SetActive(false);

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            if (_settingsPanel.activeSelf) CloseSettings();
            else if (_playPanel.activeSelf) ShowMain();
        }

        // ───────────────────────── Actions ─────────────────────────

        void StartNewGame(bool classic) => StartGame(StartMode.NewGame, classic);

        void StartGame(StartMode mode, bool classic = false)
        {
            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                Debug.LogError($"[Chess] Can't open scene \"{gameSceneName}\": add it to the Build Profiles scene list " +
                               "(File > Build Profiles > Scene List) or fix the name on the MainMenu component.");
                ShowMessage($"Scene \"{gameSceneName}\" isn't in the Build Profiles scene list");
                return;
            }
            GameConfig.Request(mode, classic);
            SceneManager.LoadScene(gameSceneName);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void ShowMain()
        {
            _mainPanel.SetActive(true);
            _playPanel.SetActive(false);
            RefreshSaveButtons();
            Select(_playButton);
        }

        void ShowPlay()
        {
            _mainPanel.SetActive(false);
            _playPanel.SetActive(true);
            Select(_normalButton);
        }

        void OpenSettings()
        {
            _settingsPanel.SetActive(true);
            _content.interactable = false; // keeps keyboard navigation inside the overlay
            Select(_settingsCloseButton);
        }

        void CloseSettings()
        {
            _settingsPanel.SetActive(false);
            PlayerPrefs.Save();
            _content.interactable = true;
            Select(_playPanel.activeSelf ? _normalButton : _playButton);
        }

        /// <summary>Continue needs the saved board; Replay needs the saved move list.</summary>
        void RefreshSaveButtons()
        {
            SetInteractable(_continueButton, SaveFiles.HasLastMatch);
            SetInteractable(_replayButton, SaveFiles.HasLastMoves);
        }

        void SetInteractable(Button button, bool value)
        {
            button.interactable = value;
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = value ? textColor : new Color(textColor.r, textColor.g, textColor.b, 0.4f);
        }

        void ShowMessage(string text, float seconds = 6f)
        {
            _message.text = text;
            _message.gameObject.SetActive(true);
            _messageUntil = Time.unscaledTime + seconds;
        }

        static void Select(Button button)
        {
            var es = EventSystem.current;
            if (es == null || button == null) return;
            es.firstSelectedGameObject = button.gameObject; // arrow keys pick this up again if a click on empty space cleared the selection
            es.SetSelectedGameObject(button.gameObject);
        }

        // ───────────────────────── UI construction ─────────────────────────

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var canvas = (RectTransform)canvasGo.transform;

            // Background
            var bg = NewRect("Background", canvas, typeof(Image));
            Stretch(bg);
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = background;
            bgImage.raycastTarget = false;

            // Everything the player interacts with lives in "Content" so settings can lock it in one go.
            var content = NewRect("Content", canvas, typeof(CanvasGroup));
            Stretch(content);
            _content = content.GetComponent<CanvasGroup>();

            var titleText = MakeText(content, "Title", title, 150, TextAnchor.MiddleCenter);
            titleText.fontStyle = FontStyle.Bold;
            Place(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1400, 220));

            // Main screen: Play / Continue / Replay
            _mainPanel = NewRect("Main", content).gameObject;
            Stretch((RectTransform)_mainPanel.transform);
            _playButton = MakeCenterButton(_mainPanel.transform, "Play", "Play", 0, ShowPlay);
            _continueButton = MakeCenterButton(_mainPanel.transform, "Continue", "Continue", 1, () =>
            {
                if (SaveFiles.HasLastMatch) StartGame(StartMode.Continue);
            });
            _replayButton = MakeCenterButton(_mainPanel.transform, "Replay", "Replay", 2, () =>
            {
                if (SaveFiles.HasLastMoves) StartGame(StartMode.Replay);
            });

            // Play screen: Back / Normal / Enhanced
            _playPanel = NewRect("Play", content).gameObject;
            Stretch((RectTransform)_playPanel.transform);
            MakeCenterButton(_playPanel.transform, "Back", "Back", 0, ShowMain);
            _normalButton = MakeCenterButton(_playPanel.transform, "Normal", "Normal", 1, () => StartNewGame(classic: true));
            MakeCenterButton(_playPanel.transform, "Enhanced", "Enhanced", 2, () => StartNewGame(classic: false));

            // Always visible: Exit (bottom-left), settings wheel (bottom-right)
            MakeButton(content, "Exit", "Exit", new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(240, 90), Quit, 36);
            var wheel = MakeButton(content, "Settings", null, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 40), new Vector2(110, 110), OpenSettings);
            var gear = NewRect("Gear", wheel.transform, typeof(Image));
            Stretch(gear, 20f);
            var gearImage = gear.GetComponent<Image>();
            gearImage.sprite = _gearSprite;
            gearImage.preserveAspect = true;
            gearImage.color = textColor;
            gearImage.raycastTarget = false;

            // Short messages (e.g. a missing scene), bottom centre
            _message = MakeText(content, "Message", "", 30, TextAnchor.MiddleCenter);
            _message.color = new Color(1f, 0.78f, 0.35f);
            Place(_message.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1100, 60));
            _message.gameObject.SetActive(false);

            BuildSettingsOverlay(canvas);
        }

        /// <summary>Settings: master volume (saved in PlayerPrefs).</summary>
        void BuildSettingsOverlay(RectTransform canvas)
        {
            var overlay = NewRect("Settings Overlay", canvas, typeof(Image));
            Stretch(overlay);
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f); // also swallows clicks on the menu behind it
            _settingsPanel = overlay.gameObject;

            var box = NewRect("Box", overlay, typeof(Image));
            Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 480));
            var boxImage = box.GetComponent<Image>();
            boxImage.sprite = _roundSprite;
            boxImage.type = Image.Type.Sliced;
            boxImage.color = new Color(0.22f, 0.23f, 0.27f);

            var heading = MakeText(box, "Heading", "Settings", 64, TextAnchor.MiddleCenter);
            heading.fontStyle = FontStyle.Bold;
            Place(heading.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(700, 100));

            var volumeLabel = MakeText(box, "Volume Label", "", 36, TextAnchor.MiddleCenter);
            Place(volumeLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(700, 60));
            var volume = MakeSlider(box, new Vector2(0, -20), new Vector2(600, 50));
            volume.onValueChanged.AddListener(v =>
            {
                AudioListener.volume = v;
                PlayerPrefs.SetFloat(VolumeKey, v);
                volumeLabel.text = $"Volume  {Mathf.RoundToInt(v * 100)}%";
            });
            volume.value = AudioListener.volume;
            volumeLabel.text = $"Volume  {Mathf.RoundToInt(volume.value * 100)}%";

            _settingsCloseButton = MakeButton(box, "Close", "Close", new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                                              new Vector2(0, 40), new Vector2(260, 90), CloseSettings, 36);
            _settingsPanel.SetActive(false);
        }

        /// <summary>A button in the centre column; row 0 is the top one.</summary>
        Button MakeCenterButton(Transform parent, string name, string label, int row, Action onClick) =>
            MakeButton(parent, name, label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                       new Vector2(0, FirstButtonY - row * ButtonSpacing), MainButtonSize, onClick, 42);

        Button MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
                          Action onClick, int fontSize = 42)
        {
            var rt = NewRect(name, parent, typeof(Image), typeof(Button));
            Place(rt, anchor, pivot, position, size);

            var image = rt.GetComponent<Image>();
            image.sprite = _roundSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white; // the Button's colour block tints it

            var button = rt.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = Tint(button.colors);
            button.onClick.AddListener(() => onClick());

            if (label != null)
            {
                var text = MakeText(rt, "Label", label, fontSize, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
            }
            return button;
        }

        ColorBlock Tint(ColorBlock colors)
        {
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonHover;
            colors.selectedColor = buttonHover;
            colors.pressedColor = buttonPressed;
            colors.disabledColor = new Color(0.32f, 0.33f, 0.37f, 0.8f);
            colors.fadeDuration = 0.08f;
            return colors;
        }

        /// <summary>0–1 horizontal slider: rounded track, fill in the button colour, round handle. Left/Right arrows step 10%.</summary>
        Slider MakeSlider(Transform parent, Vector2 position, Vector2 size)
        {
            const float handleWidth = 40f;
            var rt = NewRect("Volume", parent, typeof(Slider));
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

            var track = NewRect("Track", rt, typeof(Image));
            track.anchorMin = new Vector2(0, 0.3f);
            track.anchorMax = new Vector2(1, 0.7f);
            track.offsetMin = track.offsetMax = Vector2.zero;
            var trackImage = track.GetComponent<Image>();
            trackImage.sprite = _roundSprite;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.12f, 0.13f, 0.15f);

            // Fill and handle areas are inset by half a handle so the handle centre lines up with the fill's end.
            var fillArea = NewRect("Fill Area", rt);
            fillArea.anchorMin = new Vector2(0, 0.3f);
            fillArea.anchorMax = new Vector2(1, 0.7f);
            fillArea.offsetMin = new Vector2(handleWidth / 2, 0);
            fillArea.offsetMax = new Vector2(-handleWidth / 2, 0);
            var fill = NewRect("Fill", fillArea, typeof(Image));
            fill.sizeDelta = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.sprite = _roundSprite;
            fillImage.type = Image.Type.Sliced;
            fillImage.color = buttonColor;
            fillImage.raycastTarget = false;

            var handleArea = NewRect("Handle Area", rt);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(handleWidth / 2, 0);
            handleArea.offsetMax = new Vector2(-handleWidth / 2, 0);
            var handle = NewRect("Handle", handleArea, typeof(Image));
            handle.sizeDelta = new Vector2(handleWidth, 0);
            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = _roundSprite;
            handleImage.type = Image.Type.Sliced;
            handleImage.color = Color.white; // the Slider's colour block tints it

            var slider = rt.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.colors = Tint(slider.colors);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        Text MakeText(Transform parent, string name, string value, int size, TextAnchor alignment)
        {
            var rt = NewRect(name, parent, typeof(Text));
            var text = rt.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = textColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform NewRect(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            foreach (var c in components) go.AddComponent(c);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        // ───────────────────────── Generated sprites (no art needed) ─────────────────────────

        /// <summary>White rounded rectangle with a 9-slice border, so one sprite fits every button size.</summary>
        static Sprite MakeRoundedRect()
        {
            const int size = 64, radius = 20;
            var tex = NewTexture(size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, radius, size - radius);
                    float cy = Mathf.Clamp(fy, radius, size - radius);
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>White gear (8 teeth, round hole), 3×3 supersampled so the edges are smooth.</summary>
        static Sprite MakeGear()
        {
            const int size = 128, teeth = 8, samples = 3;
            const float outer = 0.46f, root = 0.35f, hole = 0.15f; // radii as a fraction of the image width
            var tex = NewTexture(size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples) / size - 0.5f;
                            float v = (y + (sy + 0.5f) / samples) / size - 0.5f;
                            float d = Mathf.Sqrt(u * u + v * v);
                            float tooth = Mathf.Clamp01((Mathf.Cos(Mathf.Atan2(v, u) * teeth) + 0.25f) / 0.5f); // 0 = gap, 1 = tooth top
                            if (d >= hole && d <= Mathf.Lerp(root, outer, tooth)) covered++;
                        }
                    px[y * size + x] = new Color(1, 1, 1, covered / (float)(samples * samples));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D NewTexture(int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
    }
}
