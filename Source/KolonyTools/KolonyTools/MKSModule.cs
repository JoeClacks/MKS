using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using USITools;

namespace KolonyTools
{
    public class MKSModule : PartModule
    {
        [KSPField]
        public bool calculateEfficiency = true;

        [KSPField] 
        public string efficiencyPart = "";

        [KSPField] 
        public int workSpace = 0;

        [KSPField] 
        public int livingSpace = 0;

        [KSPField] 
        public bool hasGenerators = true;

        [KSPField] 
        public string PrimarySkill = "Engineer";

        [KSPField]
        public string SecondarySkill = "Scientist";
        
        [KSPField(guiActive = true, guiName = "Efficiency")]
        public string efficiency = "Unknown";

        [KSPEvent(guiActive = true, guiName = "Governor", active = true)]
        public void ToggleGovernor()
        {
            governorActive = !governorActive;
            EfficiencySetup();
        }

        [KSPField(isPersistant = true)]
        public bool governorActive;

        private bool _showGUI = true;
        private int _numConverters;
        private float _efficiencyRate;
        private void EfficiencySetup()
        {
            _efficiencyRate = GetEfficiency();
        }

        public bool ShowGUI
        {
            get
            {
                return _showGUI;
            }

            set
            {
                _showGUI = value;

                //Hide/show MKSModule gui
                if (Fields["Efficiency"] != null)
                    Fields["Efficiency"].guiActive = _showGUI;

                if (Events["ToggleGovernor"] != null)
                    Events["ToggleGovernor"].guiActive = _showGUI;
            }
        }

        private float GetEfficiency()
        {
            try
            {
                //Efficiency is a function of:
                //  - Workspaces                [numWorkspaces]
                //  - 25% of Crew Cap           [numWorkSpaces]
                //  - Active MKS Module count   [numModules]
                //  - Crew in the module itself [modKerbalFactor]   (0.05 - 3.5 per Kerbal)
                //  - All Kerbals in the crew   [numWeightedKerbals]
                //  - efficiency parts          [added to eff]
                //          Bonus equal to 100 * number of units - 1

                float numWorkspaces = GetKolonyWorkspaces(vessel);
                print("NumWorkspaces: " + numWorkspaces);

                //Plus 25% of Crew Cap as low efficiency workspaces
                numWorkspaces += vessel.GetCrewCapacity()*.25f; 
                print("AdjNumWorkspaces: " + numWorkspaces);

                //Number of active modules
                var numModules = GetActiveKolonyModules(vessel);
                print("numModules: " + numModules);

                //Kerbals in the module
                float modKerbalFactor = part.protoModuleCrew.Sum(k => GetKerbalFactor(k));
                print("modKerbalFactor: " + modKerbalFactor);
                modKerbalFactor *= GetCrewHappiness();
                print("HappymodKerbalFactor: " + modKerbalFactor);

                //Kerbals in the ship
                float numWeightedKerbals = vessel.GetVesselCrew().Sum(k => GetKerbalFactor(k));
                print("numWeightedKerbals: " + numWeightedKerbals);
                numWeightedKerbals *= GetCrewHappiness();
                print("HappynumWeightedKerbals: " + numWeightedKerbals);

                //Worst case, 25% (if crewed).  Uncrewed vessels will be at 0%
                //You need crew for these things, no robo ships.
                float eff = .0f;
                if (vessel.GetCrewCount() > 0)
                {
                    float WorkSpaceKerbalRatio = numWorkspaces / vessel.GetCrewCount();
                    if (WorkSpaceKerbalRatio > 3) WorkSpaceKerbalRatio = 3;
                    print("WorkSpaceKerbalRatio: " + WorkSpaceKerbalRatio);
                    float WorkUnits = WorkSpaceKerbalRatio * numWeightedKerbals;
                    print("WorkUnits: " + WorkUnits);
                    eff = WorkUnits / numModules;
                    print("eff: " + eff);
                    if (eff > 2.5) eff = 2.5f;
                    if (eff < .25) eff = .25f;
                }

                //Add in efficiencyParts 
                if (efficiencyPart != "")
                {
                    print("effpartname: " + efficiencyPart);
                    var effPartNames = efficiencyPart.Split(',')
                        .Select(effPartName => effPartName.Trim().Replace('_', '.'));
                    var genParts = vessel.Parts.Count(p => p.name == part.name);
                    var effPartList = vessel.Parts.Where(p => effPartNames.Contains(p.name));
                    var effParts = 0;

                    foreach (var ep in effPartList)
                    {
                        var mod = ep.FindModuleImplementing<USIAnimation>();
                        if (mod == null)
                        {
                            effParts++;
                        }
                        else
                        {
                            if (mod.isDeployed)
                                effParts++;
                        }
                    }

                    effParts = (effParts - genParts) / genParts;
                    print("effParts: " + effParts);
                    print("oldEff: " + eff);
                    eff += effParts;
                    print("newEff: " + eff); 
                    if (eff < 0.25)  
                        eff = 0.25f;  //We can go as low as 25% as these are almost mandatory.
                }

                if (!calculateEfficiency)
                {
                    eff = 1f;
                    efficiency = String.Format("100% [Fixed]");
                }

                var prefix = "";
                if (governorActive)
                {
                    if (eff > 1f) eff = 1f;
                    prefix = "G:";
                }
                efficiency = String.Format("{5}{0}% [{1}k/{2}s/{3}m/{4}c]", Math.Round((eff * 100), 1), Math.Round(modKerbalFactor, 1), numWorkspaces, numModules, Math.Round(numWeightedKerbals, 1),prefix);

                return eff;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetEfficiency - {0}", ex.Message));
                return 1f;
            }
        }


