using Contracts;
using KourageousTourists.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KourageousTourists
{
    public class TouristFactory
    {

        public Dictionary<int, ProtoTourist> touristConfig;
        public bool initialized = false;

        public TouristFactory()
        {
            touristConfig = new Dictionary<int, ProtoTourist>();
            initialized = ReadConfig();
        }

        public Tourist CreateForLevel(int level, ProtoCrewMember crew)
        {

            Tourist t = new Tourist();
            if (!initialized)
            {
                KourageousTouristsAddOn.PrintDebug("TouristFactory not initialized, can't make tourists!");
                return t;
            }

            ProtoTourist pt;
            if (!touristConfig.TryGetValue(level, out pt))
            {
                KourageousTouristsAddOn.PrintDebug("Can't find config for level " + level);
                return t;
            }

            t.Level = pt.Level;
            t.Abilities = pt.Abilities;
            t.Situations = pt.Situations;
            t.CelestialBodies = pt.CelestialBodies;
            t.Srfspeed = pt.Srfspeed;
            t.crew = crew;
            t.rnd = new System.Random();
            t.IsSkydiver = IsSkyDiver(crew);
            return t;
        }

        public static bool IsSkyDiver(ProtoCrewMember crew)
        {
            // Check if this kerbal is participating in any skydiving contract
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
            {
                return false;
            }
            foreach (Contract c in ContractSystem.Instance.Contracts)
            {
                var contract = c as KourageousSkydiveContract;
                if (contract != null)
                {
                    if (contract.hasTourist(crew.name))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private bool ReadConfig()
        {
            KourageousTouristsAddOn.PrintDebug("reading config");
            ConfigNode config = GameDatabase.Instance.GetConfigNodes(KourageousTouristsAddOn.cfgRoot).FirstOrDefault();
            if (config == null)
            {
                KourageousTouristsAddOn.PrintDebug("no config found in game database");
                return false;
            }

            // TODO: Remove this coupling
            String dbg = config.GetValue(KourageousTouristsAddOn.debugLog);
            if (dbg != null)
                KourageousTouristsAddOn.debug = dbg.ToLower().Equals("true");

            ConfigNode[] nodes = config.GetNodes(KourageousTouristsAddOn.cfgLevel);
            foreach (ConfigNode cfg in nodes)
            {

                String tLvl = cfg.GetValue("touristlevel");
                if (tLvl == null)
                {
                    KourageousTouristsAddOn.PrintDebug("tourist config entry has no attribute 'level'");
                    return false;
                }

                KourageousTouristsAddOn.PrintDebug("lvl=" + tLvl);
                ProtoTourist t = new ProtoTourist();
                int lvl;
                if (!Int32.TryParse(tLvl, out lvl))
                {
                    KourageousTouristsAddOn.PrintDebug("Can't parse tourist level as int: " + tLvl);
                    return false;
                }
                t.Level = lvl;

                if (cfg.HasValue("situations"))
                    t.Situations.AddRange(cfg.GetValue("situations").Replace(" ", "").Split(','));
                t.Situations.RemoveAll(str => String.IsNullOrEmpty(str));
                if (cfg.HasValue("bodies"))
                    t.CelestialBodies.AddRange(cfg.GetValue("bodies").Replace(" ", "").Split(','));
                t.CelestialBodies.RemoveAll(str => String.IsNullOrEmpty(str));
                if (cfg.HasValue("abilities"))
                    t.Abilities.AddRange(cfg.GetValue("abilities").Replace(" ", "").Split(','));
                t.Abilities.RemoveAll(str => String.IsNullOrEmpty(str));
                if (cfg.HasValue("srfspeed"))
                {
                    String srfSpeed = cfg.GetValue("srfspeed");
                    KourageousTouristsAddOn.PrintDebug("srfspeed = " + srfSpeed);
                    double spd = 0.0;
                    if (Double.TryParse(srfSpeed, out spd))
                        t.Srfspeed = spd;
                    else
                        t.Srfspeed = Double.NaN;
                }

                KourageousTouristsAddOn.PrintDebug("Adding cfg: " + t.ToString());
                this.touristConfig.Add(lvl, t);
            }
            return true;
        }
    }
}

