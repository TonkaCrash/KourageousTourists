using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace KourageousTourists
{

    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class KourageousTouristsAddOn : MonoBehaviour
    {

        public const String cfgRoot = "KOURAGECONFIG";
        public const String cfgLevel = "LEVEL";
        public const String debugLog = "debuglog";

        private const String audioPath = "KourageousTourists/Sounds/shutter";

        private TouristFactory factory = null;
        // We keep every kerbal in scene in here just to make every one of them smile
        // on photo; some, however, clearly are not tourists
        public Dictionary<String, Tourist> ListOfTourists = null;

        public DateTime selfieTime;

        public Vector3 savedCameraPosition;
        public Quaternion savedCameraRotation;
        public Transform savedCameraTarget;

        bool smile = false;
        bool taken = false;
        private FXGroup fx = null;

        public double RCSamount;
        public double RCSMax;
        internal static bool debug = true;
        public static bool noSkyDiving = false;
        internal static float paraglidingChutePitch = 1.1f;
        internal static float paraglidingDeployDelay = 5f;
        public static float paraglidingMaxAirspeed = 100f;
        public static float paraglidingMinAltAGL = 1500f;

        bool highGee = false;

        public static EventVoid selfieListeners = new EventVoid("Selfie");

        public KourageousTouristsAddOn()
        {
        }

        public void Awake()
        {
            bool forceTouristsInSandbox = false;

            ConfigNode config = GameDatabase.Instance.GetConfigNodes(
                KourageousTouristsAddOn.cfgRoot).FirstOrDefault();
            if (config == null)
            {
                print("KOURAGECONFIG Node not found, are you missing Kourage.cfg file?");
                return;
            }
            String debugState = config.GetValue("debug");
            String noDiving = config.GetValue("noSkyDiving");
            String forceInSandbox = config.GetValue("forceTouristsInSandbox");

            try
            {
                paraglidingChutePitch = float.Parse(config.GetValue("paraglidingChutePitch"));
                paraglidingDeployDelay = float.Parse(config.GetValue("paraglidingDeployDelay"));
                paraglidingMaxAirspeed = float.Parse(config.GetValue("paraglidingMaxAirpseed"));
                paraglidingMinAltAGL = float.Parse(config.GetValue("paraglidingMinAltAGL"));
            }
            catch (Exception)
            {
                print("Kourageous Tourists: Failed parsing paragliding settings in Kourage.cfg!");
            }

            debug = debugState != null &&
                    (debugState.ToLower().Equals("true") || debugState.Equals("1"));
            noSkyDiving = noDiving != null &&
                    (noDiving.ToLower().Equals("true") || noDiving.Equals("1"));
            forceTouristsInSandbox = forceInSandbox != null &&
                          (forceInSandbox.ToLower().Equals("true") || forceInSandbox.Equals("1"));

            // Ignore non-career game mode
            if (HighLogic.CurrentGame == null ||
                (!forceTouristsInSandbox && HighLogic.CurrentGame.Mode != Game.Modes.CAREER))
                return;
            GameEvents.OnVesselRecoveryRequested.Add(OnVesselRecoveryRequested);

            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (factory == null)
                factory = new TouristFactory();
            if (ListOfTourists == null)
                ListOfTourists = new Dictionary<String, Tourist>();

            selfieTime = DateTime.Now;

            GameEvents.onAttemptEva.Add(OnAttemptEVA);
            GameEvents.onCrewBoardVessel.Add(OnCrewBoardVessel);
            GameEvents.onCrewOnEva.Add(OnEvaStart);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onKerbalLevelUp.Add(OnKerbalLevelUp);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselRecovered.Add(OnVesselRecoveredOffGame);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
        }

        public void OnDestroy()
        {
            ListOfTourists = null;
            factory = null; // do we really need this?
            smile = false;
            taken = false;
            fx = null;

            GameEvents.onAttemptEva.Remove(OnAttemptEVA);
            GameEvents.onCrewBoardVessel.Remove(OnCrewBoardVessel);
            GameEvents.onCrewOnEva.Remove(OnEvaStart);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onKerbalLevelUp.Remove(OnKerbalLevelUp);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onVesselRecovered.Remove(OnVesselRecoveredOffGame);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);

            // Switch tourists back
            try
            {
                if (FlightGlobals.VesselsLoaded == null)
                    return;
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    List<ProtoCrewMember> crewList = v.GetVesselCrew();
                    foreach (ProtoCrewMember crew in crewList)
                    {
                        if (Tourist.IsTourist(crew))
                            crew.type = ProtoCrewMember.KerbalType.Tourist;
                    }
                }
            }
            catch (NullReferenceException e)
            {
                print(String.Format("Got NullRef while attempting to access loaded vessels: {0}", e));
            }
        }

        private void OnEvaStart(GameEvents.FromToAction<Part, Part> evaData)
        {
            Vessel v = evaData.to.vessel;
            if (!v || !v.evaController)
                return;
            ProtoCrewMember crew = v.GetVesselCrew()[0];
            if (this.ListOfTourists == null)
            {
                // Why we get here twice with the same data?
                print("KourageousTourists: ListOfTourists is null");
                return;
            }
            if (!ListOfTourists.TryGetValue(crew.name, out Tourist t))
                return;
            if (!Tourist.IsTourist(crew) || t.HasAbility("Jetpack"))
                return;

            // SkyDiving...
            print(String.Format("skydiving: {0}, situation: {1}", t.LooksLikeSkydiving(v), v.situation));
            if (t.LooksLikeSkydiving(v))
            {
                v.evaController.ladderPushoffForce = 50;
                v.evaController.autoGrabLadderOnStart = false;
                StartCoroutine(this.DeployChute(v));
            }
        }

        public IEnumerator DeployChute(Vessel v)
        {
            if (!v.evaController.part.Modules.Contains("ModuleEvaChute"))
            {
                yield break;
            }
            ModuleEvaChute chuteModule = (ModuleEvaChute)v.evaController.part.Modules["ModuleEvaChute"];
            chuteModule.deploymentSafeState = ModuleParachute.deploymentSafeStates.UNSAFE; // FIXME: is it immediate??? 
            yield return new WaitForSeconds(paraglidingDeployDelay); // 5 seconds to deploy chute. TODO: Make configurable
            chuteModule.Deploy();

            // Set low forward pitch so uncontrolled kerbal doesn't gain lot of speed
            chuteModule.chuteDefaultForwardPitch = paraglidingChutePitch;
        }

        private void OnAttemptEVA(ProtoCrewMember crewMemeber, Part part, Transform transform)
        {

            // Can we be sure that all in-scene kerbal tourists were configured?
            if (!Tourist.IsTourist(crewMemeber)) // crew always can EVA
                return;
            if (!ListOfTourists.TryGetValue(crewMemeber.name, out Tourist t))
                return;

            Vessel v = FlightGlobals.ActiveVessel;
            EVAAttempt attempt = t.CanEVA(v);
            if (!attempt.status)
            {
                ScreenMessages.PostScreenMessage("<color=orange>" + attempt.message + "</color>");
                FlightEVA.fetch.overrideEVA = true;
            }
        }
        private void OnVesselCreate(Vessel vessel)
        {
            if (vessel == null)
                return;
            ReinitVessel(vessel);
            ReinitEvents(vessel);

            if (vessel.evaController == null)
                return;
            if (!Tourist.IsTourist(vessel.GetVesselCrew()[0]))
                return;
        }
        private void OnVesselWillDestroy(Vessel vessel)
        {
            if (vessel == null || vessel.evaController == null)
                return;
            if (!ListOfTourists.TryGetValue(vessel.evaController.name, out Tourist t))
                return;
            t.smile = false;
            t.taken = false;
        }

        private void OnCrewBoardVessel(GameEvents.FromToAction<Part, Part> fromto)
        {
            ReinitVessel(fromto.to.vessel);
        }

        private void OnKerbalLevelUp(ProtoCrewMember kerbal)
        {

            if (ListOfTourists == null || !ListOfTourists.ContainsKey(kerbal.name))
                return;
            // Re-create tourist
            ListOfTourists[kerbal.name] = factory.CreateForLevel(kerbal.experienceLevel, kerbal);
        }

        private void CheckApproachingGeeLimit()
        {
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.geeForce < 4.0)
            {// Can there be any tourist with Gee force tolerance below that?
                if (highGee)
                {
                    ReinitVessel(FlightGlobals.ActiveVessel);
                    highGee = false;
                    ScreenMessages.PostScreenMessage("EVA prohibition cleared");
                }
                return;
            }
            if (ListOfTourists == null)
                return;
            foreach (ProtoCrewMember crew in FlightGlobals.ActiveVessel.GetVesselCrew())
            {
                if (!ListOfTourists.ContainsKey(crew.name) || // not among tourists
                    !Tourist.IsTourist(crew) || // not really a tourist
                    crew.type != ProtoCrewMember.KerbalType.Crew)
                {
                    // was probably unpromoted
                    continue;
                }

                if (crew.gExperienced / ProtoCrewMember.GToleranceMult(crew) > 50000)
                { // Magic number. At 60000 kerbal passes out
                    crew.type = ProtoCrewMember.KerbalType.Tourist;
                    ScreenMessages.PostScreenMessage(String.Format(
                        "{0} temporary prohibited from EVA due to experienced high Gee forces", crew.name));
                    highGee = true;
                }
            }
        }

        private void ReinitVessel(Vessel vessel)
        {
            foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            {
                if (Tourist.IsTourist(crew))
                {
                    crew.type = ProtoCrewMember.KerbalType.Crew;
                }

                if (ListOfTourists == null)
                {
                    // TODO: Find out while half of the time we are getting this message
                    continue;
                }
                if (ListOfTourists.ContainsKey(crew.name))
                    continue;

                Tourist t = factory.CreateForLevel(crew.experienceLevel, crew);
                this.ListOfTourists.Add(crew.name, t);
            }
        }

        private void ReinitEvents(Vessel v)
        {
            if (v.evaController == null)
                return;
            KerbalEVA evaCtl = v.evaController;

            ProtoCrewMember crew = v.GetVesselCrew()[0];
            String kerbalName = crew.name;

            if (!ListOfTourists.TryGetValue(kerbalName, out Tourist t))
                return;
            t.smile = false;
            t.taken = false;

            if (!Tourist.IsTourist(v.GetVesselCrew()[0]))
                return;

            // Change crew type right away to avoid them being crew after recovery
            crew.type = ProtoCrewMember.KerbalType.Tourist;

            BaseEventList pEvents = evaCtl.Events;
            foreach (BaseEvent e in pEvents)
            {
                e.guiActive = false;
                e.guiActiveUnfocused = false;
                e.guiActiveUncommand = false;
            }
            // Adding Selfie button
            BaseEventDelegate slf = new BaseEventDelegate(TakeSelfie);
            KSPEvent evt = new KSPEvent
            {
                active = true,
                externalToEVAOnly = true,
                guiActive = true,
                guiActiveEditor = false,
                guiActiveUnfocused = false,
                guiActiveUncommand = false,
                guiName = "Take Selfie",
                name = "TakeSelfie"
            };
            BaseEvent selfie = new BaseEvent(pEvents, "Take Selfie", slf, evt);
            pEvents.Add(selfie);
            selfie.guiActive = true;
            selfie.active = true;

            foreach (PartModule m in evaCtl.part.Modules)
            {
                if (!m.ClassName.Equals("ModuleScienceExperiment"))
                    continue;
                PrintDebug("science module id: " + ((ModuleScienceExperiment)m).experimentID);
                // Disable all science
                foreach (BaseEvent e in m.Events)
                {
                    e.guiActive = false;
                    e.guiActiveUnfocused = false;
                    e.guiActiveUncommand = false;
                }

                foreach (BaseAction a in m.Actions)
                    a.active = false;
            }
            // Should we always invalidate cache???
            fx = null;
            GetOrCreateAudio(evaCtl.part.gameObject);
        }

        private void OnVesselGoOffRails(Vessel vessel)
        {
            ReinitVessel(vessel);
            ReinitEvents(vessel);
        }

        private void OnVesselChange(Vessel vessel)
        {
            if (null == vessel) return;
            if (vessel.evaController == null)
                return;
            OnVesselCreate(vessel);
        }

        private void OnFlightReady()
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                ReinitVessel(v);
        }

        private void OnVesselRecoveryRequested(Vessel vessel)
        {
            // Switch tourists back to tourists
            List<ProtoCrewMember> crewList = vessel.GetVesselCrew();
            foreach (ProtoCrewMember crew in crewList)
            {
                if (Tourist.IsTourist(crew))
                    crew.type = ProtoCrewMember.KerbalType.Tourist;
            }
        }

        private void OnVesselRecoveredOffGame(ProtoVessel vessel, bool wtf)
        {
            // Switch tourists back to tourists
            List<ProtoCrewMember> crewList = vessel.GetVesselCrew();
            foreach (ProtoCrewMember crew in crewList)
            {
                if (Tourist.IsTourist(crew))
                    crew.type = ProtoCrewMember.KerbalType.Tourist;
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            CheckApproachingGeeLimit();

            if (!smile)
                return;

            int sec = (DateTime.Now - selfieTime).Seconds;
            if (!taken && sec > 1)
            {
                FXGroup snd = GetOrCreateAudio(FlightGlobals.ActiveVessel.evaController.gameObject);
                if (snd != null)
                {
                    snd.audio.Play();
                }
                else PrintDebug("snd is null");

                String fname = "../Screenshots/" + GenerateSelfieFileName();
                ScreenCapture.CaptureScreenshot(fname);
                taken = true;
            }

            if (sec > 5)
            {
                smile = false;
                taken = false;
                GameEvents.onShowUI.Fire();
                ScreenMessages.PostScreenMessage("Selfie taken!");
            }
            else
                Smile();
        }
        public void TakeSelfie()
        {
            ScreenMessages.PostScreenMessage("Say Cheese...!");
            smile = true;
            selfieTime = DateTime.Now;
            foreach (Tourist t in ListOfTourists.Values)
                t.GenerateEmotion();

            GameEvents.onHideUI.Fire();
            selfieListeners.Fire();
        }
        private void Smile()
        {
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v.evaController == null)
                    continue;
                KerbalEVA eva = v.evaController;
                ProtoCrewMember crew = v.GetVesselCrew()[0];
                kerbalExpressionSystem expression = GetOrCreateExpressionSystem(eva);
                if (expression != null)
                {
                    if (ListOfTourists.TryGetValue(crew.name, out Tourist t))
                    {
                        expression.wheeLevel = t.whee;
                        expression.fearFactor = t.fear;
                    }
                    else // Allows crew members to always behave nicely!
                    {
                        expression.wheeLevel = 1f;
                        expression.fearFactor = 0f;
                    }
                }
                else
                {
                    print("KourageousTourist: Expression system failure");
                }
            }
        }

        // TODO: Refactor this - now we create audio every time active kerbal is changed
        private FXGroup GetOrCreateAudio(GameObject obj)
        {
            if (obj == null) return null;
            if (fx != null) return fx;

            fx = new FXGroup("SelfieShutter");
            fx.audio = obj.AddComponent<AudioSource>();
            fx.audio.volume = GameSettings.SHIP_VOLUME;
            fx.audio.rolloffMode = AudioRolloffMode.Logarithmic;
            fx.audio.dopplerLevel = 0.0f;
            fx.audio.maxDistance = 30;
            fx.audio.loop = false;
            fx.audio.playOnAwake = false;

            if (GameDatabase.Instance.ExistsAudioClip(audioPath))
                fx.audio.clip = GameDatabase.Instance.GetAudioClip(audioPath);
            else
                print("KourageousTourists: No audio clip found with path " + audioPath);
            return fx;
        }

        private String GenerateSelfieFileName()
        {
            // KerbalName-CelestialBody-Time
            Vessel v = FlightGlobals.ActiveVessel;
            return v.GetVesselCrew()[0].name + "-" + v.mainBody.name + "-" + DateTime.Now.ToString("yy-MM-dd-HH:mm:ss") + ".png";
        }
        private kerbalExpressionSystem GetOrCreateExpressionSystem(KerbalEVA p)
        {

            kerbalExpressionSystem e = p.part.GetComponent<kerbalExpressionSystem>();

            if (e == null)
            {
                AvailablePart evaPrefab = PartLoader.getPartInfoByName("kerbalEVA");
                Part prefabEvaPart = evaPrefab.partPrefab;
                ProtoCrewMember protoCrew = FlightGlobals.ActiveVessel.GetVesselCrew()[0];
                Animator a = p.part.GetComponent<Animator>();
                if (a == null)
                {
                    Animator prefabAnim = prefabEvaPart.GetComponent<Animator>();
                    a = p.part.gameObject.AddComponent<Animator>();
                    a.avatar = prefabAnim.avatar;
                    a.runtimeAnimatorController = prefabAnim.runtimeAnimatorController;
                    a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    a.rootRotation = Quaternion.identity;
                    a.applyRootMotion = false;
                }

                e = p.part.gameObject.AddComponent<kerbalExpressionSystem>();
                e.evaPart = p.part;
                e.animator = a;
                e.protoCrewMember = protoCrew;
            }
            return e;
        }

        internal static void PrintDebug(String message)
        {
            if (!debug)
                return;
            StackTrace trace = new StackTrace();
            String caller = trace.GetFrame(1).GetMethod().Name;
            int line = trace.GetFrame(1).GetFileLineNumber();
            print("KourageousTourists: " + caller + ":" + line + ": " + message);
        }
    }
}