        private float GetKerbalFactor(ProtoCrewMember k)
        {
            var kerbalFactor = k.experienceLevel / 2f;
            //A level 0 Kerbal is not quite zero - it.s 0.1
            if (kerbalFactor < 0.1)
                kerbalFactor = 0.1f;
            
            // Level 0 Pilot:       0.05
            // Level 0 Engineer:    0.15
            // Level 1 Pilot:       0.25
            // Level 1 Engineer:    0.75
            // Level 2 Pilot:       0.50
            // Level 2 Engineer:    1.50
            // Level 5 Pilot:       1.25
            // Level 5 engineer:    3.25

            //(0.025 - 3.25)
            if (k.experienceTrait.Title == PrimarySkill)
            {
                kerbalFactor *= 1.5f;
            }
            else if (k.experienceTrait.Title == SecondarySkill)
            {
                kerbalFactor *= 1f;
            }
            else 
            {
                kerbalFactor *= 0.5f;
            }
            return kerbalFactor;
        }

        private float GetCrewHappiness()
        {
            //Prototype.  Crew Happiness is a function of the ratio of living space to Kerbals.
            float ls = GetKolonyLivingSpace(vessel);
            //We can add in a limited number for crew capacity - 10%
            ls += vessel.GetCrewCapacity()*.1f;

            var hap = ls/vessel.GetCrewCount();
            //Range is 50% - 150%
            if (hap < .5f) hap = .5f;
            if (hap > 1.5f) hap = 1.5f;
            return hap;
        }
        private int GetActiveKolonyModules(Vessel v)
        {
            try
            {
                var numMods = 0;
                var pList = v.parts.Where(p => p.Modules.Contains("KolonyConverter"));
                foreach (var p in pList)
                {
                    var mods = p.Modules.OfType<KolonyConverter>();
                    numMods += mods.Count(pm => pm.IsActivated);
                }
                return numMods;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetActiveKolonyModules - {0}", ex.Message));
                return 0;
            }
        }


        private int GetKolonyWorkspaces(Vessel v)
        {
            try
            {
                var numWS = 0;
                var pList = v.parts.Where(p => p.Modules.Contains("MKSModule"));
                foreach (var p in pList)
                {
                    var mods = p.Modules.OfType<MKSModule>();
                    numWS += mods.Sum(pm => pm.workSpace);
                }
                return numWS;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetKolonyWorkspaces - {0}", ex.Message));
                return 0;
            }
        }


        private int GetKolonyLivingSpace(Vessel v)
        {
            try
            {
                var numLS = 0;
                var pList = v.parts.Where(p => p.Modules.Contains("MKSModule"));
                foreach (var p in pList)
                {
                    var mods = p.Modules.OfType<MKSModule>();
                    foreach (var pm in mods)
                    {
                        if (p.Modules.Contains("USIAnimation"))
                        {
                            var am = p.Modules.OfType<USIAnimation>().First();
                            if (am.isDeployed)
                            {
                                numLS += pm.livingSpace;
                            }
                        }
                        else
                        {
                            numLS += pm.livingSpace;
                        }
                    }
                }
                return numLS;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetKolonyWorkspaces - {0}", ex.Message));
                return 0;
            }
        }
        

        public virtual float GetEfficiencyRate()
        {
            var curConverters = GetActiveKolonyModules(vessel);
            if (curConverters != _numConverters)
            {
                _numConverters = curConverters;
                EfficiencySetup();
            }
            return _efficiencyRate;
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                if (!hasGenerators)
                {
                    Fields["efficiency"].guiActive = false;
                    Events["ToggleGovernor"].active = false;
                }
            }
            catch (Exception ex)
            {
                print("ERROR IN MKSModuleOnLoad - " + ex.Message);
            }
        }
    }
}
