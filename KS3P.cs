using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.IO;

namespace KSP_PostProcessing
{
    // this got REALLY big all of a sudden...

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class KS3P : MonoBehaviour
    {
        #region LoggingCenter
        internal static void Error(string input, ref List<string> log)
        {
            Debug.LogError("[KS3P]: " + input);
            log.Add("[Err]" + input);
        }
        internal static void Error(string input) { Debug.Log("[KS3P]: " + input); }
        internal static void Exception(string message, Exception e, ref List<string> log)
        {
            Debug.LogException(e);
            log.Add("[Exc]: " + message);
            log.Add("    Stacktrace: " + e.Message);
        }
        internal static void Exception(string message, Exception e)
        {
            Debug.LogException(e);
        }
        internal static void Warning(string input, ref List<string> log)
        {
            Debug.LogWarning("[KS3P]: " + input);
            log.Add("[Wrn]: " + input);
        }
        internal static void Warning(string input) { Debug.LogWarning("[KS3P]: " + input); }
        internal static void Log(string input, ref List<string> log)
        {
            Debug.Log("[KS3P]: " + input);
            log.Add("[Log]: " + input);
        }
        internal static void Log(string input) { Debug.LogWarning("[KS3P]: " + input); }
        #endregion

        // for making spawning the GUI customizable
        KeyCode primary, secondary;

        // for assigning new KeyCode values [for spawning the gui] from a config node
        void SetKeyCode(ConfigNode node)
        {
            if(node != null)
            {
                if(!Parsers.MiscParser.TryParseEnum(node.GetValue("primaryKeycode"), out primary))
                {
                    primary = KeyCode.LeftAlt;
                }
                if(!Parsers.MiscParser.TryParseEnum(node.GetValue("secondaryKeycode"), out secondary))
                {
                    secondary = KeyCode.Alpha3;
                }
            }
            else
            {
                primary = KeyCode.LeftAlt;
                secondary = KeyCode.Alpha3;
            }
        }

        // start up as soon as possible.
        private void Awake()
        {
            // creatively create logger
            List<string> logger = new List<string>()
            {
                "    ----    KS3P loading report    ----"
            };
            Log("Running MonoBehaviour.Awake()!", ref logger);
            
            // make the core module persistent; we'll reuse it later as the GUI-driver.
            DontDestroyOnLoad(this);

            // load shaders
            ShaderLoader.LoadShaders(ref logger);

            // get main config
            string configLocation = Path.Combine(KS3PUtil.Root, "GameData");
            configLocation = Path.Combine(configLocation, "KS3P");
            configLocation = Path.Combine(configLocation, "Configuration.cfg");

            // load keycode preferences from configuration node
            SetKeyCode(ConfigNode.Load(configLocation));

            // save logfile
            File.WriteAllLines(Path.Combine(KS3PUtil.Log, "log.txt"), logger.ToArray());
        }

        /// <summary>
        /// KS3P enabled status
        /// <para>Get: returns the current status of KS3P: enabled or not?</para>
        /// <para>Set: either enables or disables KS3P depending on the value given.</para>
        /// </summary>
        static bool KS3P_Enabled
        {
            get => (cam) ? cam.enabled : false;
            set
            {
                if (cam)
                {
                    cam.enabled = value;
                }
                else
                {
                    Debug.LogWarning("[KS3P]: No camera targeted! Cannot enable or disable behaviour on camera!");
                }
            }
        }

        // the PP behavior manager
        internal static PostProcessingBehaviour cam;

        // Updates the core in response to a scene change.
        internal static void Register(PostProcessingBehaviour target, Scene targetScene)
        {
            cam = target;
            target.profile = loadedProfiles[targetScenes[(int)targetScene]].profile;
        }

        // Registers a new texture references (so the GUI can manage and/or assign them)
        internal static void Register(string path, Texture2D tex, TexType type)
        {
            TextureReference reference = new TextureReference(tex, path, type);
            if(!loadedTextures.Contains(reference))
            {
                loadedTextures.Add(reference);
            }
        }
        internal static string GetPathOf(Texture2D tex)
        {
            for(int i = 0; i < loadedTextures.Count; i++)
            {
                if(loadedTextures[i].tex == tex)
                {
                    return loadedTextures[i].name;
                }
            }
            return null;
        }

        // All available profiles
        static List<Profile> loadedProfiles = new List<Profile>();
        Profile[] cycleProfiles;

        /// <summary>
        /// Gets the desired post-processing profile as per the entered settings.
        /// </summary>
        /// <param name="scene">The scene we want to fetch the profile for.</param>
        /// <returns></returns>
        internal PostProcessingProfile GetProfile(Scene scene)
        {
            return loadedProfiles[targetScenes[(int)scene]].profile;
        }

        // scene enumerator index versus the index of the desired PP profile
        static int[] targetScenes = { 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        /// <summary>
        /// All scenes KS3P can support.
        /// </summary>
        public enum Scene
        {
            MainMenu = 0,
            SpaceCenter = 1,
            VAB = 2,
            SPH = 3,
            TrackingStation = 4,
            Flight = 5,
            EVA = 6,
            IVA = 7,
            MapView = 8
        }

        #region GUI

        // for the GUI: the scene type we are currently dealing with.
        Scene currentlyEditingScene;

        // the scene we are currently in, auto-set in preparation for the main menu.
        internal static Scene currentScene = Scene.MainMenu;

        #region GuiHelpers
        internal enum CurrentMixerChannel
        {
            Red = 0,
            Green = 1,
            Blue = 2
        }
        CurrentMixerChannel channel = CurrentMixerChannel.Red;

        internal enum LogMode
        {
            Slope = 0,
            Power = 1,
            Offset = 2
        }
        LogMode logMode = LogMode.Slope;

        internal enum LinMode
        {
            Lift = 0,
            Gamma = 1,
            Gain = 2
        }
        LinMode linMode = LinMode.Lift;

        struct EnumEntry
        {
            internal int position;
            internal string name;
            internal EnumEntry(int pos, string name)
            {
                position = pos;
                this.name = name;
            }
        }
        struct TextureReference
        {
            internal Texture2D tex;
            internal string name;
            internal TexType type;
            internal TextureReference(Texture2D tex, string name, TexType type)
            {
                this.tex = tex;
                this.name = name;
                this.type = type;
            }
        }
        internal enum TexType
        {
            LensDirt = 0,
            Lut = 1,
            VignetteMask = 2,
            ChromaticTex = 3
        }

        #endregion

        /// <summary>
        /// If true, KS3P encountered a critical error and had to shut down.
        /// </summary>
        internal static bool criticalError = false;
        /// <summary>
        /// The current status of the GUI.
        /// </summary>
        public static bool GuiEnabled { get; internal set; }
        /// <summary>
        /// The GUI window's size.
        /// </summary>
        internal Rect rect = new Rect(0f, 0f, 300f, 500f);

        /// <summary>
        /// The cached value of most GUI items
        /// </summary>
        Dictionary<string, string> data = new Dictionary<string, string>();
        /// <summary>
        /// The cached value of GUI sliders
        /// </summary>
        Dictionary<string, float> sliders = new Dictionary<string, float>();
        /// <summary>
        /// The cached value of GUI booleans
        /// </summary>
        Dictionary<string, bool> toggles = new Dictionary<string, bool>();

        /// <summary>
        /// If true, KS3P will re-build the enumerator cache on the next GUI loop.
        /// </summary>
        bool redrawEnum = true;

        /// <summary>
        /// The current profile used.
        /// </summary>
        internal ushort currentProfile = 0;

        // The name displayed by the GUI window
        string displayname = "KS3P GUI Menu";

        /// <summary>
        /// All possible GUI statuses
        /// </summary>
        internal enum EditorWindowStatus
        {
            MainMenu = 0,
            Setup = 1,
            EditorMain = 2,
            AAEditor = 3,
            AOEditor = 4,
            DOFEditor = 5,
            MBEditor = 6,
            EAEditor = 7,
            BEditor = 8,
            CGEditor = 9,
            ULEditor = 10,
            CAEditor = 11,
            GEditor = 12,
            VEditor = 13,
            DrawingEnum = 14,
            DrawingList = 15,
            None = 16,
            CG_Tonemapper = 17,
            CG_Basic = 18,
            CG_Mixer = 19,
            CG_Trackballs = 20,
            CG_Curves = 21,
            SSREditor = 22,
            ProfileSelector = 23,
            ProfileSettingsEditor = 24,
            SceneSelector = 25
        }

        /// <summary>
        /// The current GUI status
        /// </summary>
        internal EditorWindowStatus CurrentStatus = EditorWindowStatus.MainMenu;
        /// <summary>
        /// The last GUI status
        /// </summary>
        internal EditorWindowStatus LastStatus = EditorWindowStatus.None;

        /// <summary>
        /// Cached type grab of the currently targeted enumerator
        /// </summary>
        Type currentTargetEnum = null;

        /// <summary>
        /// The texture we're currently selecting - for the sake of assigning the ultimately selected texture to the correct texture slot.
        /// </summary>
        TexType currentTargetTex = TexType.ChromaticTex;

        /// <summary>
        /// The enumerator cache.
        /// </summary>
        EnumEntry[] enumData = null;

        /// <summary>
        /// All loaded textures that the GUI can access.
        /// </summary>
        static List<TextureReference> loadedTextures = new List<TextureReference>()
        {
            new TextureReference(null, "None", TexType.ChromaticTex),
            new TextureReference(null, "None", TexType.LensDirt),
            new TextureReference(null, "None", TexType.Lut),
            new TextureReference(null, "None", TexType.VignetteMask)
        };

        /// <summary>
        /// For usage by System.Linq; all textures that the GUI can access that satisfy some arbitrary condition.
        /// </summary>
        TextureReference[] filteredTextures = null;

        /// <summary>
        /// The callback method of the enumerator editor, featuring the target enumerator value, cast to int, as the input parameter.
        /// </summary>
        Action<int> onEnumSelect = null;

        /// <summary>
        /// The callback method of the texture selecter, featuring the chosen texture as the input parameter.
        /// </summary>
        Action<Texture2D> onTexSelect = null;

        // Used internally by the GUI for drawing colors.
        Texture2D generated;

        /// <summary>
        /// Returns true if the GUI is currently editing a PP-effect
        /// </summary>
        /// <returns></returns>
        bool IsEditor()
        {
            return (CurrentStatus != EditorWindowStatus.MainMenu && CurrentStatus != EditorWindowStatus.Setup && CurrentStatus != EditorWindowStatus.EditorMain);
        }
        /// <summary>
        /// Returns true if the GUI is currently editing a Color Grading subsection.
        /// </summary>
        /// <returns></returns>
        bool IsCG()
        {
            return (CurrentStatus == EditorWindowStatus.CG_Basic) || (CurrentStatus == EditorWindowStatus.CG_Curves) || (CurrentStatus == EditorWindowStatus.CG_Mixer) || (CurrentStatus == EditorWindowStatus.CG_Tonemapper) || (CurrentStatus == EditorWindowStatus.CG_Trackballs);
        }

        #region GuiLayout
        /// <summary>
        /// Rect data of the 'close gui' button.
        /// </summary>
        internal Rect closeButtonPosition = new Rect(280f, 2f, 17f, 17f);

        /// <summary>
        /// Rect data of the 'return' button.
        /// </summary>
        internal Rect returnPosition = new Rect(2f, 2f, 50f, 15f);
        


        /// <summary>
        /// The scale of a standalone GUI button.
        /// </summary>
        internal Vector2 buttonSize = new Vector2(250f, 25f);
        /// <summary>
        /// The default x-axis offset of a standalone GUI button.
        /// </summary>
        internal float offsetX = 25f;
        /// <summary>
        /// The default y-axis offset of a standalone GUI button.
        /// </summary>
        internal float offsetY = 25f;



        /// <summary>
        /// The amount of space between GUI elements.
        /// </summary>
        internal float increment = 30f;



        /// <summary>
        /// The size of a field label.
        /// </summary>
        internal Vector2 labelSize = new Vector2(125f, 25f);
        /// <summary>
        /// The default x-axis position of a field label.
        /// </summary>
        internal float labelX = 5f;
        /// <summary>
        /// The default y-axis position of a field label.
        /// </summary>
        internal float labelY = 25f;



        /// <summary>
        /// The size of a field's input area.
        /// </summary>
        internal Vector2 fieldSize = new Vector2(125f, 25f);
        /// <summary>
        /// The default x-axis position of a field's input area.
        /// </summary>
        internal float fieldX = 125f;
        /// <summary>
        /// The default y-axis position of a field's input area.
        /// </summary>
        internal float fieldY = 30f;



        /// <summary>
        /// The size of a field's current value display.
        /// </summary>
        internal Vector2 valueSize = new Vector2(100f, 25f);
        /// <summary>
        /// The default x-axis position of a field's current value display.
        /// </summary>
        internal float valueX = 260f;
        /// <summary>
        /// The default y-axis position of a field's current value display.
        /// </summary>
        internal float valueY = 25f;

        /// <summary>
        /// The position of the 'export' button.
        /// </summary>
        internal Rect exportRect = new Rect(207f, 477f, 90f, 20f);

        /// <summary>
        /// If true, the GUI is editing a curve.
        /// </summary>
        internal bool isEditingCurve = false;

        // are used for automatically computing GUI elements using only an index.

        Rect GetRect(float multiplier)
        {
            return new Rect(new Vector2(offsetX, offsetY + (multiplier * increment)), buttonSize);
        }
        Rect GetRect(float multiplier, float heightScalar)
        {
            return new Rect(new Vector2(offsetX, offsetY + (multiplier * increment)), new Vector2(buttonSize.x, buttonSize.y * heightScalar));
        }
        Rect GetLabel(float multiplier)
        {
            return new Rect(new Vector2(labelX, labelY + (multiplier * increment)), labelSize);
        }
        Rect GetField(float multiplier)
        {
            return new Rect(new Vector2(fieldX, fieldY + (multiplier * increment)), fieldSize);
        }
        Rect GetValue(float multiplier)
        {
            return new Rect(new Vector2(valueX, valueY + (multiplier * increment)), valueSize);
        }
        #endregion


        // with the mod properly prepared, we can now start loading data.
        void Start()
        {
            // cached signleton grab for performance
            GameDatabase database = GameDatabase.Instance;

            // the texture KS3P will resort to if none can be loaded.
            string fallbackpath = "KS3P/Textures/Fallback.png";
            Texture2D fallbacktex = database.GetTexture(fallbackpath, false);

            // register the fallback tex to be eligible for all texture types.
            Register(fallbackpath, fallbacktex, TexType.Lut);
            Register(fallbackpath, fallbacktex, TexType.ChromaticTex);
            Register(fallbackpath, fallbacktex, TexType.LensDirt);
            Register(fallbackpath, fallbacktex, TexType.VignetteMask);

            // load all config files.
            UrlDir.UrlConfig[] cfgs = database.GetConfigs("KS3P");

            // For temporarily saving all profile nodes.
            List<ConfigNode> profilenodes = new List<ConfigNode>();

            // first config pass
            foreach (var cfg in cfgs)
            {
                // index all loaded profiles
                foreach (ConfigNode node in cfg.config.GetNodes("Profile"))
                {
                    profilenodes.Add(node);
                }
                // properly register all loaded textures
                foreach(ConfigNode node in cfg.config.GetNodes("Textures"))
                {
                    foreach(string value in node.GetValues("dirtTex"))
                    {
                        loadedTextures.Add(new TextureReference(database.GetTexture(value, false), value, TexType.LensDirt));
                    }
                    foreach (string value in node.GetValues("maskTex"))
                    {
                        loadedTextures.Add(new TextureReference(database.GetTexture(value, false), value, TexType.VignetteMask));
                    }
                    foreach (string value in node.GetValues("chromaticTex"))
                    {
                        loadedTextures.Add(new TextureReference(database.GetTexture(value, false), value, TexType.ChromaticTex));
                    }
                    foreach (string value in node.GetValues("lutTex"))
                    {
                        loadedTextures.Add(new TextureReference(database.GetTexture(value, false), value, TexType.Lut));
                    }
                }
            }

            // second config pass
            foreach (var profilenode in profilenodes)
            {
                loadedProfiles.Add(profilenode);
            }

            // default initialization
            GuiEnabled = false;

            // create generated texture for color select
            generated = new Texture2D(Mathf.RoundToInt(buttonSize.x), Mathf.RoundToInt(buttonSize.y));
        }



        // GUI update loop
        void OnGUI()
        {
            // do we want the GUI drawn?
            if (GuiEnabled)
            {
                // properly draw the GUI.
                rect = GUI.Window(0, rect, OnRender, displayname);
            }
        }

        /// <summary>
        /// Called when a new profile is selected for a specific scene.
        /// </summary>
        /// <param name="target">The scene we are [re]selecting a profile for.</param>
        void OnProfileSeleted(Scene target)
        {
            if(target == currentScene)
            {
                if(cam.profile == null)
                {
                    cam.profile = GetProfile(currentScene);
                }
            }
        }

        // manual GUI toggle
        void Update()
        {
            if (Input.GetKey(primary) && Input.GetKeyDown(secondary))
            {
                if(GuiEnabled)
                {
                    Debug.Log("[KS3P]: Disabling GUI!");
                }
                else
                {
                    Debug.Log("[KS3P]: Enabling GUI!");
                }
                GuiEnabled = !GuiEnabled;
            }
        }

        // draws the gui. Careful, big one.
        void OnRender(int input)
        {
            // draw the 'close' button
            Button("X", OnPressClose, closeButtonPosition);

            // draw the 'return' button if applicable
            if (CurrentStatus != EditorWindowStatus.MainMenu)
            {
                Button("Return", OnPressReturn, returnPosition);
            }

            // fill in the rest of the GUI depending on the requested window type.
            switch (CurrentStatus)
            {
                #region DrawingEnum
                case EditorWindowStatus.DrawingEnum:
                    displayname = "Select Entry";
                    DrawEnum(currentTargetEnum);
                    break;
                #endregion

                #region DrawingList
                case EditorWindowStatus.DrawingList:
                    DrawList(currentTargetTex);
                    break;
                #endregion

                #region SelectingEditProfile
                case EditorWindowStatus.ProfileSelector:
                    for (ushort i = 0; i < loadedProfiles.Count; i++)
                    {
                        if (GUI.Button(GetRect(i), loadedProfiles[i].identifier))
                        {
                            if (currentProfile != i)
                            {
                                currentProfile = i;
                                data.Clear();
                                sliders.Clear();
                                toggles.Clear();
                            }
                            CurrentStatus = EditorWindowStatus.EditorMain;
                        }
                    }
                    break;
                #endregion

                #region MainMenu
                case EditorWindowStatus.MainMenu:
                    displayname = "KS3P GUI Menu";
                    Text("Setup is for choosing which profile to display for every scene, and for configuring KS3P in general.", GetRect(0f, 3f));
                    Button("Setup", OnPressSetup, GetRect(3f));

                    Text("For when you want to make some small changes to a post-processing profile or create one from scratch.", GetRect(5f, 3f));
                    Button("Profile Editor", OnPressEditor, GetRect(8f));
                    break;
                #endregion

                #region EditorMain
                case EditorWindowStatus.EditorMain:
                    displayname = "KS3P Editor";
                    Text("Currently editing profile [" + loadedProfiles[currentProfile].identifier + "]", GetRect(0f));
                    Button("Select Profile", OnSelectProfile, GetRect(1f));
                    Button("Edit Profile Properties", OnEditProperties, GetRect(2f));
                    Button("Edit Anti-Aliasing", SpawnAAEditor, GetRect(3f));
                    Button("Edit Ambient Occlusion", SpawnAOEditor, GetRect(4f));
                    Button("Edit Bloom", SpawnBEditor, GetRect(5f));
                    Button("Edit Depth Of Field", SpawnDOFEditor, GetRect(6f));
                    Button("Edit Motion Blur", SpawnMBEditor, GetRect(7f));
                    Button("Edit Eye Adaptation", SpawnEAEditor, GetRect(8f));
                    Button("Edit Color Grading", SpawnCGEditor, GetRect(9f));
                    Button("Edit User Lut", SpawnULEditor, GetRect(10f));
                    Button("Edit Chromatic Abberation", SpawnCAEditor, GetRect(11f));
                    Button("Edit Vignette", SpawnVEditor, GetRect(12f));
                    Button("Edit Grain", SpawnGEditor, GetRect(13f));
                    Button("Edit Screen Space Reflection", SpawnSSREditor, GetRect(14f));
                    loadedProfiles[currentProfile].profile.dithering.enabled = BoolField(loadedProfiles[currentProfile].profile.dithering.enabled, GetLabel(15f), "Dithering Enabled");
                    if (GUI.Button(exportRect, "Export Profile"))
                    {
                        ConfigWriter.ToFile(loadedProfiles[currentProfile], loadedProfiles[currentProfile].ProfileName, loadedProfiles[currentProfile].AuthorName);
                    }
                    break;
                #endregion

                #region EditProfileSettings
                case EditorWindowStatus.ProfileSettingsEditor:
                    loadedProfiles[currentProfile].AuthorName = NamedStringField(loadedProfiles[currentProfile].AuthorName, 0f, "Name");
                    loadedProfiles[currentProfile].ProfileName = NamedStringField(loadedProfiles[currentProfile].ProfileName, 1f, "Author");

                    for (int i = 0; i < 9; i++)
                    {
                        loadedProfiles[currentProfile].scenes[i] = CachedBoolField(loadedProfiles[currentProfile].scenes[i], GetRect(i + 2), "s_" + i.ToString(), "Selectable for scene [" + ((Scene)i).ToString() + "]");
                    }

                    break;
                #endregion

                #region Setup
                case EditorWindowStatus.Setup:
                    displayname = "KS3P Setup";

                    for (int i = 0; i < 9; i++)
                    {
                        if (GUI.Button(GetRect(i), "Select profile for scene [" + ((Scene)i).ToString() + "]"))
                        {
                            currentlyEditingScene = (Scene)i;
                            cycleProfiles = loadedProfiles.Where(selectedprofile => selectedprofile.scenes[i]).ToArray();
                            CurrentStatus = EditorWindowStatus.SceneSelector;
                        }
                    }

                    if (KS3P_Enabled)
                    {
                        if (GUI.Button(GetRect(10f), "Disable KS3P"))
                        {
                            KS3P_Enabled = false;
                        }
                    }
                    else
                    {
                        if (GUI.Button(GetRect(10f), "Enable KS3P"))
                        {
                            KS3P_Enabled = true;
                        }
                    }
                    break;
                #endregion

                #region SelectingSceneProfile
                case EditorWindowStatus.SceneSelector:
                    displayname = "Select profile for scene [" + currentlyEditingScene.ToString() + "]";
                    for (int i = 0; i < cycleProfiles.Length; i++)
                    {
                        if (GUI.Button(GetRect(i), cycleProfiles[i].identifier))
                        {
                            targetScenes[(int)currentlyEditingScene] = loadedProfiles.IndexOf(cycleProfiles[i]);
                            OnProfileSeleted(currentlyEditingScene);
                            CurrentStatus = EditorWindowStatus.Setup;
                        }
                    }
                    break;
                #endregion

                #region AntiAliasing
                case EditorWindowStatus.AAEditor:
                    displayname = "KS3P Anti-Aliasing Editor";
                    var settings = loadedProfiles[currentProfile].profile.antialiasing.settings;
                    var fxaa = settings.fxaaSettings;
                    var taa = settings.taaSettings;

                    // settings.method
                    Text("Method", GetLabel(0f));
                    Button(settings.method.ToString(), OnEditAAMethod, GetField(0f));

                    if (settings.method == AntialiasingModel.Method.Fxaa)
                    {
                        Text("FXAA Settings", GetRect(1f));
                        Text("Preset", GetLabel(2f));
                        Button(fxaa.preset.ToString(), OnEditAAPreset, GetField(2f));

                        loadedProfiles[currentProfile].profile.antialiasing.enabled = BoolField(loadedProfiles[currentProfile].profile.antialiasing.enabled, GetRect(4f), "Effect Enabled");
                    }
                    else
                    {
                        Text("TAA Settings", GetRect(1f));

                        // taa.jitterspread
                        Text("Jitter Spread", GetLabel(2f));
                        taa.jitterSpread = FloatSlider(taa.jitterSpread, "taa_js", GetField(2f), 0.1f, 1f);
                        taa.jitterSpread = FloatValue("taa_js", GetValue(2f), 0.1f, 1f);

                        // taa.motionBlending
                        Text("Motion Blending", GetLabel(3f));
                        taa.motionBlending = FloatSlider(taa.motionBlending, "taa_mb", GetField(3f), 0f, 0.99f);
                        taa.motionBlending = FloatValue("taa_mb", GetValue(3f), 0f, 0.99f);

                        // taa.stationaryBlending
                        Text("Stationary Blending", GetLabel(4f));
                        taa.stationaryBlending = FloatSlider(taa.stationaryBlending, "taa_sb", GetField(4f), 0f, 0.99f);
                        taa.stationaryBlending = FloatValue("taa_sb", GetValue(4f), 0f, 0.99f);

                        // taa.sharpen
                        Text("Sharpen", GetLabel(5f));
                        taa.sharpen = FloatSlider(taa.sharpen, "taa_s", GetField(5f), 0f, 3f);
                        taa.sharpen = FloatValue("taa_s", GetValue(5f), 0f, 3f);

                        loadedProfiles[currentProfile].profile.antialiasing.enabled = BoolField(loadedProfiles[currentProfile].profile.antialiasing.enabled, GetRect(7f), "Effect Enabled");
                    }

                    settings.fxaaSettings = fxaa;
                    settings.taaSettings = taa;

                    loadedProfiles[currentProfile].profile.antialiasing.settings = settings;
                    break;
                #endregion

                #region AmbientOcclusion
                case EditorWindowStatus.AOEditor:
                    displayname = "KS3P Ambient Occlusion Editor";
                    var ao_settings = loadedProfiles[currentProfile].profile.ambientOcclusion.settings;

                    Text("Intensity", GetLabel(0f));
                    ao_settings.intensity = FloatSlider(ao_settings.intensity, "ao_i", GetField(0f), 0f, 4f);
                    ao_settings.intensity = FloatValue("ao_i", GetValue(0f), 0f, 4f);

                    Text("Radius", GetLabel(1f));
                    ao_settings.radius = FloatField(ao_settings.radius, "ao_r", GetField(1f));

                    Text("Sample Count", GetLabel(2f));
                    Button(ao_settings.sampleCount.ToString(), OnEditAOSampleCount, GetField(2f));

                    ao_settings.forceForwardCompatibility = CachedBoolField(ao_settings.forceForwardCompatibility, GetRect(3f), "ao_ffc", "Force Forward Compatibility");

                    ao_settings.highPrecision = CachedBoolField(ao_settings.highPrecision, GetRect(4f), "ao_hp", "High Precision (Forward)");

                    ao_settings.ambientOnly = CachedBoolField(ao_settings.ambientOnly, GetRect(5f), "ao_ao", "Ambient Only (Deferred + HDR)");

                    loadedProfiles[currentProfile].profile.ambientOcclusion.enabled = BoolField(loadedProfiles[currentProfile].profile.ambientOcclusion.enabled, GetRect(7f), "Effect Enabled");

                    loadedProfiles[currentProfile].profile.ambientOcclusion.settings = ao_settings;
                    break;
                #endregion

                #region Bloom
                case EditorWindowStatus.BEditor:
                    displayname = "KS3P Bloom Editor";
                    var b_settings = loadedProfiles[currentProfile].profile.bloom.settings;



                    Text("Bloom Settings", GetRect(0f));
                    var bloom = b_settings.bloom;

                    Text("Intensity", GetLabel(1f));
                    bloom.intensity = FloatField(bloom.intensity, "b_i", GetField(1f));

                    Text("Threshold (Gamma)", GetLabel(2f));
                    bloom.threshold = FloatField(bloom.threshold, "b_t", GetField(2f));

                    Text("Soft Knee", GetLabel(3f));
                    bloom.softKnee = FloatSlider(bloom.softKnee, "b_sk", GetField(3f));
                    bloom.softKnee = FloatValue("b_sk", GetValue(3f));

                    Text("Radius", GetLabel(4f));
                    bloom.radius = FloatSlider(bloom.radius, "b_r", GetField(4f), 1f, 7f);
                    bloom.radius = FloatValue("b_r", GetValue(4f), 1f, 7f);

                    bloom.antiFlicker = CachedBoolField(bloom.antiFlicker, GetRect(5f), "b_af", "Anti Flicker");

                    Text("Dirt Settings", GetRect(6f));
                    var dirt = b_settings.lensDirt;

                    // dirt texture
                    Button("Select Dirt Texture", OnEditDirtTex, GetRect(7f));

                    dirt.intensity = FloatField(dirt.intensity, "d_i", GetRect(8f));

                    loadedProfiles[currentProfile].profile.bloom.enabled = BoolField(loadedProfiles[currentProfile].profile.bloom.enabled, GetRect(10f), "Effect Enabled");

                    b_settings.bloom = bloom;
                    b_settings.lensDirt = dirt;
                    loadedProfiles[currentProfile].profile.bloom.settings = b_settings;
                    break;
                #endregion

                #region DepthOfField
                case EditorWindowStatus.DOFEditor:
                    displayname = "KS3P Depth Of Field Editor";
                    var dof_settings = loadedProfiles[currentProfile].profile.depthOfField.settings;

                    Text("Focus Distance", GetLabel(0f));
                    dof_settings.focusDistance = FloatField(dof_settings.focusDistance, "dof_fd", GetField(0f));

                    Text("Aperture (f-stop)", GetLabel(1f));
                    dof_settings.aperture = FloatSlider(dof_settings.aperture, "dof_a", GetField(1f), 0.1f, 32f);
                    dof_settings.aperture = FloatValue("dof_a", GetValue(1f), 0.1f, 32f);

                    dof_settings.useCameraFov = CachedBoolField(dof_settings.useCameraFov, GetRect(2f), "dof_ucf", "Use Camera FOV");

                    if (dof_settings.useCameraFov)
                    {
                        Text("Kernel Size", GetLabel(3f));
                        Button(dof_settings.kernelSize.ToString(), OnEditDOFKernel, GetField(3f));

                        loadedProfiles[currentProfile].profile.depthOfField.settings = dof_settings;
                        loadedProfiles[currentProfile].profile.depthOfField.enabled = BoolField(loadedProfiles[currentProfile].profile.depthOfField.enabled, GetRect(5f), "Effect Enabled");
                    }
                    else
                    {
                        Text("Focal Length (mm)", GetLabel(3f));
                        dof_settings.focalLength = FloatSlider(dof_settings.focalLength, "dof_fl", GetField(3f), 1f, 300f, 0);
                        dof_settings.focalLength = FloatValue("dof_fl", GetValue(3f), 1f, 300f, 0);

                        Text("Kernel Size", GetLabel(4f));
                        Button(dof_settings.kernelSize.ToString(), OnEditDOFKernel, GetField(4f));

                        loadedProfiles[currentProfile].profile.depthOfField.settings = dof_settings;
                        loadedProfiles[currentProfile].profile.depthOfField.enabled = BoolField(loadedProfiles[currentProfile].profile.depthOfField.enabled, GetRect(6f), "Effect Enabled");
                    }
                    break;
                #endregion

                #region MotionBlur
                case EditorWindowStatus.MBEditor:
                    displayname = "KS3P Motion Blur Editor";
                    var mb_settings = loadedProfiles[currentProfile].profile.motionBlur.settings;

                    Text("Shutter Speed Simulation", GetRect(0f));

                    Text("Shutter Angle", GetLabel(1f));
                    mb_settings.shutterAngle = FloatSlider(mb_settings.shutterAngle, "mb_sa", GetField(1f), 0f, 360f, 0);
                    mb_settings.shutterAngle = FloatValue("mb_sa", GetValue(1f), 0f, 360f, 0);

                    Text("Sample Count", GetLabel(2f));
                    mb_settings.sampleCount = IntSlider(mb_settings.sampleCount, "mb_sc", GetField(2f), 4f, 32f);
                    mb_settings.sampleCount = IntValue("mb_sc", GetValue(2f), 4, 32);

                    Text("Multiple Frame Blending", GetRect(4f));
                    DrawFloatSlider("Frame Blending", ref mb_settings.frameBlending, "mb_fb", 5f, 0f, 1f, 3);

                    loadedProfiles[currentProfile].profile.motionBlur.settings = mb_settings;
                    loadedProfiles[currentProfile].profile.motionBlur.enabled = BoolField(loadedProfiles[currentProfile].profile.motionBlur.enabled, GetRect(7f), "Effect Enabled");
                    break;
                #endregion

                #region EyeAdaptation
                case EditorWindowStatus.EAEditor:
                    displayname = "KS3P Eye Adaptation Editor";
                    var ea_settings = loadedProfiles[currentProfile].profile.eyeAdaptation.settings;
                    Text("Luminosity range", GetRect(0f));

                    DrawIntSlider("Minimum (EV)", ref ea_settings.logMin, "ea_lm", 1f, -16, -1);
                    DrawIntSlider("Maximum (EV)", ref ea_settings.logMax, "ea_lM", 2f, 1, 16);

                    Text("Auto exposure", GetRect(3f));
                    DrawFloatSlider("Histogram Lower", ref ea_settings.lowPercent, "ea_hl", 4f, 0f, 100f);
                    DrawFloatSlider("Histogram Upper", ref ea_settings.highPercent, "ea_hu", 5f, 0f, 100f);
                    if (ea_settings.lowPercent > ea_settings.highPercent)
                    {
                        SwapValues(ref ea_settings.lowPercent, ref ea_settings.highPercent, "ea_hl", "ea_hu");
                    }

                    Text("Minimum (EV)", GetLabel(6f));
                    ea_settings.minLuminance = FloatField(ea_settings.minLuminance, "ea_ml", GetField(6f));

                    Text("Maximum (EV)", GetLabel(7f));
                    ea_settings.maxLuminance = FloatField(ea_settings.maxLuminance, "ea_Ml", GetField(7f));

                    ea_settings.dynamicKeyValue = CachedBoolField(ea_settings.dynamicKeyValue, GetRect(8f), "ea_dkv", "Dynamic Key Value");

                    if (ea_settings.dynamicKeyValue)
                    {
                        Text("Adaptation", GetRect(9f));

                        Text("Type", GetLabel(10f));
                        Button(ea_settings.adaptationType.ToString(), OnEditEAType, GetField(10f));

                        Text("Speed Up", GetLabel(11f));
                        ea_settings.speedUp = FloatField(ea_settings.speedUp, "ea_su", GetField(11f));

                        Text("Speed Down", GetLabel(12f));
                        ea_settings.speedDown = FloatField(ea_settings.speedDown, "ea_sd", GetField(12f));

                        loadedProfiles[currentProfile].profile.eyeAdaptation.enabled = BoolField(loadedProfiles[currentProfile].profile.eyeAdaptation.enabled, GetRect(13f), "Effect Enabled");
                    }
                    else
                    {
                        Text("Key Value", GetLabel(9f));
                        ea_settings.keyValue = FloatField(ea_settings.keyValue, "ea_kv", GetField(9f));

                        Text("Adaptation", GetRect(10f));

                        Text("Type", GetLabel(11f));
                        Button(ea_settings.adaptationType.ToString(), OnEditEAType, GetField(11f));

                        Text("Speed Up", GetLabel(12f));
                        ea_settings.speedUp = FloatField(ea_settings.speedUp, "ea_su", GetField(12f));

                        Text("Speed Down", GetLabel(13f));
                        ea_settings.speedDown = FloatField(ea_settings.speedDown, "ea_sd", GetField(13f));

                        loadedProfiles[currentProfile].profile.eyeAdaptation.enabled = BoolField(loadedProfiles[currentProfile].profile.eyeAdaptation.enabled, GetRect(14f), "Effect Enabled");
                    }

                    loadedProfiles[currentProfile].profile.eyeAdaptation.settings = ea_settings;
                    break;
                #endregion

                #region ColorGrading
                case EditorWindowStatus.CGEditor:
                    displayname = "KS3P Color Grading Editor";
                    Button("Tonemapping Settings", SpawnCG_Mapper, GetRect(0f));
                    Button("Basic Settings", SpawnCG_Basic, GetRect(1f));
                    Button("Channel Mixer Settings", SpawnCG_Mixer, GetRect(2f));
                    Button("Trackballs Settings", SpawnCG_Trackballs, GetRect(3f));
                    Button("Curves Settings", SpawnCG_Curve, GetRect(4f));
                    loadedProfiles[currentProfile].profile.colorGrading.enabled = BoolField(loadedProfiles[currentProfile].profile.colorGrading.enabled, GetRect(6f), "Effect Enabled");
                    break;
                #endregion

                #region CG_Curves
                case EditorWindowStatus.CG_Curves:
                    displayname = "KS3P Color Curves Editor";
                    /*
                    for(int i = 0; i < 8; i++)
                    {
                        if(GUI.Button(GetRect(i), "Edit " + ((CurveType)(i)).ToString()))
                        {
                            curveType = (CurveType)i;
                            EditCurve();
                        }
                    }
                    */
                    Text("This is currently not finalized, sorry.", GetRect(3f));
                    break;
                #endregion

                #region CG_Tonemapping
                case EditorWindowStatus.CG_Tonemapper:
                    var tmap_cg_settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
                    var tmap_cg_grading = tmap_cg_settings.tonemapping;

                    displayname = "KS3P Tonemapper settings";

                    Text("Tonemapper", GetLabel(0f));
                    Button(tmap_cg_grading.tonemapper.ToString(), OnEditTonemapperModel, GetField(0f));

                    if (tmap_cg_grading.tonemapper == ColorGradingModel.Tonemapper.Neutral)
                    {
                        DrawFloatSlider("Black In", ref tmap_cg_grading.neutralBlackIn, "cg_tm_bi", 1f, -0.1f, 0.1f, 4);
                        DrawFloatSlider("White In", ref tmap_cg_grading.neutralWhiteIn, "cg_tm_wi", 2f, 1f, 20f);
                        DrawFloatSlider("Black Out", ref tmap_cg_grading.neutralBlackOut, "cg_tm_bo", 3f, -0.09f, 0.1f, 4);
                        DrawFloatSlider("White Out", ref tmap_cg_grading.neutralWhiteOut, "cg_tm_wo", 4f, 1f, 19f);
                        DrawFloatSlider("White Level", ref tmap_cg_grading.neutralWhiteLevel, "cg_tm_wl", 5f, 0.1f, 20f);
                        DrawFloatSlider("White Clip", ref tmap_cg_grading.neutralWhiteClip, "cg_tm_wc", 6f, 1f, 10f);
                    }

                    tmap_cg_settings.tonemapping = tmap_cg_grading;
                    loadedProfiles[currentProfile].profile.colorGrading.settings = tmap_cg_settings;
                    break;
                #endregion

                #region CG_Basic
                case EditorWindowStatus.CG_Basic:
                    var b_cg_settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
                    var basic = b_cg_settings.basic;

                    Text("Post Exposure (EV)", GetLabel(0f));
                    FloatField(basic.postExposure, "cg_b_pe", GetField(0f));
                    DrawFloatSlider("Temperature", ref basic.temperature, "cg_b_te", 1f, -100f, 100f, 1);
                    DrawFloatSlider("Tint", ref basic.tint, "cg_b_ti", 2f, -100f, 100f, 1);
                    DrawFloatSlider("Hue Shift", ref basic.hueShift, "cg_b_hs", 3f, -180f, 180f, 0);
                    DrawFloatSlider("Saturation", ref basic.saturation, "cg_b_s", 4f, 0f, 2f, 3);
                    DrawFloatSlider("Contrast", ref basic.contrast, "cg_b_c", 5f, 0f, 2f, 3);

                    b_cg_settings.basic = basic;
                    loadedProfiles[currentProfile].profile.colorGrading.settings = b_cg_settings;
                    break;
                #endregion

                #region CG_ChannelMixer
                case EditorWindowStatus.CG_Mixer:

                    var mix_cg_settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
                    var mixer_s = mix_cg_settings.channelMixer;
                    Vector3 channelvalues;

                    Text("Target Channel", GetLabel(0f));
                    Button(channel.ToString(), OnEditMixerColor, GetField(0f));

                    switch (channel)
                    {
                        case CurrentMixerChannel.Red:
                            channelvalues = mixer_s.red;
                            break;
                        case CurrentMixerChannel.Green:
                            channelvalues = mixer_s.green;
                            break;
                        case CurrentMixerChannel.Blue:
                            channelvalues = mixer_s.blue;
                            break;
                        default:
                            channelvalues = Vector3.zero;
                            break;
                    }

                    DrawFloatSlider("Red", ref channelvalues.x, "cg_mix_r", 1f, -2f, 2f);
                    DrawFloatSlider("Red", ref channelvalues.y, "cg_mix_g", 2f, -2f, 2f);
                    DrawFloatSlider("Red", ref channelvalues.z, "cg_mix_b", 3f, -2f, 2f);

                    switch (channel)
                    {
                        case CurrentMixerChannel.Red:
                            mixer_s.red = channelvalues;
                            break;
                        case CurrentMixerChannel.Green:
                            mixer_s.green = channelvalues;
                            break;
                        case CurrentMixerChannel.Blue:
                            mixer_s.blue = channelvalues;
                            break;
                    }

                    mix_cg_settings.channelMixer = mixer_s;
                    loadedProfiles[currentProfile].profile.colorGrading.settings = mix_cg_settings;
                    break;
                #endregion

                #region CG_Trackballs
                case EditorWindowStatus.CG_Trackballs:
                    var tb_cg_settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
                    var trackballs = tb_cg_settings.colorWheels;

                    Text("Mode", GetLabel(0f));
                    Button(trackballs.mode.ToString(), OnEditTrackballModel, GetField(0f));

                    Color tb_c;

                    if (trackballs.mode == ColorGradingModel.ColorWheelMode.Linear)
                    {
                        // linear
                        var cg_linear = trackballs.linear;

                        Text("Target", GetLabel(1f));
                        Button(linMode.ToString(), OnEditLinTgt, GetField(1f));

                        if (linMode == LinMode.Gain)
                        {
                            tb_c = cg_linear.gain;
                        }
                        else if (linMode == LinMode.Gamma)
                        {
                            tb_c = cg_linear.gamma;
                        }
                        else
                        {
                            tb_c = cg_linear.lift;
                        }

                        DrawFloatSlider("Red", ref tb_c.r, "cg_tb_r", 3f, 0f, 1f, 3);
                        DrawFloatSlider("Green", ref tb_c.g, "cg_tb_g", 4f, 0f, 1f, 3);
                        DrawFloatSlider("Blue", ref tb_c.b, "cg_tb_b", 5f, 0f, 1f, 3);
                        DrawFloatSlider("Alpha", ref tb_c.a, "cg_tb_a", 6f, -1f, 1f, 3);

                        if (linMode == LinMode.Gain)
                        {
                            cg_linear.gain = tb_c;
                        }
                        else if (linMode == LinMode.Gamma)
                        {
                            cg_linear.gamma = tb_c;
                        }
                        else
                        {
                            cg_linear.lift = tb_c;
                        }

                        SetPixels(tb_c);
                        GUI.Label(GetRect(2f), generated);

                        trackballs.linear = cg_linear;
                        tb_cg_settings.colorWheels = trackballs;
                        loadedProfiles[currentProfile].profile.colorGrading.settings = tb_cg_settings;
                    }
                    else
                    {
                        // log
                        var cg_log = trackballs.log;

                        Text("Target", GetLabel(1f));
                        Button(logMode.ToString(), OnEditLogTgt, GetField(1f));

                        if (logMode == LogMode.Offset)
                        {
                            tb_c = cg_log.offset;
                        }
                        else if (logMode == LogMode.Power)
                        {
                            tb_c = cg_log.power;
                        }
                        else
                        {
                            tb_c = cg_log.slope;
                        }

                        DrawFloatSlider("Red", ref tb_c.r, "cg_tb_r", 3f, 0f, 1f, 3);
                        DrawFloatSlider("Green", ref tb_c.g, "cg_tb_g", 4f, 0f, 1f, 3);
                        DrawFloatSlider("Blue", ref tb_c.b, "cg_tb_b", 5f, 0f, 1f, 3);
                        DrawFloatSlider("Alpha", ref tb_c.a, "cg_tb_a", 6f, -1f, 1f, 3);

                        if (logMode == LogMode.Offset)
                        {
                            cg_log.offset = tb_c;
                        }
                        else if (logMode == LogMode.Power)
                        {
                            cg_log.power = tb_c;
                        }
                        else
                        {
                            cg_log.slope = tb_c;
                        }

                        SetPixels(tb_c);
                        GUI.Label(GetRect(2f), generated);

                        trackballs.log = cg_log;
                        tb_cg_settings.colorWheels = trackballs;
                        loadedProfiles[currentProfile].profile.colorGrading.settings = tb_cg_settings;
                    }
                    break;
                #endregion

                #region UserLut
                case EditorWindowStatus.ULEditor:
                    displayname = "KS3P User Lut Editor";
                    var ul_settings = loadedProfiles[currentProfile].profile.userLut.settings;
                    Button("Select Lut Texture", OnEditLutTex, GetRect(0f));

                    DrawFloatSlider("Contribution", ref ul_settings.contribution, "ul_c", 1f, 0f, 1f, 3);

                    loadedProfiles[currentProfile].profile.userLut.settings = ul_settings;
                    loadedProfiles[currentProfile].profile.userLut.enabled = BoolField(loadedProfiles[currentProfile].profile.userLut.enabled, GetRect(3f), "Effect Enabled");
                    break;
                #endregion

                #region ChromaticAbberation
                case EditorWindowStatus.CAEditor:
                    displayname = "KS3P Chromatic Abberation Editor";
                    var ca_settings = loadedProfiles[currentProfile].profile.chromaticAberration.settings;

                    Button("Select Spectral Texture", OnEditChromTex, GetRect(0f));

                    DrawFloatSlider("Intensity", ref ca_settings.intensity, "ca_i", 1f, 0f, 1f, 3);

                    loadedProfiles[currentProfile].profile.chromaticAberration.settings = ca_settings;
                    loadedProfiles[currentProfile].profile.chromaticAberration.enabled = BoolField(loadedProfiles[currentProfile].profile.chromaticAberration.enabled, GetRect(3f), "Effect Enabled");
                    break;
                #endregion

                #region Grain
                case EditorWindowStatus.GEditor:
                    displayname = "KS3P Grain Editor";
                    var g_settings = loadedProfiles[currentProfile].profile.grain.settings;

                    DrawFloatSlider("Intensity", ref g_settings.intensity, "g_i", 0f, 0f, 1f, 3);
                    DrawFloatSlider("Contribution", ref g_settings.luminanceContribution, "g_lc", 1f, 0f, 1f, 3);
                    DrawFloatSlider("Size", ref g_settings.size, "g_s", 2f, 0.3f, 3f);

                    g_settings.colored = CachedBoolField(g_settings.colored, GetRect(3f), "g_c", "Colored");

                    loadedProfiles[currentProfile].profile.grain.settings = g_settings;

                    loadedProfiles[currentProfile].profile.grain.enabled = BoolField(loadedProfiles[currentProfile].profile.grain.enabled, GetRect(5f), "Effect Enabled");
                    break;
                #endregion

                #region Vignette
                case EditorWindowStatus.VEditor:
                    displayname = "KS3P Vignette Editor";

                    var v_settings = loadedProfiles[currentProfile].profile.vignette.settings;

                    Text("Mode", GetLabel(0f));
                    Button(v_settings.mode.ToString(), OnEditVMode, GetField(0f));

                    Vector2 center = v_settings.center;
                    DrawFloatSlider("Center X", ref center.x, "v_cx", 1f);
                    DrawFloatSlider("Center Y", ref center.y, "v_cy", 2f);
                    v_settings.center = center;

                    DrawFloatSlider("Intensity", ref v_settings.intensity, "v_i", 3f, 0f, 1f, 3);
                    DrawFloatSlider("Smoothness", ref v_settings.smoothness, "v_s", 4f, 0.01f, 1f, 3);
                    DrawFloatSlider("Roundness", ref v_settings.roundness, "v_rn", 5f, 0f, 1f, 3);
                    v_settings.rounded = CachedBoolField(v_settings.rounded, GetRect(6f), "v_r", "Rounded");

                    // color editor
                    SetPixels(v_settings.color);
                    GUI.Label(GetRect(7f), generated);

                    Color maskColor = v_settings.color;
                    DrawFloatSlider("Color, Red", ref maskColor.r, "v_cr", 8f, 0f, 1f, 3);
                    DrawFloatSlider("Color, Green", ref maskColor.g, "v_cg", 9f, 0f, 1f, 3);
                    DrawFloatSlider("Color, Blue", ref maskColor.b, "v_cb", 10f, 0f, 1f, 3);
                    v_settings.color = maskColor;
                    // end color editor

                    loadedProfiles[currentProfile].profile.vignette.settings = v_settings;
                    loadedProfiles[currentProfile].profile.vignette.enabled = BoolField(loadedProfiles[currentProfile].profile.vignette.enabled, GetRect(12f), "Effect Enabled");
                    break;
                #endregion

                #region ScreenSpaceReflection
                case EditorWindowStatus.SSREditor:
                    displayname = "KS3P Screen Space Reflection Editor";
                    var ssr_settings = loadedProfiles[currentProfile].profile.screenSpaceReflection.settings;
                    var ssr_refl = ssr_settings.reflection;
                    var ssr_int = ssr_settings.intensity;
                    var ssr_sem = ssr_settings.screenEdgeMask;

                    // Reflection
                    Text("Blend Type", GetLabel(0f));
                    Button(ssr_refl.blendType.ToString(), OnEditSSRBlendType, GetField(0f));

                    Text("Quality", GetLabel(1f));
                    Button(ssr_refl.reflectionQuality.ToString(), OnEditSSRQuality, GetField(1f));

                    DrawFloatSlider("Max Distance", ref ssr_refl.maxDistance, "ssr_md", 2f, 0f, 300f, 0);
                    DrawIntSlider("Iteration Count", ref ssr_refl.iterationCount, "ssr_ic", 3f, 16, 1024);
                    DrawIntSlider("Step Size", ref ssr_refl.stepSize, "ssr_ss", 4f, 1, 16);
                    DrawFloatSlider("Width Modifier", ref ssr_refl.widthModifier, "ssr_wm", 5f, 0.01f, 10f);
                    DrawFloatSlider("Reflection Blur", ref ssr_refl.reflectionBlur, "ssr_rb", 6f, 0.1f, 8f);
                    ssr_refl.reflectBackfaces = CachedBoolField(ssr_refl.reflectBackfaces, GetRect(7f), "ssr_bf", "Reflect Backfaces");



                    // Intensity
                    DrawFloatSlider("Multiplier", ref ssr_int.reflectionMultiplier, "ssr_mp", 8f, 0f, 2f, 3);
                    DrawFloatSlider("Fade Distance", ref ssr_int.fadeDistance, "ssr_fd", 9f, 0f, 100f, 0);
                    DrawFloatSlider("Fresnel Fade", ref ssr_int.fresnelFade, "ssr_ff", 10f, 0f, 1f, 3);
                    DrawFloatSlider("Fresnel Fade Power", ref ssr_int.fresnelFadePower, "ssr_fp", 11f, 0.1f, 10f);



                    // Screen Edge Mask
                    Text("Screen Edge Mask", GetRect(12f));
                    DrawFloatSlider("Intensity", ref ssr_sem.intensity, "ssr_i", 13f, 0f, 1f, 3);
                    ssr_settings.intensity = ssr_int;
                    ssr_settings.reflection = ssr_refl;
                    ssr_settings.screenEdgeMask = ssr_sem;
                    loadedProfiles[currentProfile].profile.screenSpaceReflection.settings = ssr_settings;
                    loadedProfiles[currentProfile].profile.screenSpaceReflection.enabled = BoolField(loadedProfiles[currentProfile].profile.screenSpaceReflection.enabled, GetRect(14f), "Effect Enabled");
                    break;
                    #endregion
            }

            // so we can drag the window around arbitrarily.
            GUI.DragWindow();
        }
        
        #region GuiMethods

        /// <summary>
        /// Draws a float slider on the GUI.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <param name="input">The value assigned to the field.</param>
        /// <param name="identifier">The key name of this slider's data.</param>
        /// <param name="multiplier">The position of this slider.</param>
        /// <param name="leftBound">The slider's minimum value.</param>
        /// <param name="rightBound">The slider's maximum value.</param>
        /// <param name="digits">The maximum amount of digits to display.</param>
        void DrawFloatSlider(string name, ref float input, string identifier, float multiplier, float leftBound = 0f, float rightBound = 1f, int digits = 2)
        {
            Text(name, GetLabel(multiplier));
            input = FloatSlider(input, identifier, GetField(multiplier), leftBound, rightBound, digits);
            input = FloatValue(identifier, GetValue(multiplier), leftBound, rightBound, digits);
        }

        /// <summary>
        /// Draws an int slider on the GUI.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <param name="input">The value assigned to the field.</param>
        /// <param name="identifier">The key name of this slider's data.</param>
        /// <param name="multiplier">The position of this slider.</param>
        /// <param name="leftBound">The slider's minimum value.</param>
        /// <param name="rightBound">The slider's maximum value.</param>
        void DrawIntSlider(string name, ref int input, string identifier, float multiplier, int leftBound = 0, int rightBound = 1)
        {
            Text(name, GetLabel(multiplier));
            input = IntSlider(input, identifier, GetField(multiplier), leftBound, rightBound);
            input = IntValue(identifier, GetValue(multiplier), leftBound, rightBound);
        }

        /// <summary>
        /// Draws a float input field on the GUI.
        /// </summary>
        /// <param name="defaultValue">The default value of this field.</param>
        /// <param name="identifier">The field's data key.</param>
        /// <param name="position">The position of this field in the GUI.</param>
        /// <param name="digits">The max amount of digits to show.</param>
        /// <returns></returns>
        float FloatField(float defaultValue, string identifier, Rect position, int digits = 2)
        {
            GUI.enabled = GuiEnabled;

            // do we have a stored cache?
            if (data.ContainsKey(identifier))
            {
                // set cache after feeding current cache to GUI
                data[identifier] = GUI.TextField(position, data[identifier]);
            }
            else
            {
                // create cache
                data.Add(identifier, GUI.TextField(position, defaultValue.ToString()));
            }

            float value;
            if (float.TryParse(data[identifier], out value))
            {
                // this value is usable
                GUI.backgroundColor = Color.white;
                value = Round(value, digits);
                data[identifier] = value.ToString();
            }
            else
            {
                // this value is unusable, set to default value internally for safety and set BG to red to notify user
                value = defaultValue;
                GUI.backgroundColor = Color.red;
            }

            // output
            return value;
        }

        /// <summary>
        /// Draws an int input field on the GUI.
        /// </summary>
        /// <param name="defaultValue">The default value of this field.</param>
        /// <param name="identifier">The field's data key.</param>
        /// <param name="position">The field's position on the GUI.</param>
        /// <returns></returns>
        int IntField(int defaultValue, string identifier, Rect position)
        {
            GUI.enabled = GuiEnabled;

            // do we have a stored cache?
            if (data.ContainsKey(identifier))
            {
                // set cache after feeding current cache to GUI
                data[identifier] = GUI.TextField(position, data[identifier]);
            }
            else
            {
                // create cache
                data.Add(identifier, GUI.TextField(position, defaultValue.ToString()));
            }

            int value;
            if (int.TryParse(data[identifier], out value))
            {
                // this value is usable
                GUI.backgroundColor = Color.white;
            }
            else
            {
                // this value is unusable, set to default value internally for safety and set BG to red to notify user
                value = defaultValue;
                GUI.backgroundColor = Color.red;
            }

            // output
            return value;
        }

        float FloatValue(string identifier, Rect position, float min = 0f, float max = 1f, int digits = 2)
        {
            GUI.enabled = GuiEnabled;
            string cache = GUI.TextField(position, Round(sliders[identifier], digits).ToString());
            float parsed;
            if (float.TryParse(cache, out parsed))
            {
                parsed = Mathf.Clamp(Round(parsed, digits), min, max);
                sliders[identifier] = parsed;
                return parsed;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                return sliders[identifier];
            }
        }
        int IntValue(string identifier, Rect position, int min = 0, int max = 1)
        {
            GUI.enabled = GuiEnabled;
            string cache = GUI.TextField(position, Mathf.Floor(sliders[identifier]).ToString());
            int parsed;
            if (int.TryParse(cache, out parsed))
            {
                parsed = Mathf.Clamp(parsed, min, max);
                sliders[identifier] = parsed;
                return parsed;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                return Mathf.FloorToInt(sliders[identifier]);
            }
        }

        bool BoolField(bool defaultValue, Rect position, string name = " Boolean")
        {
            GUI.enabled = GuiEnabled;
            return GUI.Toggle(position, defaultValue, name);
        }
        bool CachedBoolField(bool defaultValue, Rect position, string identifier, string name = " Boolean")
        {
            GUI.enabled = GuiEnabled;
            if (toggles.ContainsKey(identifier))
            {
                toggles[identifier] = GUI.Toggle(position, toggles[identifier], name);
                return toggles[identifier];
            }
            else
            {
                toggles.Add(identifier, GUI.Toggle(position, defaultValue, name));
                return toggles[identifier];
            }
        }

        string StringField(string defaultValue, Rect position)
        {
            GUI.enabled = GuiEnabled;
            return GUI.TextField(position, defaultValue);
        }
        string NamedStringField(string defaultValue, float multiplier, string name)
        {
            GUI.enabled = GuiEnabled;
            Text(name, GetLabel(multiplier));
            return GUI.TextField(GetField(multiplier), defaultValue);
        }

        void Button(string label, Action callback, Rect rect)
        {
            // does the button have functionality?
            if (callback != null)
            {
                GUI.enabled = GuiEnabled;
                if (GUI.Button(rect, label))
                {
                    callback();
                }
            }
        }

        float FloatSlider(float value, string identifier, Rect position, float leftBound = 0f, float rightBound = 1f, int digits = 2)
        {
            GUI.enabled = GuiEnabled;
            if (sliders.ContainsKey(identifier))
            {
                sliders[identifier] = GUI.HorizontalSlider(position, sliders[identifier], leftBound, rightBound);
                return Round(sliders[identifier], digits);
            }
            else
            {
                sliders.Add(identifier, GUI.HorizontalSlider(position, Round(value, digits), leftBound, rightBound));
                return Round(sliders[identifier], digits);
            }
        }
        int IntSlider(int value, string identifier, Rect position, float leftBound = 0f, float rightBound = 1f)
        {
            GUI.enabled = GuiEnabled;
            if (sliders.ContainsKey(identifier))
            {
                sliders[identifier] = GUI.HorizontalSlider(position, sliders[identifier], leftBound, rightBound);
                return Mathf.FloorToInt(sliders[identifier]);
            }
            else
            {
                sliders.Add(identifier, GUI.HorizontalSlider(position, value, leftBound, rightBound));
                return Mathf.FloorToInt(sliders[identifier]);
            }
        }

        /// <summary>
        /// Rounds off a float to the desired amount of digits.
        /// </summary>
        /// <param name="f">The float to round off.</param>
        /// <param name="digits">The max amount of digits.</param>
        /// <returns></returns>
        float Round(float f, int digits)
        {
            // messy, but it works.
            return (float)Math.Round(f, digits);
        }

        /// <summary>
        /// Draws an enumerator on the GUI.
        /// </summary>
        /// <param name="enumType">The type of the enumerator to draw.</param>
        void DrawEnum(Type enumType)
        {
            // initialize database if necessary
            if (redrawEnum)
            {
                var values = Enum.GetValues(enumType);
                enumData = new EnumEntry[values.Length];
                int value;
                for (int i = 0; i < values.Length; i++)
                {
                    value = (int)values.GetValue(i);
                    enumData[i] = new EnumEntry(value, Enum.ToObject(enumType, value).ToString());
                }
                redrawEnum = false;
            }
            // loop and w
            for (int i = 0; i < enumData.Length; i++)
            {
                if (GUI.Button(GetRect(i), enumData[i].name))
                {
                    onEnumSelect(enumData[i].position);
                    redrawEnum = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Draws a list of sorts on the GUI.
        /// </summary>
        /// <param name="mode">The texture type we're selecting for.</param>
        void DrawList(TexType mode)
        {
            if (redrawEnum)
            {
                if (loadedTextures.Count > 0)
                {
                    // gotta love System.Linq!
                    filteredTextures = loadedTextures.Where(reference => reference.type == mode).ToArray();
                }
                else
                {
                    filteredTextures = new TextureReference[0];
                }
                redrawEnum = false;
            }
            if (filteredTextures.Length == 0)
            {
                Text("No textures found!", GetRect(0f));
            }
            else
            {
                for (int i = 0; i < filteredTextures.Length; i++)
                {
                    if (GUI.Button(GetRect(i), filteredTextures[i].name))
                    {
                        switch(mode)
                        {
                            case TexType.ChromaticTex:
                                Debug.Log("[KS3P]: Setting chromatic tex to [" + filteredTextures[i].name + "]");
                                loadedProfiles[currentProfile].chromaticTex = filteredTextures[i].name;
                                break;
                            case TexType.LensDirt:
                                Debug.Log("[KS3P]: Setting dirt tex to [" + filteredTextures[i].name + "]");
                                loadedProfiles[currentProfile].dirtTex = filteredTextures[i].name;
                                break;
                            case TexType.Lut:
                                Debug.Log("[KS3P]: Setting lut tex to [" + filteredTextures[i].name + "]");
                                loadedProfiles[currentProfile].lutTex = filteredTextures[i].name;
                                break;
                            case TexType.VignetteMask:
                                Debug.Log("[KS3P]: Setting mask tex to [" + filteredTextures[i].name + "]");
                                loadedProfiles[currentProfile].vignetteMask = filteredTextures[i].name;
                                break;
                        }
                        onTexSelect(filteredTextures[i].tex);
                        redrawEnum = true;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Swaps the values of two fields.
        /// </summary>
        void SwapValues(ref float left, ref float right, string identifierLeft, string identifierRight)
        {
            float hold = right;
            right = left;
            left = hold;
            hold = sliders[identifierLeft];
            sliders[identifierLeft] = sliders[identifierRight];
            sliders[identifierRight] = hold;
        }

        void Text(string value, Rect pos)
        {
            GUI.Label(pos, value);
        }
        void Text(object o, Rect pos)
        {
            GUI.Label(pos, o.ToString());
        }
        #endregion

        void OnPressSetup()
        {
            CurrentStatus = EditorWindowStatus.Setup;
        }
        void OnPressEditor()
        {
            CurrentStatus = EditorWindowStatus.EditorMain;
        }
        void OnPressClose()
        {
            if (CurrentStatus == EditorWindowStatus.DrawingEnum || CurrentStatus == EditorWindowStatus.DrawingList)
            {
                redrawEnum = true;
            }
            CurrentStatus = EditorWindowStatus.MainMenu;
            GuiEnabled = false;
        }
        void OnPressReturn()
        {
            if (CurrentStatus == EditorWindowStatus.SceneSelector)
            {
                CurrentStatus = EditorWindowStatus.Setup;
            }
            else if (CurrentStatus == EditorWindowStatus.DrawingEnum || CurrentStatus == EditorWindowStatus.DrawingList)
            {
                redrawEnum = true;
                CurrentStatus = LastStatus;
            }
            else if (IsCG())
            {
                CurrentStatus = EditorWindowStatus.CGEditor;
            }
            else if (IsEditor())
            {
                CurrentStatus = EditorWindowStatus.EditorMain;
            }
            else
            {
                CurrentStatus = EditorWindowStatus.MainMenu;
            }
        }

        void OnSelectProfile()
        {
            displayname = "Select Profile for editing";
            CurrentStatus = EditorWindowStatus.ProfileSelector;
        }

        void OnEditProperties()
        {
            CurrentStatus = EditorWindowStatus.ProfileSettingsEditor;
        }

        #region EditorVoids
        void SpawnAAEditor() => CurrentStatus = EditorWindowStatus.AAEditor;
        void SpawnAOEditor() => CurrentStatus = EditorWindowStatus.AOEditor;
        void SpawnBEditor() => CurrentStatus = EditorWindowStatus.BEditor;
        void SpawnDOFEditor() => CurrentStatus = EditorWindowStatus.DOFEditor;
        void SpawnMBEditor() => CurrentStatus = EditorWindowStatus.MBEditor;
        void SpawnEAEditor() => CurrentStatus = EditorWindowStatus.EAEditor;
        void SpawnCGEditor() => CurrentStatus = EditorWindowStatus.CGEditor;
        void SpawnULEditor() => CurrentStatus = EditorWindowStatus.ULEditor;
        void SpawnCAEditor() => CurrentStatus = EditorWindowStatus.CAEditor;
        void SpawnGEditor() => CurrentStatus = EditorWindowStatus.GEditor;
        void SpawnVEditor() => CurrentStatus = EditorWindowStatus.VEditor;
        void SpawnCG_Mapper() => CurrentStatus = EditorWindowStatus.CG_Tonemapper;
        void SpawnCG_Basic() => CurrentStatus = EditorWindowStatus.CG_Basic;
        void SpawnCG_Mixer() => CurrentStatus = EditorWindowStatus.CG_Mixer;
        void SpawnCG_Curve() => CurrentStatus = EditorWindowStatus.CG_Curves;
        void SpawnCG_Trackballs() => CurrentStatus = EditorWindowStatus.CG_Trackballs;
        void SpawnSSREditor() => CurrentStatus = EditorWindowStatus.SSREditor;
        #endregion

        void OnEditAAPreset()
        {
            currentTargetEnum = typeof(AntialiasingModel.FxaaPreset);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectAAPreset;
        }
        void OnSelectAAPreset(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.antialiasing.settings;
            var fxaa = settings.fxaaSettings;

            fxaa.preset = (AntialiasingModel.FxaaPreset)chosen;

            settings.fxaaSettings = fxaa;
            loadedProfiles[currentProfile].profile.antialiasing.settings = settings;

            CurrentStatus = LastStatus;
        }

        void OnEditAAMethod()
        {
            currentTargetEnum = typeof(AntialiasingModel.Method);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectAAMethod;
        }
        void OnSelectAAMethod(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.antialiasing.settings;
            settings.method = (AntialiasingModel.Method)chosen;
            loadedProfiles[currentProfile].profile.antialiasing.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditAOSampleCount()
        {
            currentTargetEnum = typeof(AmbientOcclusionModel.SampleCount);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectAOSampleCount;
        }
        void OnSelectAOSampleCount(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.ambientOcclusion.settings;
            settings.sampleCount = (AmbientOcclusionModel.SampleCount)chosen;
            loadedProfiles[currentProfile].profile.ambientOcclusion.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditDOFKernel()
        {
            currentTargetEnum = typeof(DepthOfFieldModel.KernelSize);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectDOFKernel;
        }
        void OnSelectDOFKernel(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.depthOfField.settings;
            settings.kernelSize = (DepthOfFieldModel.KernelSize)chosen;
            loadedProfiles[currentProfile].profile.depthOfField.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditEAType()
        {
            currentTargetEnum = typeof(EyeAdaptationModel.EyeAdaptationType);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectEAType;
        }
        void OnSelectEAType(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.eyeAdaptation.settings;
            settings.adaptationType = (EyeAdaptationModel.EyeAdaptationType)chosen;
            loadedProfiles[currentProfile].profile.eyeAdaptation.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditVMode()
        {
            currentTargetEnum = typeof(VignetteModel.Mode);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectVMode;
        }
        void OnSelectVMode(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.vignette.settings;
            settings.mode = (VignetteModel.Mode)chosen;
            loadedProfiles[currentProfile].profile.vignette.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditSSRBlendType()
        {
            currentTargetEnum = typeof(ScreenSpaceReflectionModel.SSRReflectionBlendType);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectSSRBlendType;
        }
        void OnSelectSSRBlendType(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.screenSpaceReflection.settings;
            var reflectionsettings = settings.reflection;
            reflectionsettings.blendType = (ScreenSpaceReflectionModel.SSRReflectionBlendType)chosen;
            settings.reflection = reflectionsettings;
            loadedProfiles[currentProfile].profile.screenSpaceReflection.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditSSRQuality()
        {
            currentTargetEnum = typeof(ScreenSpaceReflectionModel.SSRResolution);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectSSRQuality;
        }
        void OnSelectSSRQuality(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.screenSpaceReflection.settings;
            var reflectionsettings = settings.reflection;
            reflectionsettings.reflectionQuality = (ScreenSpaceReflectionModel.SSRResolution)chosen;
            settings.reflection = reflectionsettings;
            loadedProfiles[currentProfile].profile.screenSpaceReflection.settings = settings;
            CurrentStatus = LastStatus;
        }

        void OnEditTonemapperModel()
        {
            currentTargetEnum = typeof(ColorGradingModel.Tonemapper);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectTonemapperModel;
        }
        void OnSelectTonemapperModel(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
            var tonemapsettings = settings.tonemapping;

            tonemapsettings.tonemapper = (ColorGradingModel.Tonemapper)chosen;

            settings.tonemapping = tonemapsettings;
            loadedProfiles[currentProfile].profile.colorGrading.settings = settings;

            CurrentStatus = LastStatus;
        }

        void OnEditTrackballModel()
        {
            currentTargetEnum = typeof(ColorGradingModel.ColorWheelMode);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectTrackballModel;
        }
        void OnSelectTrackballModel(int chosen)
        {
            var settings = loadedProfiles[currentProfile].profile.colorGrading.settings;
            var tbsettings = settings.colorWheels;

            ClearSliderValue("cg_tb_r");
            ClearSliderValue("cg_tb_g");
            ClearSliderValue("cg_tb_b");
            ClearSliderValue("cg_tb_a");

            tbsettings.mode = (ColorGradingModel.ColorWheelMode)chosen;

            settings.colorWheels = tbsettings;
            loadedProfiles[currentProfile].profile.colorGrading.settings = settings;

            CurrentStatus = LastStatus;
        }

        void OnEditMixerColor()
        {
            currentTargetEnum = typeof(CurrentMixerChannel);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectMixerColor;
        }
        void OnSelectMixerColor(int chosen)
        {
            channel = (CurrentMixerChannel)chosen;
            ClearSliderValue("cg_mix_r");
            ClearSliderValue("cg_mix_g");
            ClearSliderValue("cg_mix_b");
            CurrentStatus = LastStatus;
        }

        void OnEditLinTgt()
        {
            currentTargetEnum = typeof(LinMode);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectLinTgt;
        }
        void OnSelectLinTgt(int chosen)
        {
            linMode = (LinMode)chosen;
            ClearSliderValue("cg_tb_r");
            ClearSliderValue("cg_tb_g");
            ClearSliderValue("cg_tb_b");
            ClearSliderValue("cg_tb_a");
            CurrentStatus = LastStatus;
        }

        void OnEditLogTgt()
        {
            currentTargetEnum = typeof(LogMode);
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingEnum;
            onEnumSelect = OnSelectLogTgt;
        }
        void OnSelectLogTgt(int chosen)
        {
            logMode = (LogMode)chosen;
            ClearSliderValue("cg_tb_r");
            ClearSliderValue("cg_tb_g");
            ClearSliderValue("cg_tb_b");
            ClearSliderValue("cg_tb_a");
            CurrentStatus = LastStatus;
        }

        void ClearSliderValue(string key)
        {
            if (sliders.ContainsKey(key))
            {
                sliders.Remove(key);
            }
        }

        // dirt tex
        void OnEditDirtTex()
        {
            displayname = "Select Texture";
            currentTargetTex = TexType.LensDirt;
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingList;
            onTexSelect = OnSelectDirtTex;
        }
        void OnSelectDirtTex(Texture2D tex)
        {
            var settings = loadedProfiles[currentProfile].profile.bloom.settings;
            var dirt = settings.lensDirt;

            dirt.texture = tex;

            settings.lensDirt = dirt;
            loadedProfiles[currentProfile].profile.bloom.settings = settings;

            CurrentStatus = LastStatus;
        }

        // chrom tex
        void OnEditChromTex()
        {
            displayname = "Select Texture";
            currentTargetTex = TexType.ChromaticTex;
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingList;
            onTexSelect = OnSelectChromTex;
        }
        void OnSelectChromTex(Texture2D tex)
        {
            var settings = loadedProfiles[currentProfile].profile.chromaticAberration.settings;
            settings.spectralTexture = tex;
            loadedProfiles[currentProfile].profile.chromaticAberration.settings = settings;
            CurrentStatus = LastStatus;
        }

        // mask tex
        void OnEditMaskTex()
        {
            displayname = "Select Texture";
            currentTargetTex = TexType.VignetteMask;
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingList;
            onTexSelect = OnSelectMaskTex;
        }
        void OnSelectMaskTex(Texture2D tex)
        {
            var settings = loadedProfiles[currentProfile].profile.vignette.settings;

            settings.mask = tex;

            loadedProfiles[currentProfile].profile.vignette.settings = settings;
            CurrentStatus = LastStatus;
        }

        // lut tex
        void OnEditLutTex()
        {
            displayname = "Select Texture";
            currentTargetTex = TexType.Lut;
            LastStatus = CurrentStatus;
            CurrentStatus = EditorWindowStatus.DrawingList;
            onTexSelect = OnSelectLutTex;
        }
        void OnSelectLutTex(Texture2D tex)
        {
            var settings = loadedProfiles[currentProfile].profile.userLut.settings;

            settings.lut = tex;

            loadedProfiles[currentProfile].profile.userLut.settings = settings;
            CurrentStatus = LastStatus;
        }

        void SetPixels(Color c)
        {
            Color col = SetAlpha(c);
            for (int x = 0; x < generated.width; x++)
            {
                for (int y = 0; y < generated.height; y++)
                {
                    generated.SetPixel(x, y, col);
                }
            }
            generated.Apply();
        }
        Color SetAlpha(Color c)
        {
            return new Color(c.r, c.g, c.b, 1f);
        }

        #endregion
    }
}