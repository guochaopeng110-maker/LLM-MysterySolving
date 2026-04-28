﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LLMMysterSolving
{
    public class ChatBot : MonoBehaviour
    {
        private const string PrefBaseUrl = "deepseek_base_url";
        private const string PrefApiKey = "deepseek_api_key";
        private const string PrefModel = "deepseek_model_name";
        private const string RuntimeConfigFileName = "game_config.json";

        public Transform chatContainer;
        public Color playerColor = new Color32(75, 70, 80, 255);
        public Color aiColor = new Color32(70, 80, 80, 255);
        public Color fontColor = Color.white;
        public Font font;
        public int fontSize = 16;
        public int bubbleWidth = 600;
        [Range(2, 10)] public int inputBubbleLineCount = 4;
        public float textPadding = 10f;
        public float bubbleSpacing = 10f;
        public Sprite sprite;
        public Button stopButton;

        [Header("Evidence & Stress (Optional)")]
        public Button evidenceButton;
        public string evidencePrompt = "【证物出示】你现在必须正面回应该证物，不能回避，先交代关键矛盾点再继续辩解。";
        public Slider stressSlider;
        public Text stressText;
        public int evidenceBreakDeltaThreshold = 8;

        [Header("Visual Polish (Optional)")]
        public bool autoBuildDemoLayout = true;
        public Sprite portraitFrameSprite;
        public bool enablePortraitVideo = true;
        public string portraitVideoRelativePath = "portraits/suspect.mp4";

        [Header("Dialogue Panel Layout")]
        [Range(0.2f, 0.8f)] public float dialogueLeftRatio = 0.3f;
        [Range(0f, 0.6f)] public float dialogueBottomRatio = 0.18f;
        [Range(0.4f, 1f)] public float dialogueTopRatio = 0.85f;
        public float dialoguePadding = 10f;

        [Header("DeepSeek")]
        public string deepSeekBaseUrl = "https://api.deepseek.com/chat/completions";
        public string deepSeekApiKey = "";
        public string deepSeekModel = "deepseek-chat";

        [Header("Settings Panel (Optional)")]
        public GameObject settingsPanel;
        public InputField baseUrlInputField;
        public InputField apiKeyInputField;
        public InputField modelNameInputField;
        public Button openSettingsButton;
        public Button closeSettingsButton;
        public Button saveSettingsButton;

        [Header("Script Editor Panel (Optional)")]
        public GameObject scriptEditorPanel;
        public InputField npcNameInputField;
        public InputField npcPersonalityInputField;
        public InputField npcSecretInputField;
        public InputField npcBackgroundInputField;
        public InputField scenarioBackgroundInputField;
        public Button openScriptEditorButton;
        public Button closeScriptEditorButton;
        public Button saveScriptEditorButton;

        [TextArea(3, 10)]
        public string systemPrompt = "你是唐代背景审讯中的嫌疑人。请严格返回 JSON 对象，字段必须包含：stress_delta, current_stress, emotion_state, inner_thought, dialogue。";

        private readonly DeepSeekClient deepSeekClient = new DeepSeekClient();
        private readonly List<DeepSeekMessage> conversationHistory = new List<DeepSeekMessage>();
        private const string DefaultPortraitAssetPath = "Assets/Scenes/MysterySolving/img/portriat.png";

        private InputBubble inputBubble;
        private readonly List<Bubble> chatBubbles = new List<Bubble>();
        private bool blockInput = true;
        private BubbleUI playerUI;
        private BubbleUI aiUI;
        private bool warmUpDone;
        private int lastBubbleOutsideFOV = -1;
        private GameConfigRoot runtimeConfig;
        private string runtimeConfigPath;
        private bool evidenceModePending;
        private int currentStressValue;
        private RectTransform dialogueViewportRect;
        private RectTransform dialogueContentRect;
        private ScrollRect dialogueScrollRect;
        private float dialogueBottomInset = 84f;
        private Image portraitFallbackImage;
        private RawImage portraitVideoImage;
        private VideoPlayer portraitVideoPlayer;
        private RenderTexture portraitVideoTexture;

        void Start()
        {
            EnsureEventSystemInputModule();

            // 强制设置为 30/70 比例，确保即使 Inspector 中是旧值也能正确应用
            dialogueLeftRatio = 0.3f;

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            playerUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = fontColor,
                bubbleColor = playerColor,
                bottomPosition = 0,
                leftPosition = 0,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            aiUI = playerUI;
            aiUI.bubbleColor = aiColor;
            aiUI.leftPosition = 1;

            inputBubble = new InputBubble(chatContainer, playerUI, "InputBubble", "Loading...", inputBubbleLineCount);
            inputBubble.AddSubmitListener(OnInputFieldSubmit);
            inputBubble.AddValueChangedListener(OnValueChanged);
            inputBubble.setInteractable(false);

            if (stopButton != null)
            {
                stopButton.gameObject.SetActive(true);
            }

            EnsureVisualDemoScaffold();
            EnsureStopButtonAndLayout();
            BindEvidenceButton();
            LoadOrCreateRuntimeConfig();
            LoadApiSettingsFromPrefs();
            BindSettingsPanel();
            BindScriptEditorPanel();
            InitializeConversation();
            UpdateStressUI(currentStressValue);
            WarmUpCallback();
        }

        static void EnsureEventSystemInputModule()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
                Debug.LogWarning("[EventSystem] Missing EventSystem was recreated with StandaloneInputModule.");
            }

            BaseInputModule inputModule = eventSystem.GetComponent<BaseInputModule>();
            if (inputModule == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
                Debug.LogWarning("[EventSystem] Missing input module restored with StandaloneInputModule.");
            }
        }

        void BindEvidenceButton()
        {
            if (evidenceButton == null)
            {
                return;
            }

            evidenceButton.onClick.RemoveListener(OnEvidenceButtonClicked);
            evidenceButton.onClick.AddListener(OnEvidenceButtonClicked);
        }

        void EnsureVisualDemoScaffold()
        {
            if (!autoBuildDemoLayout || chatContainer == null)
            {
                return;
            }

            RectTransform canvasRect = chatContainer.root as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            // 清理可能存在的孤立 StressPanel
            Transform strayStress = canvasRect.Find("StressPanel");
            if (strayStress != null && strayStress.parent == canvasRect)
            {
                DestroyImmediate(strayStress.gameObject);
            }

            RectTransform backdropRect = EnsurePanel(canvasRect, "TangBackdrop", new Vector2(0f, 0f), new Vector2(1f, 1f), Color.white, 0);
            Image backdropImage = backdropRect.GetComponent<Image>();
            if (backdropImage != null && backdropImage.sprite == null)
            {
                backdropImage.color = new Color32(32, 30, 27, 255);
            }
            float left = Mathf.Clamp(dialogueLeftRatio, 0.2f, 0.8f);
            float bottom = Mathf.Clamp(dialogueBottomRatio, 0f, 0.6f);
            float top = Mathf.Clamp(dialogueTopRatio, bottom + 0.05f, 1f);

            // 左右主面板使用半透明遮罩，保留文字可读性同时透出 TangBackdrop 背景图
            Color32 themeColor = new Color32(52, 46, 38, 118);
            RectTransform portraitLeftRect = EnsurePanel(canvasRect, "PortraitLeft", new Vector2(0f, 0f), new Vector2(left, 1f), themeColor, 1);
            RectTransform portraitRightRect = EnsurePanel(canvasRect, "PortraitRight", new Vector2(left, 0f), new Vector2(1f, 1f), themeColor, 2);

            // 修正 StressPanel：无背景色，左右对齐 DialoguePanel (使用 dialoguePadding)
            RectTransform stressPanelRect = EnsurePanel(portraitRightRect, "StressPanel", new Vector2(0f, 0.86f), new Vector2(1f, 1f), new Color32(0, 0, 0, 0), 0);
            stressPanelRect.offsetMin = new Vector2(dialoguePadding, 0f);
            stressPanelRect.offsetMax = new Vector2(-dialoguePadding, 0f);

            EnsurePortraitPlaceholder(portraitLeftRect);

            RectTransform chatRect = chatContainer as RectTransform;
            if (chatRect != null)
            {
                // 将 DialoguePanel 设为 PortraitRight 的子物体
                chatRect.SetParent(portraitRightRect, false);
                // 占满 PortraitRight 剩下的高度 (从底部 0 到 StressPanel 底部的 0.86)
                chatRect.anchorMin = new Vector2(0f, 0f);
                chatRect.anchorMax = new Vector2(1f, 0.86f);
                chatRect.offsetMin = new Vector2(dialoguePadding, dialoguePadding);
                chatRect.offsetMax = new Vector2(-dialoguePadding, -dialoguePadding);
                EnsureDialogueScrollArea(chatRect);
            }

            if (stressSlider == null)
            {
                stressSlider = EnsureStressSlider(stressPanelRect);
            }

            if (stressText == null)
            {
                stressText = EnsureStressText(stressPanelRect);
            }

            if (evidenceButton == null)
            {
                evidenceButton = EnsureEvidenceButton(stressPanelRect);
            }

            EnsureSettingsUi(canvasRect, portraitLeftRect);
            EnsureScriptEditorUi(canvasRect, portraitLeftRect);
        }

        void EnsureStopButtonAndLayout()
        {
            if (chatContainer == null || inputBubble == null)
            {
                return;
            }

            RectTransform chatRect = chatContainer as RectTransform;
            if (chatRect == null)
            {
                return;
            }

            if (stopButton == null)
            {
                stopButton = CreateOrUpdateButton(
                    chatRect,
                    "StopButton",
                    "停止",
                    Vector2.zero,
                    Vector2.zero,
                    new Color32(74, 74, 74, 230));
            }

            RectTransform inputRect = inputBubble.GetRectTransform();
            if (inputRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            const float side = 10f;
            const float gap = 8f;
            const float stopWidth = 88f;
            float rowHeight = Mathf.Max(48f, inputRect.sizeDelta.y);

            // 修改输入框布局：横向拉伸锚点，右侧预留按钮空间
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0f, 0f);
            inputRect.offsetMin = new Vector2(side, side);
            inputRect.offsetMax = new Vector2(-(side + stopWidth + gap), side + rowHeight);

            RectTransform stopRect = stopButton.GetComponent<RectTransform>();
            if (stopRect != null)
            {
                stopRect.SetParent(chatContainer, false);
                stopRect.anchorMin = new Vector2(1f, 0f);
                stopRect.anchorMax = new Vector2(1f, 0f);
                stopRect.pivot = new Vector2(1f, 0f);
                stopRect.sizeDelta = new Vector2(stopWidth, rowHeight);
                stopRect.anchoredPosition = new Vector2(-side, side);
            }

            SetDialogueViewportBottomInset(rowHeight + side * 2f + gap);

            stopButton.onClick.RemoveListener(CancelRequests);
            stopButton.onClick.AddListener(CancelRequests);
            stopButton.gameObject.SetActive(true);
        }

        static RectTransform EnsurePanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color, int siblingIndex)
        {
            Transform existing = parent.Find(name);
            GameObject panelObject;
            if (existing != null)
            {
                panelObject = existing.gameObject;
            }
            else
            {
                panelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panelObject.transform.SetParent(parent, false);
            }

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            Image image = panelObject.GetComponent<Image>();
            if (image == null)
            {
                image = panelObject.AddComponent<Image>();
            }

            image.color = color;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            return rect;
        }

        void EnsurePortraitPlaceholder(RectTransform portraitParent)
        {
            if (portraitParent == null)
            {
                return;
            }

            GameObject frameObject = CreateOrUpdatePanel(portraitParent, "PortraitFrame", new Vector2(0.08f, 0f), new Vector2(0.92f, 0.86f), new Color32(78, 66, 51, 200));
            RectTransform frameRect = frameObject.GetComponent<RectTransform>();
            TryAutoLoadPortraitSpriteInEditor();

            RemoveChildByName(frameRect, "PortraitTitle");
            RemoveChildByName(frameRect, "PortraitHint");

            Transform imageTransform = frameRect.Find("PortraitImage");
            GameObject imageObject;
            if (imageTransform != null)
            {
                imageObject = imageTransform.gameObject;
            }
            else
            {
                imageObject = new GameObject("PortraitImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                imageObject.transform.SetParent(frameRect, false);
            }

            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.04f, 0.03f);
            imageRect.anchorMax = new Vector2(0.96f, 0.97f);
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            imageRect.pivot = new Vector2(0.5f, 0.5f);

            Image portraitImage = imageObject.GetComponent<Image>();
            portraitImage.sprite = portraitFrameSprite;
            portraitImage.type = Image.Type.Sliced;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;
            portraitImage.color = portraitFrameSprite != null ? Color.white : new Color32(28, 25, 22, 255);
            //portraitFallbackImage = portraitImage;

            SetupPortraitVideo(frameRect, imageRect);
        }

        void SetupPortraitVideo(RectTransform frameRect, RectTransform imageRect)
        {
            if (frameRect == null || imageRect == null)
            {
                return;
            }

            string videoPath = ResolvePortraitVideoPath();
            bool canPlayVideo = enablePortraitVideo && !string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath);
            if (!canPlayVideo)
            {
                if (portraitVideoPlayer != null && portraitVideoPlayer.isPlaying)
                {
                    portraitVideoPlayer.Stop();
                }
                ShowPortraitFallback();
                return;
            }

            Transform videoTransform = frameRect.Find("PortraitVideo");
            GameObject videoObject;
            if (videoTransform != null)
            {
                videoObject = videoTransform.gameObject;
            }
            else
            {
                videoObject = new GameObject("PortraitVideo", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                videoObject.transform.SetParent(frameRect, false);
            }

            RectTransform videoRect = videoObject.GetComponent<RectTransform>();
            videoRect.anchorMin = imageRect.anchorMin;
            videoRect.anchorMax = imageRect.anchorMax;
            videoRect.offsetMin = imageRect.offsetMin;
            videoRect.offsetMax = imageRect.offsetMax;
            videoRect.pivot = imageRect.pivot;

            RawImage videoImage = videoObject.GetComponent<RawImage>();
            videoImage.raycastTarget = false;
            // Keep black until first frame is prepared to avoid flashing fallback portrait.
            videoImage.color = Color.black;
            portraitVideoImage = videoImage;

            if (portraitVideoImage != null)
            {
                portraitVideoImage.gameObject.SetActive(true);
            }

            if (portraitFallbackImage != null)
            {
                portraitFallbackImage.gameObject.SetActive(false);
            }

            VideoPlayer player = frameRect.GetComponent<VideoPlayer>();
            if (player == null)
            {
                player = frameRect.gameObject.AddComponent<VideoPlayer>();
            }

            portraitVideoPlayer = player;
            portraitVideoPlayer.playOnAwake = false;
            portraitVideoPlayer.isLooping = true;
            portraitVideoPlayer.skipOnDrop = true;
            portraitVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            portraitVideoPlayer.source = VideoSource.Url;
            portraitVideoPlayer.url = videoPath;
            portraitVideoPlayer.aspectRatio = VideoAspectRatio.FitVertically;
            portraitVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

            EnsurePortraitRenderTexture();
            portraitVideoPlayer.targetTexture = portraitVideoTexture;
            portraitVideoImage.texture = portraitVideoTexture;

            portraitVideoPlayer.prepareCompleted -= OnPortraitVideoPrepared;
            portraitVideoPlayer.errorReceived -= OnPortraitVideoError;
            portraitVideoPlayer.prepareCompleted += OnPortraitVideoPrepared;
            portraitVideoPlayer.errorReceived += OnPortraitVideoError;
            portraitVideoPlayer.Prepare();
        }

        string ResolvePortraitVideoPath()
        {
            if (string.IsNullOrWhiteSpace(portraitVideoRelativePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(portraitVideoRelativePath))
            {
                return portraitVideoRelativePath;
            }

            string normalizedRelative = portraitVideoRelativePath.Replace('\\', '/').TrimStart('/');
            return Path.Combine(Application.streamingAssetsPath, normalizedRelative);
        }

        void EnsurePortraitRenderTexture()
        {
            const int targetWidth = 1024;
            const int targetHeight = 1536;

            if (portraitVideoTexture != null && portraitVideoTexture.width == targetWidth && portraitVideoTexture.height == targetHeight)
            {
                return;
            }

            if (portraitVideoTexture != null)
            {
                portraitVideoTexture.Release();
                Destroy(portraitVideoTexture);
            }

            portraitVideoTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            portraitVideoTexture.name = "PortraitVideoRT";
            portraitVideoTexture.Create();
        }

        void OnPortraitVideoPrepared(VideoPlayer source)
        {
            if (source == null || source != portraitVideoPlayer)
            {
                return;
            }

            if (portraitVideoImage != null)
            {
                portraitVideoImage.gameObject.SetActive(true);
                portraitVideoImage.color = Color.white;
            }

            if (portraitFallbackImage != null)
            {
                portraitFallbackImage.gameObject.SetActive(false);
            }

            source.Play();
        }

        void OnPortraitVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[PortraitVideo] Failed to play portrait video: {message}");
            ShowPortraitFallback();
        }

        void ShowPortraitFallback()
        {
            if (portraitVideoPlayer != null && portraitVideoPlayer.isPlaying)
            {
                portraitVideoPlayer.Stop();
            }

            if (portraitVideoImage != null)
            {
                portraitVideoImage.gameObject.SetActive(false);
            }

            if (portraitFallbackImage != null)
            {
                portraitFallbackImage.gameObject.SetActive(true);
            }
        }

        static void RemoveChildByName(RectTransform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
            {
                return;
            }

            Transform child = parent.Find(childName);
            if (child == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(child.gameObject);
        }

        void TryAutoLoadPortraitSpriteInEditor()
        {
#if UNITY_EDITOR
            if (portraitFrameSprite == null)
            {
                portraitFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(DefaultPortraitAssetPath);
            }
#endif
            if (portraitFrameSprite == null)
            {
                Debug.LogWarning("[Portrait] portraitFrameSprite is null. Assign ChatBot.portraitFrameSprite in the scene to ensure portrait appears in builds.");
            }
        }

        void EnsureSettingsUi(RectTransform canvasRect, RectTransform portraitLeftRect)
        {
            if (canvasRect == null || portraitLeftRect == null)
            {
                return;
            }

            // 垂直锚点调整为 0.895 - 0.965 以对齐右侧压力条
            openSettingsButton = CreateOrUpdateButton(portraitLeftRect, "OpenSettingsButton", "API设置", new Vector2(0.08f, 0.895f), new Vector2(0.45f, 0.965f), new Color32(115, 83, 52, 235), openSettingsButton);
            settingsPanel = CreateOrUpdatePanel(canvasRect, "SettingsPanel", new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), new Color32(20, 18, 16, 235), settingsPanel);
            RectTransform panelRect = settingsPanel.GetComponent<RectTransform>();
            ConfigureModalPanel(panelRect, new Vector2(620f, 460f));

            CreateOrUpdateLabel(panelRect, "SettingsTitle", "API 设置", 24, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.88f), new Vector2(0.6f, 0.98f), new Color32(236, 223, 194, 255));
            baseUrlInputField = CreateOrUpdateLabeledInputField(panelRect, "BaseUrlInput", "Base URL", "https://api.deepseek.com/chat/completions", new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.82f), baseUrlInputField);
            apiKeyInputField = CreateOrUpdateLabeledInputField(panelRect, "ApiKeyInput", "API Key", "请输入 DeepSeek API Key", new Vector2(0.05f, 0.44f), new Vector2(0.95f, 0.62f), apiKeyInputField);
            modelNameInputField = CreateOrUpdateLabeledInputField(panelRect, "ModelNameInput", "Model Name", "deepseek-chat", new Vector2(0.05f, 0.24f), new Vector2(0.95f, 0.42f), modelNameInputField);
            saveSettingsButton = CreateOrUpdateButton(panelRect, "SaveSettingsButton", "保存", new Vector2(0.6f, 0.06f), new Vector2(0.78f, 0.18f), new Color32(120, 73, 50, 245), saveSettingsButton);
            closeSettingsButton = CreateOrUpdateButton(panelRect, "CloseSettingsButton", "关闭", new Vector2(0.8f, 0.06f), new Vector2(0.95f, 0.18f), new Color32(80, 80, 80, 220), closeSettingsButton);
        }

        void EnsureScriptEditorUi(RectTransform canvasRect, RectTransform portraitLeftRect)
        {
            if (canvasRect == null || portraitLeftRect == null)
            {
                return;
            }

            // 垂直锚点调整为 0.895 - 0.965 以对齐右侧压力条
            openScriptEditorButton = CreateOrUpdateButton(portraitLeftRect, "OpenScriptEditorButton", "剧本设置", new Vector2(0.52f, 0.895f), new Vector2(0.9f, 0.965f), new Color32(115, 83, 52, 235), openScriptEditorButton);
            scriptEditorPanel = CreateOrUpdatePanel(canvasRect, "ScriptEditorPanel", new Vector2(0.14f, 0.1f), new Vector2(0.86f, 0.9f), new Color32(20, 18, 16, 235), scriptEditorPanel);
            RectTransform panelRect = scriptEditorPanel.GetComponent<RectTransform>();
            ConfigureModalPanel(panelRect, new Vector2(660f, 560f));

            CreateOrUpdateLabel(panelRect, "ScriptEditorTitle", "剧本与 NPC 设置", 24, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.9f), new Vector2(0.9f, 0.98f), new Color32(236, 223, 194, 255));
            scenarioBackgroundInputField = CreateOrUpdateLabeledInputField(panelRect, "ScenarioInput", "场景背景", "案情背景与目标", new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.88f), scenarioBackgroundInputField);
            npcNameInputField = CreateOrUpdateLabeledInputField(panelRect, "NpcNameInput", "NPC 名称", "例如：王掌柜", new Vector2(0.05f, 0.60f), new Vector2(0.95f, 0.72f), npcNameInputField);
            npcPersonalityInputField = CreateOrUpdateLabeledInputField(panelRect, "NpcPersonalityInput", "NPC 性格", "例如：圆滑谨慎", new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.58f), npcPersonalityInputField);
            npcSecretInputField = CreateOrUpdateLabeledInputField(panelRect, "NpcSecretInput", "NPC 秘密", "例如：篡改时间线", new Vector2(0.05f, 0.32f), new Vector2(0.95f, 0.44f), npcSecretInputField);
            npcBackgroundInputField = CreateOrUpdateLabeledInputField(panelRect, "NpcBackgroundInput", "NPC 背景", "例如：长安西市酒肆掌柜", new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.30f), npcBackgroundInputField);
            saveScriptEditorButton = CreateOrUpdateButton(panelRect, "SaveScriptEditorButton", "保存", new Vector2(0.6f, 0.03f), new Vector2(0.78f, 0.12f), new Color32(120, 73, 50, 245), saveScriptEditorButton);
            closeScriptEditorButton = CreateOrUpdateButton(panelRect, "CloseScriptEditorButton", "关闭", new Vector2(0.8f, 0.03f), new Vector2(0.95f, 0.12f), new Color32(80, 80, 80, 220), closeScriptEditorButton);
        }

        void ConfigureModalPanel(RectTransform panelRect, Vector2 panelSize)
        {
            if (panelRect == null)
            {
                return;
            }

            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = panelSize;

            Canvas canvas = panelRect.gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = panelRect.gameObject.AddComponent<Canvas>();
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;

            if (panelRect.gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                panelRect.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        void EnsureDialogueScrollArea(RectTransform chatRect)
        {
            if (chatRect == null)
            {
                return;
            }

            Transform scrollTransform = chatRect.Find("DialogueScrollRect");
            GameObject scrollObject = scrollTransform != null
                ? scrollTransform.gameObject
                : new GameObject("DialogueScrollRect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(chatRect, false);
            scrollObject.transform.SetSiblingIndex(0);

            RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            Image scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color32(0, 0, 0, 0);

            Transform viewportTransform = scrollObject.transform.Find("Viewport");
            GameObject viewportObject = viewportTransform != null
                ? viewportTransform.gameObject
                : new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);

            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(0f, dialogueBottomInset);
            viewportRect.offsetMax = Vector2.zero;

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color32(0, 0, 0, 0); // 完全透明

            // 使用 RectMask2D 替代 Mask
            if (viewportObject.GetComponent<RectMask2D>() == null)
            {
                viewportObject.AddComponent<RectMask2D>();
            }
            Mask oldMask = viewportObject.GetComponent<Mask>();
            if (oldMask != null)
            {
                DestroyImmediate(oldMask);
            }

            Transform contentTransform = viewportObject.transform.Find("Content");
            GameObject contentObject = contentTransform != null
                ? contentTransform.gameObject
                : new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);

            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 0f);
            contentRect.pivot = new Vector2(0.5f, 0f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportRect.rect.height, 1f));

            ScrollRect scrollRectComponent = scrollObject.GetComponent<ScrollRect>();
            scrollRectComponent.viewport = viewportRect;
            scrollRectComponent.content = contentRect;
            scrollRectComponent.horizontal = false;
            scrollRectComponent.vertical = true;
            scrollRectComponent.movementType = ScrollRect.MovementType.Clamped;
            scrollRectComponent.scrollSensitivity = 25f;

            dialogueViewportRect = viewportRect;
            dialogueContentRect = contentRect;
            dialogueScrollRect = scrollRectComponent;
        }

        void SetDialogueViewportBottomInset(float bottomInset)
        {
            dialogueBottomInset = Mathf.Max(0f, bottomInset);
            if (dialogueViewportRect == null)
            {
                return;
            }

            dialogueViewportRect.offsetMin = new Vector2(0f, dialogueBottomInset);
            dialogueViewportRect.offsetMax = Vector2.zero;
        }

        InputField CreateOrUpdateLabeledInputField(
            RectTransform parent,
            string name,
            string labelText,
            string placeholder,
            Vector2 anchorMin,
            Vector2 anchorMax,
            InputField existingField = null)
        {
            GameObject groupObject = null;
            if (groupObject == null)
            {
                Transform existing = parent.Find($"{name}Group");
                groupObject = existing != null
                    ? existing.gameObject
                    : new GameObject($"{name}Group", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            }

            groupObject.transform.SetParent(parent, false);
            RectTransform groupRect = groupObject.GetComponent<RectTransform>();
            groupRect.anchorMin = anchorMin;
            groupRect.anchorMax = anchorMax;
            groupRect.offsetMin = Vector2.zero;
            groupRect.offsetMax = Vector2.zero;

            Image groupImage = groupObject.GetComponent<Image>();
            groupImage.color = new Color32(0, 0, 0, 0);

            CreateOrUpdateLabel(
                groupRect,
                "Label",
                labelText,
                15,
                TextAnchor.LowerLeft,
                new Vector2(0f, 0.58f),
                new Vector2(1f, 1f),
                new Color32(221, 211, 188, 255));

            InputField field = CreateOrUpdateInputField(
                groupRect,
                name,
                placeholder,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.56f),
                existingField,
                InputField.LineType.SingleLine);

            return field;
        }

        GameObject CreateOrUpdatePanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color, GameObject existingPanel = null)
        {
            GameObject panelObject = existingPanel;
            if (panelObject == null)
            {
                Transform existing = parent.Find(name);
                panelObject = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            }

            panelObject.transform.SetParent(parent, false);
            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = panelObject.GetComponent<Image>();
            if (image == null) image = panelObject.AddComponent<Image>();
            image.color = color;
            return panelObject;
        }

        void CreateOrUpdateLabel(RectTransform parent, string name, string textValue, int size, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject textObject = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            if (text.font == null) text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.text = textValue;
        }

        Button CreateOrUpdateButton(RectTransform parent, string name, string textValue, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Button existingButton = null)
        {
            GameObject buttonObject = existingButton != null ? existingButton.gameObject : null;
            if (buttonObject == null)
            {
                Transform existing = parent.Find(name);
                buttonObject = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            }

            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = buttonObject.GetComponent<Image>();
            image.color = bgColor;
            Button button = buttonObject.GetComponent<Button>();

            Transform textTransform = buttonObject.transform.Find("Text");
            GameObject textObject = textTransform != null ? textTransform.gameObject : new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text label = textObject.GetComponent<Text>();
            if (label.font == null) label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = textValue;
            return button;
        }

        InputField CreateOrUpdateInputField(
            RectTransform parent,
            string name,
            string placeholder,
            Vector2 anchorMin,
            Vector2 anchorMax,
            InputField existingField = null,
            InputField.LineType lineType = InputField.LineType.SingleLine)
        {
            GameObject inputObject = existingField != null ? existingField.gameObject : null;
            if (inputObject == null)
            {
                Transform existing = parent.Find(name);
                inputObject = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            }

            inputObject.transform.SetParent(parent, false);
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image bg = inputObject.GetComponent<Image>();
            bg.color = new Color32(42, 36, 31, 245);

            InputField field = inputObject.GetComponent<InputField>();
            field.lineType = lineType;

            Transform textTransform = inputObject.transform.Find("Text");
            GameObject textObject = textTransform != null ? textTransform.gameObject : new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.03f, 0.08f);
            textRect.anchorMax = new Vector2(0.97f, 0.92f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            if (text.font == null) text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color32(237, 231, 218, 255);

            Transform placeholderTransform = inputObject.transform.Find("Placeholder");
            GameObject placeholderObject = placeholderTransform != null ? placeholderTransform.gameObject : new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            placeholderObject.transform.SetParent(inputObject.transform, false);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = new Vector2(0.03f, 0.08f);
            placeholderRect.anchorMax = new Vector2(0.97f, 0.92f);
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            Text placeholderText = placeholderObject.GetComponent<Text>();
            if (placeholderText.font == null) placeholderText.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontSize = 16;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = new Color32(181, 173, 156, 180);
            placeholderText.text = placeholder;

            field.textComponent = text;
            field.placeholder = placeholderText;
            return field;
        }

        Slider EnsureStressSlider(RectTransform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject sliderObject = new GameObject("StressSlider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.04f, 0.25f);
            sliderRect.anchorMax = new Vector2(0.68f, 0.75f);
            sliderRect.offsetMin = Vector2.zero;
            sliderRect.offsetMax = Vector2.zero;
            sliderObject.GetComponent<Image>().color = new Color32(66, 54, 42, 255);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderObject.transform, false);
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0.02f, 0.2f);
            fillAreaRect.anchorMax = new Vector2(0.98f, 0.8f);
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillObject.GetComponent<Image>().color = new Color32(150, 76, 58, 255);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.targetGraphic = fillObject.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.value = currentStressValue;
            return slider;
        }

        Text EnsureStressText(RectTransform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject textObject = new GameObject("StressText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.04f, 0.05f);
            textRect.anchorMax = new Vector2(0.68f, 0.3f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textObject.GetComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color32(231, 220, 191, 255);
            text.text = "压力值：0";
            return text;
        }

        Button EnsureEvidenceButton(RectTransform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject buttonObject = new GameObject("EvidenceButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.72f, 0.25f);
            buttonRect.anchorMax = new Vector2(0.98f, 0.75f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            buttonObject.GetComponent<Image>().color = new Color32(119, 72, 50, 230);

            GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "出示证物";

            return buttonObject.GetComponent<Button>();
        }

        public void OnEvidenceButtonClicked()
        {
            if (blockInput)
            {
                return;
            }

            evidenceModePending = true;
            string text = inputBubble != null ? inputBubble.GetText() : string.Empty;
            OnInputFieldSubmit(string.IsNullOrWhiteSpace(text) ? "请解释证物矛盾" : text);
        }

        void LoadOrCreateRuntimeConfig()
        {
            string templatePath = Path.Combine(Application.streamingAssetsPath, RuntimeConfigFileName);
            runtimeConfigPath = Path.Combine(Application.persistentDataPath, RuntimeConfigFileName);
            GameConfigRoot defaultConfig = CreateDefaultConfig();

            try
            {
                if (!File.Exists(runtimeConfigPath))
                {
                    if (File.Exists(templatePath))
                    {
                        File.Copy(templatePath, runtimeConfigPath, false);
                    }
                    else
                    {
                        string initialJson = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                        File.WriteAllText(runtimeConfigPath, initialJson, Encoding.UTF8);
                    }
                }

                string json = File.ReadAllText(runtimeConfigPath, Encoding.UTF8);
                runtimeConfig = JsonConvert.DeserializeObject<GameConfigRoot>(json) ?? defaultConfig;
            }
            catch
            {
                runtimeConfig = defaultConfig;
            }

            if (runtimeConfig.rag_clues == null)
            {
                runtimeConfig.rag_clues = defaultConfig.rag_clues;
            }

            ApplyApiSettingsFromConfig();
        }

        static GameConfigRoot CreateDefaultConfig()
        {
            GameConfigRoot config = new GameConfigRoot();
            config.npcs.Add(new GameNpcConfig
            {
                name = "王掌柜",
                personality = "圆滑谨慎，擅长转移话题",
                secret = "案发当夜为掩护账册问题篡改了时间线",
                background = "长安西市酒肆掌柜，常与商旅往来"
            });

            config.rag_clues.Add(new GameRagClue
            {
                id = "ledger",
                title = "账册缺页",
                content = "案发当夜账册有两页被撕下，缺失时段集中在亥时前后。",
                keywords = new List<string> { "账册", "缺页", "亥时", "账目" }
            });

            config.rag_clues.Add(new GameRagClue
            {
                id = "witness",
                title = "更夫证词",
                content = "更夫称见王掌柜在案发后一刻仍在后巷，衣袖有潮湿水迹。",
                keywords = new List<string> { "更夫", "后巷", "衣袖", "水迹" }
            });

            return config;
        }

        void ApplyApiSettingsFromConfig()
        {
            if (runtimeConfig == null || runtimeConfig.api == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.api.base_url))
            {
                deepSeekBaseUrl = runtimeConfig.api.base_url;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.api.api_key))
            {
                deepSeekApiKey = runtimeConfig.api.api_key;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.api.model_name))
            {
                deepSeekModel = runtimeConfig.api.model_name;
            }
        }

        void LoadApiSettingsFromPrefs()
        {
            deepSeekBaseUrl = PlayerPrefs.GetString(PrefBaseUrl, deepSeekBaseUrl);
            deepSeekApiKey = PlayerPrefs.GetString(PrefApiKey, deepSeekApiKey);
            deepSeekModel = PlayerPrefs.GetString(PrefModel, deepSeekModel);
        }

        void BindSettingsPanel()
        {
            if (openSettingsButton != null)
            {
                openSettingsButton.onClick.RemoveListener(OpenSettingsPanel);
                openSettingsButton.onClick.AddListener(OpenSettingsPanel);
            }

            if (closeSettingsButton != null)
            {
                closeSettingsButton.onClick.RemoveListener(CloseSettingsPanel);
                closeSettingsButton.onClick.AddListener(CloseSettingsPanel);
            }

            if (saveSettingsButton != null)
            {
                saveSettingsButton.onClick.RemoveListener(SaveApiSettingsFromUI);
                saveSettingsButton.onClick.AddListener(SaveApiSettingsFromUI);
            }

            SyncApiSettingsToInputs();

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        void BindScriptEditorPanel()
        {
            if (openScriptEditorButton != null)
            {
                openScriptEditorButton.onClick.RemoveListener(OpenScriptEditorPanel);
                openScriptEditorButton.onClick.AddListener(OpenScriptEditorPanel);
            }

            if (closeScriptEditorButton != null)
            {
                closeScriptEditorButton.onClick.RemoveListener(CloseScriptEditorPanel);
                closeScriptEditorButton.onClick.AddListener(CloseScriptEditorPanel);
            }

            if (saveScriptEditorButton != null)
            {
                saveScriptEditorButton.onClick.RemoveListener(SaveScriptEditsFromUI);
                saveScriptEditorButton.onClick.AddListener(SaveScriptEditsFromUI);
            }

            SyncScriptInputsFromConfig();

            if (scriptEditorPanel != null)
            {
                scriptEditorPanel.SetActive(false);
            }
        }

        void SyncApiSettingsToInputs()
        {
            if (baseUrlInputField != null)
            {
                baseUrlInputField.text = deepSeekBaseUrl;
            }

            if (apiKeyInputField != null)
            {
                apiKeyInputField.text = deepSeekApiKey;
            }

            if (modelNameInputField != null)
            {
                modelNameInputField.text = deepSeekModel;
            }
        }

        void SyncScriptInputsFromConfig()
        {
            if (runtimeConfig == null)
            {
                return;
            }

            if (scenarioBackgroundInputField != null)
            {
                scenarioBackgroundInputField.text = runtimeConfig.scenario_background ?? string.Empty;
            }

            if (runtimeConfig.npcs == null || runtimeConfig.npcs.Count == 0)
            {
                return;
            }

            GameNpcConfig npc = runtimeConfig.npcs[0];
            if (npcNameInputField != null) npcNameInputField.text = npc.name ?? string.Empty;
            if (npcPersonalityInputField != null) npcPersonalityInputField.text = npc.personality ?? string.Empty;
            if (npcSecretInputField != null) npcSecretInputField.text = npc.secret ?? string.Empty;
            if (npcBackgroundInputField != null) npcBackgroundInputField.text = npc.background ?? string.Empty;
        }

        public void OpenSettingsPanel()
        {
            SyncApiSettingsToInputs();
            CloseScriptEditorPanel();
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                settingsPanel.transform.SetAsLastSibling();
            }
        }

        public void CloseSettingsPanel()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        public void ToggleSettingsPanel()
        {
            if (settingsPanel == null)
            {
                return;
            }

            if (settingsPanel.activeSelf)
            {
                CloseSettingsPanel();
            }
            else
            {
                OpenSettingsPanel();
            }
        }

        public void SaveApiSettingsFromUI()
        {
            if (baseUrlInputField != null && !string.IsNullOrWhiteSpace(baseUrlInputField.text))
            {
                deepSeekBaseUrl = baseUrlInputField.text.Trim();
            }

            if (apiKeyInputField != null)
            {
                deepSeekApiKey = apiKeyInputField.text.Trim();
            }

            if (modelNameInputField != null && !string.IsNullOrWhiteSpace(modelNameInputField.text))
            {
                deepSeekModel = modelNameInputField.text.Trim();
            }

            PlayerPrefs.SetString(PrefBaseUrl, deepSeekBaseUrl);
            PlayerPrefs.SetString(PrefApiKey, deepSeekApiKey);
            PlayerPrefs.SetString(PrefModel, deepSeekModel);
            PlayerPrefs.Save();

            if (runtimeConfig != null && runtimeConfig.api != null)
            {
                runtimeConfig.api.base_url = deepSeekBaseUrl;
                runtimeConfig.api.api_key = deepSeekApiKey;
                runtimeConfig.api.model_name = deepSeekModel;
                WriteRuntimeConfigToFile();
            }

            CloseSettingsPanel();
        }

        public void OpenScriptEditorPanel()
        {
            SyncScriptInputsFromConfig();
            CloseSettingsPanel();
            if (scriptEditorPanel != null)
            {
                scriptEditorPanel.SetActive(true);
                scriptEditorPanel.transform.SetAsLastSibling();
            }
        }

        public void CloseScriptEditorPanel()
        {
            if (scriptEditorPanel != null)
            {
                scriptEditorPanel.SetActive(false);
            }
        }

        public void ToggleScriptEditorPanel()
        {
            if (scriptEditorPanel == null)
            {
                return;
            }

            if (scriptEditorPanel.activeSelf)
            {
                CloseScriptEditorPanel();
            }
            else
            {
                OpenScriptEditorPanel();
            }
        }

        public void SaveScriptEditsFromUI()
        {
            if (runtimeConfig == null)
            {
                return;
            }

            // CFG-04: Only update existing template fields, do not add structures.
            if (scenarioBackgroundInputField != null)
            {
                runtimeConfig.scenario_background = scenarioBackgroundInputField.text.Trim();
            }

            if (runtimeConfig.npcs != null && runtimeConfig.npcs.Count > 0)
            {
                GameNpcConfig npc = runtimeConfig.npcs[0];
                if (npcNameInputField != null) npc.name = npcNameInputField.text.Trim();
                if (npcPersonalityInputField != null) npc.personality = npcPersonalityInputField.text.Trim();
                if (npcSecretInputField != null) npc.secret = npcSecretInputField.text.Trim();
                if (npcBackgroundInputField != null) npc.background = npcBackgroundInputField.text.Trim();
            }

            WriteRuntimeConfigToFile();
            CloseScriptEditorPanel();
        }

        void WriteRuntimeConfigToFile()
        {
            if (runtimeConfig == null || string.IsNullOrWhiteSpace(runtimeConfigPath))
            {
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(runtimeConfig, Formatting.Indented);
                File.WriteAllText(runtimeConfigPath, json, Encoding.UTF8);
            }
            catch
            {
                Debug.LogWarning("保存 game_config.json 失败");
            }
        }

        void InitializeConversation()
        {
            conversationHistory.Clear();
            conversationHistory.Add(new DeepSeekMessage
            {
                role = "system",
                content = systemPrompt
            });
        }

        Bubble AddBubble(string message, bool isPlayerMessage)
        {
            Transform bubbleParent = dialogueContentRect != null ? dialogueContentRect : chatContainer;
            Bubble bubble = new Bubble(bubbleParent, isPlayerMessage ? playerUI : aiUI, isPlayerMessage ? "PlayerBubble" : "AIBubble", message);
            chatBubbles.Add(bubble);
            bubble.OnResize(UpdateBubblePositions);
            return bubble;
        }

        void OnInputFieldSubmit(string newText)
        {
            inputBubble.ActivateInputField();
#if ENABLE_INPUT_SYSTEM
            bool shiftHeld = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            if (blockInput || string.IsNullOrWhiteSpace(newText) || shiftHeld)
            {
                StartCoroutine(BlockInteraction());
                return;
            }

            string typedMessage = inputBubble.GetText().Replace("\v", "\n").Trim();
            string message = string.IsNullOrWhiteSpace(typedMessage) ? newText.Trim() : typedMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                StartCoroutine(BlockInteraction());
                return;
            }

            blockInput = true;
            inputBubble.setInteractable(false);
            if (evidenceButton != null)
            {
                evidenceButton.interactable = false;
            }

            string outgoingMessage = evidenceModePending
                ? $"{evidencePrompt}\n用户追问：{message}"
                : message;

            AddBubble(message, true);
            Bubble aiBubble = AddBubble("...", false);
            inputBubble.SetText("");
            _ = HandleChatRequestAsync(outgoingMessage, aiBubble);
            evidenceModePending = false;
        }

        async Task HandleChatRequestAsync(string message, Bubble aiBubble)
        {
            try
            {
                List<DeepSeekMessage> messages = BuildMessagesWithHistory(message);
                DeepSeekClientResult result = await deepSeekClient.SendChatAsync(deepSeekBaseUrl, deepSeekApiKey, deepSeekModel, messages);

                if (!result.IsSuccess)
                {
                    aiBubble.SetText($"请求失败：{result.ErrorMessage}");
                    return;
                }

                InterrogationResponsePayload payload;
                bool usedFallback;
                string formattedText = FormatAiBubbleText(result.Content, out payload, out usedFallback);
                aiBubble.SetText(formattedText);

                conversationHistory.Add(new DeepSeekMessage { role = "user", content = message });
                // Keep assistant history in the same JSON format requested from the model to reduce format drift.
                conversationHistory.Add(new DeepSeekMessage { role = "assistant", content = result.Content ?? string.Empty });
                UpdateStressUI(payload.current_stress ?? currentStressValue);
            }
            catch (Exception ex)
            {
                aiBubble.SetText($"请求失败：{ex.Message}");
            }
            finally
            {
                AllowInput();
            }
        }

        List<DeepSeekMessage> BuildMessagesWithHistory(string latestUserInput)
        {
            List<DeepSeekMessage> requestMessages = new List<DeepSeekMessage>();
            requestMessages.Add(new DeepSeekMessage { role = "system", content = BuildSystemPromptWithRag(latestUserInput) });

            if (conversationHistory.Count > 1)
            {
                int startIndex = Mathf.Max(1, conversationHistory.Count - 6);
                for (int i = startIndex; i < conversationHistory.Count; i++)
                {
                    requestMessages.Add(new DeepSeekMessage
                    {
                        role = conversationHistory[i].role,
                        content = conversationHistory[i].content
                    });
                }
            }

            requestMessages.Add(new DeepSeekMessage { role = "user", content = latestUserInput });
            return requestMessages;
        }

        string BuildSystemPromptWithRag(string latestUserInput)
        {
            string basePrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "你是审讯中的嫌疑人。请严格返回 JSON 对象，字段必须包含：stress_delta, current_stress, emotion_state, inner_thought, dialogue。"
                : systemPrompt;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(basePrompt);
            builder.AppendLine();
            builder.AppendLine("【剧本设定】");
            AppendScenarioAndNpcPrompt(builder);
            builder.AppendLine();
            builder.AppendLine("【审讯规则】");
            builder.AppendLine("- 你必须始终只输出 JSON 对象，不得输出额外文本。");
            builder.AppendLine("- JSON 字段必须包含：stress_delta, current_stress, emotion_state, inner_thought, dialogue。");
            builder.AppendLine("- current_stress 取值范围 0-100。");
            builder.AppendLine("- 非证物追问时，不得直接承认真相，优先回避或辩解。");
            builder.AppendLine($"- 只有当用户消息中明确包含“【证物出示】”时，才允许破防并交代关键真相；此时 stress_delta 应明显上升（建议 >= {evidenceBreakDeltaThreshold}）。");
            builder.AppendLine("- 未出现“【证物出示】”时，不允许破防与完整坦白；stress_delta 变化应较小（建议在 -3 到 +3 内）。");
            builder.AppendLine($"- 当前已知压力值：{currentStressValue}。请根据 stress_delta 计算 current_stress 并保持在 0-100。");

            List<string> snippets = RetrieveLocalRagSnippets(latestUserInput);
            if (snippets.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("【本地线索检索】");
                builder.AppendLine("以下线索来自本地剧本检索，仅作为事实参考，不可编造未给出的信息：");
                for (int i = 0; i < snippets.Count; i++)
                {
                    builder.AppendLine($"- 线索{i + 1}：{snippets[i]}");
                }
            }

            return builder.ToString();
        }

        void AppendScenarioAndNpcPrompt(StringBuilder builder)
        {
            if (builder == null || runtimeConfig == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.scenario_background))
            {
                builder.AppendLine($"- 场景背景：{runtimeConfig.scenario_background}");
            }

            if (runtimeConfig.npcs == null || runtimeConfig.npcs.Count == 0 || runtimeConfig.npcs[0] == null)
            {
                builder.AppendLine("- NPC 设定：王掌柜，圆滑谨慎，擅长转移话题。");
                return;
            }

            GameNpcConfig npc = runtimeConfig.npcs[0];
            if (!string.IsNullOrWhiteSpace(npc.name))
            {
                builder.AppendLine($"- NPC 名称：{npc.name}");
            }

            if (!string.IsNullOrWhiteSpace(npc.personality))
            {
                builder.AppendLine($"- NPC 性格：{npc.personality}");
            }

            if (!string.IsNullOrWhiteSpace(npc.background))
            {
                builder.AppendLine($"- NPC 背景：{npc.background}");
            }

            if (!string.IsNullOrWhiteSpace(npc.secret))
            {
                builder.AppendLine($"- NPC 隐藏真相（仅可在证物触发后承认）：{npc.secret}");
            }
        }

        List<string> RetrieveLocalRagSnippets(string query)
        {
            List<string> snippets = new List<string>();
            if (runtimeConfig == null)
            {
                return snippets;
            }

            List<string> tokens = TokenizeQuery(query);
            if (tokens.Count == 0)
            {
                return snippets;
            }

            List<(string text, int score)> candidates = new List<(string text, int score)>();

            if (runtimeConfig.rag_clues != null)
            {
                foreach (GameRagClue clue in runtimeConfig.rag_clues)
                {
                    if (clue == null || string.IsNullOrWhiteSpace(clue.content))
                    {
                        continue;
                    }

                    int score = 0;
                    foreach (string token in tokens)
                    {
                        if (clue.content.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 2;
                        }

                        if (clue.title != null && clue.title.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 2;
                        }

                        if (clue.keywords != null && clue.keywords.Any(k => !string.IsNullOrWhiteSpace(k) && k.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            score += 3;
                        }
                    }

                    if (score > 0)
                    {
                        candidates.Add(($"{clue.title}：{clue.content}", score));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.scenario_background))
            {
                int bgScore = tokens.Count(t => runtimeConfig.scenario_background.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (bgScore > 0)
                {
                    candidates.Add(($"背景：{runtimeConfig.scenario_background}", bgScore));
                }
            }

            foreach ((string text, int score) in candidates.OrderByDescending(c => c.score).Take(3))
            {
                snippets.Add(text);
            }

            return snippets;
        }

        static List<string> TokenizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<string>();
            }

            char[] separators = { ' ', '\t', '\r', '\n', ',', '，', '。', '！', '？', ';', '；', ':', '：', '.', '"', '\'', '（', '）', '(', ')', '[', ']' };
            List<string> tokens = query.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length >= 1)
                .Distinct()
                .ToList();
            return tokens;
        }

        string FormatAiBubbleText(string rawJson, out InterrogationResponsePayload payload, out bool usedFallback)
        {
            payload = new InterrogationResponsePayload();
            usedFallback = false;

            try
            {
                payload = JsonConvert.DeserializeObject<InterrogationResponsePayload>(rawJson) ?? new InterrogationResponsePayload();
            }
            catch
            {
                usedFallback = true;
                payload = new InterrogationResponsePayload();
            }

            if (string.IsNullOrWhiteSpace(payload.dialogue))
            {
                payload.dialogue = "……";
                usedFallback = true;
            }

            if (!payload.stress_delta.HasValue)
            {
                payload.stress_delta = 0;
                usedFallback = true;
            }

            if (!payload.current_stress.HasValue)
            {
                payload.current_stress = currentStressValue;
                usedFallback = true;
            }

            string deltaText = payload.stress_delta.Value >= 0 ? $"+{payload.stress_delta.Value}" : payload.stress_delta.Value.ToString();
            Debug.Log($"[DeepSeekPayload] stress_delta={deltaText}, current_stress={payload.current_stress.Value}");
            Debug.Log($"[DeepSeekPayload] inner_thought={payload.inner_thought ?? string.Empty}");

            if (usedFallback)
            {
                Debug.LogWarning("[DeepSeekPayload] 部分字段缺失，已使用默认值填充。");
            }

            return payload.dialogue;
        }

        void UpdateStressUI(int newStress)
        {
            currentStressValue = Mathf.Clamp(newStress, 0, 100);

            if (stressSlider != null)
            {
                stressSlider.minValue = 0;
                stressSlider.maxValue = 100;
                stressSlider.value = currentStressValue;
            }

            if (stressText != null)
            {
                stressText.text = $"压力值：{currentStressValue}";
            }
        }

        public void WarmUpCallback()
        {
            warmUpDone = true;
            inputBubble.SetPlaceHolderText("审讯我");
            AllowInput();
        }

        public void AllowInput()
        {
            blockInput = false;
            inputBubble.setInteractable(true);
            inputBubble.ReActivateInputField();
            if (evidenceButton != null)
            {
                evidenceButton.interactable = true;
            }
        }

        public void CancelRequests()
        {
            AllowInput();
        }

        IEnumerator BlockInteraction()
        {
            inputBubble.setInteractable(false);
            yield return null;
            inputBubble.setInteractable(true);
            inputBubble.MoveTextEnd();
        }

        void OnValueChanged(string newText)
        {
#if ENABLE_INPUT_SYSTEM
            bool enterPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
#else
            bool enterPressed = Input.GetKey(KeyCode.Return);
#endif
            if (enterPressed)
            {
                if (inputBubble.GetText().Trim() == "")
                {
                    inputBubble.SetText("");
                }
            }
        }

        public void UpdateBubblePositions()
        {
            if (dialogueContentRect != null)
            {
                float yInContent = bubbleSpacing;
                for (int i = chatBubbles.Count - 1; i >= 0; i--)
                {
                    Bubble bubble = chatBubbles[i];
                    RectTransform childRect = bubble.GetRectTransform();
                    childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, yInContent);
                    yInContent += bubble.GetSize().y + bubbleSpacing;
                }

                float viewportHeight = dialogueViewportRect != null ? dialogueViewportRect.rect.height : 0f;
                dialogueContentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, yInContent));
                lastBubbleOutsideFOV = -1;

                if (dialogueScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    dialogueScrollRect.verticalNormalizedPosition = 0f;
                }

                return;
            }

            float y = inputBubble.GetSize().y + inputBubble.GetRectTransform().offsetMin.y + bubbleSpacing;
            float containerHeight = chatContainer.GetComponent<RectTransform>().rect.height;
            for (int i = chatBubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = chatBubbles[i];
                RectTransform childRect = bubble.GetRectTransform();
                childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, y);

                if (y > containerHeight && lastBubbleOutsideFOV == -1)
                {
                    lastBubbleOutsideFOV = i;
                }

                y += bubble.GetSize().y + bubbleSpacing;
            }
        }

        void Update()
        {
            if (settingsPanel != null && settingsPanel.activeSelf) return;
            if (scriptEditorPanel != null && scriptEditorPanel.activeSelf) return;

            if (!inputBubble.inputFocused() && warmUpDone)
            {
                inputBubble.ActivateInputField();
                StartCoroutine(BlockInteraction());
            }

            if (dialogueContentRect == null && lastBubbleOutsideFOV != -1)
            {
                for (int i = 0; i <= lastBubbleOutsideFOV; i++)
                {
                    chatBubbles[i].Destroy();
                }

                chatBubbles.RemoveRange(0, lastBubbleOutsideFOV + 1);
                lastBubbleOutsideFOV = -1;
            }
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        void OnDestroy()
        {
            if (portraitVideoPlayer != null)
            {
                portraitVideoPlayer.prepareCompleted -= OnPortraitVideoPrepared;
                portraitVideoPlayer.errorReceived -= OnPortraitVideoError;
            }

            if (portraitVideoTexture != null)
            {
                portraitVideoTexture.Release();
                Destroy(portraitVideoTexture);
                portraitVideoTexture = null;
            }
        }
    }
}
