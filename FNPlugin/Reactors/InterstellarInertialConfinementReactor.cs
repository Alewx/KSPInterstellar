﻿using System;
using System.Linq;
using UnityEngine;

namespace FNPlugin {
    [KSPModule("IC Fusion Reactor")]
    class InterstellarInertialConfinementReactor : InterstellarFusionReactor, IChargedParticleSource
    {
        [KSPField(isPersistant = false, guiActive = true, guiName = "Maintenance")]
        public string laserPower;
        [KSPField(isPersistant = true)]
        protected double accumulatedElectricChargeInMW;

        protected double power_consumed;
        protected bool fusion_alert = false;
        protected int shutdown_c = 0;

        [KSPField(isPersistant = false, guiActive = true, guiName = "Plasma Ratio")]
        public float plasma_ratio = 1.0f;

        [KSPField(isPersistant = false, guiActive = true, guiName = "Charge")]
        public string accumulatedChargeStr = String.Empty;

        protected bool isChargingForJumpstart;

        [KSPEvent(guiActive = true, guiName = "Charge Jumpstart", active = true)]
        public void ChargeStartup()
        {
            isChargingForJumpstart = true;
        }

        public override float StableMaximumReactorPower { get { return IsEnabled && plasma_ratio >= 1 ? RawPowerOutput : 0; } }

        public override string TypeName { get { return (isupgraded ? upgradedName != "" ? upgradedName : originalName : originalName) + " Reactor"; } }

        public override bool IsNeutronRich { get { return !current_fuel_mode.Aneutronic; } }

        public override float MaximumThermalPower
        {
            get
            {
                float thermal_fuel_factor = current_fuel_mode == null ? 1.0f : (float)Math.Sqrt(current_fuel_mode.NormalisedReactionRate);
                float laser_power_4 = plasma_ratio >= 1 ? 1 : 0.000000001f;   //Mathf.Pow(plasma_ratio, 4.0f);
                return isupgraded 
                    ? upgradedPowerOutput != 0 
                        ? laser_power_4 * upgradedPowerOutput * (1.0f - ChargedParticleRatio) * thermal_fuel_factor 
                            : laser_power_4 * PowerOutput * (1.0f - ChargedParticleRatio) * thermal_fuel_factor 
                    : laser_power_4 * PowerOutput * (1.0f - ChargedParticleRatio) * thermal_fuel_factor;
            }
        }

        public override float MaximumChargedPower 
        { 
            get 
            {
                float charged_fuel_factor = current_fuel_mode == null ? 1.0f : (float)Math.Sqrt(current_fuel_mode.NormalisedReactionRate);
                float laser_power_4 = plasma_ratio >= 1 ? 1 : 0.000000001f;  //Mathf.Pow(plasma_ratio, 4.0f);
                return isupgraded 
					? upgradedPowerOutput != 0 
						? laser_power_4 * charged_fuel_factor * upgradedPowerOutput * ChargedParticleRatio 
						: laser_power_4 * charged_fuel_factor * PowerOutput * ChargedParticleRatio 
					: laser_power_4 * charged_fuel_factor * PowerOutput * ChargedParticleRatio; 
            } 
        }

        public override float MinimumPower { get { return MaximumPower * minimumThrottle; } }

	    [KSPField(isPersistant = false, guiActive = true, guiName = "HeatingPowerRequirements")]
	    public float LaserPowerRequirements
	    {
		    get { return current_fuel_mode == null 
				? powerRequirements 
				: (float)(powerRequirements * current_fuel_mode.NormalisedPowerRequirements); }
	    }

        [KSPEvent(guiActive = true, guiName = "Switch Fuel Mode", active = false)]
        public void SwapFuelMode() 
        {
            fuel_mode++;
            if (fuel_mode >= fuel_modes.Count) 
                fuel_mode = 0;
            
            current_fuel_mode = fuel_modes[fuel_mode];
        }
        
        public override bool shouldScaleDownJetISP() 
        {
            return isupgraded ? false : true;
        }

        public override void OnUpdate() 
        {
            if (getCurrentResourceDemand(FNResourceManager.FNRESOURCE_MEGAJOULES) > getStableResourceSupply(FNResourceManager.FNRESOURCE_MEGAJOULES) && getResourceBarRatio(FNResourceManager.FNRESOURCE_MEGAJOULES) < 0.1 && IsEnabled && !fusion_alert) 
            {
                ScreenMessages.PostScreenMessage("Warning: Fusion Reactor plasma heating cannot be guaranteed, reducing power requirements is recommended.", 10.0f, ScreenMessageStyle.UPPER_CENTER);
                fusion_alert = true;
            } 
            else 
                fusion_alert = false;
            
            Events["SwapFuelMode"].active = isupgraded;
            Fields["accumulatedChargeStr"].guiActive = plasma_ratio < 1;


            laserPower = PluginHelper.getFormattedPowerString(power_consumed) + "/" + PluginHelper.getFormattedPowerString(LaserPowerRequirements);
            base.OnUpdate();
        }

        public override void OnFixedUpdate() 
        {
            base.OnFixedUpdate();

	        if (!IsEnabled) return;

            if (isChargingForJumpstart)
            {
                var neededPower = LaserPowerRequirements - accumulatedElectricChargeInMW;
                if (neededPower > 0)
                    accumulatedElectricChargeInMW += part.RequestResource("ElectricCharge", neededPower * 1000) / 1000;

                if (accumulatedElectricChargeInMW >= LaserPowerRequirements)
                    isChargingForJumpstart = false;
            }

            accumulatedChargeStr = FNGenerator.getPowerFormatString(accumulatedElectricChargeInMW) + " / " + FNGenerator.getPowerFormatString(LaserPowerRequirements);

            if (!IsEnabled)
            {
                plasma_ratio = 0;
                power_consumed = 0;
                return;
            }

            power_consumed = consumeFNResource(LaserPowerRequirements * TimeWarp.fixedDeltaTime, FNResourceManager.FNRESOURCE_MEGAJOULES) / TimeWarp.fixedDeltaTime;

            if (TimeWarp.fixedDeltaTime <= 0.1 && accumulatedElectricChargeInMW > 0 && power_consumed < LaserPowerRequirements && (accumulatedElectricChargeInMW + power_consumed) >= LaserPowerRequirements)
            {
                var shortage = LaserPowerRequirements - power_consumed;
                if (shortage <= accumulatedElectricChargeInMW)
                {
                    ScreenMessages.PostScreenMessage("Attempting to Jump start", 5.0f, ScreenMessageStyle.LOWER_CENTER);
                    power_consumed += accumulatedElectricChargeInMW;
                }
            }

	        plasma_ratio = (float)(power_consumed / LaserPowerRequirements);

            if (plasma_ratio >= 1)
            {
                plasma_ratio = 1;

                isChargingForJumpstart = false;
                framesPlasmaRatioIsGood++;
                if (framesPlasmaRatioIsGood > 10)
                    accumulatedElectricChargeInMW = 0;
            }
            else
            {
                framesPlasmaRatioIsGood = 0;

                if (plasma_ratio < 0.001) ;
                    plasma_ratio = 0;
            }
        }

        private int framesPlasmaRatioIsGood;

        public override string getResourceManagerDisplayName() 
        {
            return TypeName;
        }

        public override int getPowerPriority() 
        {
            return 1;
        }

        protected override void setDefaultFuelMode()
        {
            current_fuel_mode = (fuel_mode < fuel_modes.Count) ? fuel_modes[fuel_mode] : fuel_modes.FirstOrDefault();
        }

    }
}
