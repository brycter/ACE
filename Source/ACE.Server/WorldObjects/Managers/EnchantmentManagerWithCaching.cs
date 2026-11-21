using System;
using System.Collections.Generic;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects.Managers
{
    public class EnchantmentManagerWithCaching : EnchantmentManager
    {
        private bool? hasEnchantments;

        public override bool HasEnchantments
        {
            get
            {
                if (hasEnchantments == null)
                    hasEnchantments = base.HasEnchantments;

                return hasEnchantments.Value;
            }
        }

        private bool? hasVitae;

        public override bool HasVitae
        {
            get
            {
                if (hasVitae == null)
                    hasVitae = base.HasVitae;

                return hasVitae.Value;
            }
        }

        /// <summary>
        /// Constructs a new EnchantmentManager for a WorldObject
        /// </summary>
        public EnchantmentManagerWithCaching(WorldObject obj) : base(obj)
        {
        }

        /// <summary>
        /// Add/update an enchantment in this object's registry
        /// </summary>
        public override AddEnchantmentResult Add(Spell spell, WorldObject caster, WorldObject weapon, bool equip = false, bool isWeaponSpell = false)
        {
            var result = base.Add(spell, caster, weapon, equip, isWeaponSpell);

            ClearCache();

            return result;
        }

        /// <summary>
        /// Removes a spell from the enchantment registry, and
        /// sends the relevant network messages for spell removal
        /// </summary>
        public override void Remove(PropertiesEnchantmentRegistry entry, bool sound = true)
        {
            base.Remove(entry, sound);

            if (entry == null)
                return;

            ClearCache();

            if (Player != null)
            {
                if (entry.SpellCategory != (SpellCategory)SpellCategory_Cooldown)
                {
                    var spell = new Spell(entry.SpellId);
                    Player.HandleSpellHooks(spell);
                }
            }
        }

        /// <summary>
        /// Removes all enchantments except for vitae and enchantments from items
        /// Called on player death
        /// </summary>
        public override void RemoveAllEnchantments()
        {
            base.RemoveAllEnchantments();

            ClearCache();
        }

        /// <summary>
        /// Removes all enchantments except for beneficial enchantments, vitae and enchantments from items
        /// Called on player death
        /// </summary>
        public override void RemoveAllBadEnchantments()
        {
            base.RemoveAllBadEnchantments();

            ClearCache();
        }


        /// <summary>
        /// Called on player death
        /// </summary>
        public override float UpdateVitae()
        {
            var result = base.UpdateVitae();

            ClearCache();

            return result;
        }

        /// <summary>
        /// Called when player crosses the VitaeCPPool threshold
        /// </summary>
        public override float ReduceVitae()
        {
            var result = base.ReduceVitae();

            ClearCache();

            return result;
        }


        /// <summary>
        /// Silently removes a spell from the enchantment registry, and sends the relevant network message for dispel
        /// </summary>
        public override void Dispel(PropertiesEnchantmentRegistry entry)
        {
            base.Dispel(entry);

            if (entry == null)
                return;

            ClearCache();

            if (Player != null)
            {
                var spell = new Spell(entry.SpellId);
                Player.HandleSpellHooks(spell);
            }
        }

        /// <summary>
        /// Silently removes multiple spells from the enchantment registry, and sends the relevent network messages for dispel
        /// </summary>
        public override void Dispel(List<PropertiesEnchantmentRegistry> entries)
        {
            base.Dispel(entries);

            ClearCache();

            if (Player != null)
            {
                foreach (var entry in entries)
                {
                    var spell = new Spell(entry.SpellId);
                    Player.HandleSpellHooks(spell);
                }
            }
        }


        private void ClearCache()
        {
            hasEnchantments = null;
            hasVitae = null;

            attributeModAdditiveCache?.Clear();
            attributeModMultiplierCache?.Clear();

            vitalModAdditiveCache?.Clear();
            vitalModMultiplierCache?.Clear();

            skillModAdditiveCache?.Clear();
            skillModMultiplierCache?.Clear();

            bodyArmorModCache = null;
            resistanceModCache?.Clear();
            protectionResistanceModCache?.Clear();
            vulnerabilityResistanceModCache?.Clear();
            regenerationModCache?.Clear();

            damageBonusCache = null;
            damageModCache = null;
            attackModCache = null;
            weaponSpeedModCache = null;
            defenseModCache = null;
            manaConvModCache = null;
            elementalDamageModCache = null;
            varianceModCache = null;
            armorModCache = null;
            armorModVsTypeModCache?.Clear();
            ratingCache?.Clear();
            netherDotDamageRatingCache = null;
            xpBonusCache = null;
            resistLockpickCache = null;
        }


        private Dictionary<PropertyAttribute, int> attributeModAdditiveCache;
        private Dictionary<PropertyAttribute, float> attributeModMultiplierCache;

        /// <summary>
        /// Returns the additive modifiers to an attribute from enchantments
        /// </summary>
        public override int GetAttributeMod_Additive(PropertyAttribute attribute)
        {
            if ((attributeModAdditiveCache ??= new()).TryGetValue(attribute, out var value))
                return value;

            value = base.GetAttributeMod_Additive(attribute);

            attributeModAdditiveCache[attribute] = value;

            return value;
        }

        /// <summary>
        /// Returns the multiplicative modifiers to an attribute from enchantments
        /// </summary>
        public override float GetAttributeMod_Multiplier(PropertyAttribute attribute)
        {
            if ((attributeModMultiplierCache ??= new()).TryGetValue(attribute, out var value))
                return value;

            value = base.GetAttributeMod_Multiplier(attribute);

            attributeModMultiplierCache[attribute] = value;

            return value;
        }

        private Dictionary<CreatureVital, float> vitalModAdditiveCache;
        private Dictionary<CreatureVital, float> vitalModMultiplierCache;

        /// <summary>
        /// Gets the additive modifiers to a vital / secondary attribute
        /// </summary>
        public override float GetVitalMod_Additives(CreatureVital vital)
        {
            if ((vitalModAdditiveCache ??= new()).TryGetValue(vital, out var value))
                return value;

            value = base.GetVitalMod_Additives(vital);

            vitalModAdditiveCache[vital] = value;

            return value;
        }

        /// <summary>
        /// Gets the multiplicative modifiers to a vital / secondary attribute
        /// </summary>
        public override float GetVitalMod_Multiplier(CreatureVital vital)
        {
            if ((vitalModMultiplierCache ??= new()).TryGetValue(vital, out var value))
                return value;

            value = base.GetVitalMod_Multiplier(vital);

            vitalModMultiplierCache[vital] = value;

            return value;
        }

        private Dictionary<Skill, int> skillModAdditiveCache;
        private Dictionary<Skill, float> skillModMultiplierCache;

        /// <summary>
        /// Returns the additive modifiers to a skill from enchantments
        /// </summary>
        public override int GetSkillMod_Additives(Skill skill)
        {
            if ((skillModAdditiveCache ??= new()).TryGetValue(skill, out var value))
                return value;

            value = base.GetSkillMod_Additives(skill);

            skillModAdditiveCache[skill] = value;

            return value;
        }

        /// <summary>
        /// Returns the multiplicative modifiers to a skill from enchantments
        /// </summary>
        public override float GetSkillMod_Multiplier(Skill skill)
        {
            if ((skillModMultiplierCache ??= new()).TryGetValue(skill, out var value))
                return value;

            value = base.GetSkillMod_Multiplier(skill);

            skillModMultiplierCache[skill] = value;

            return value;
        }

        private int? bodyArmorModCache;

        /// <summary>
        /// Returns the base armor modifier from enchantments
        /// </summary>
        public override int GetBodyArmorMod()
        {
            if (bodyArmorModCache.HasValue)
                return bodyArmorModCache.Value;

            bodyArmorModCache = base.GetBodyArmorMod();

            return bodyArmorModCache.Value;
        }

        private Dictionary<DamageType, float> resistanceModCache;

        /// <summary>
        /// Gets the resistance modifier for a damage type
        /// </summary>
        public override float GetResistanceMod(DamageType damageType)
        {
            if ((resistanceModCache ??= new()).TryGetValue(damageType, out var value))
                return value;

            value = base.GetResistanceMod(damageType);

            resistanceModCache[damageType] = value;

            return value;
        }

        private Dictionary<DamageType, float> protectionResistanceModCache;

        /// <summary>
        /// Gets the resistance modifier for a damage type
        /// </summary>
        public override float GetProtectionResistanceMod(DamageType damageType)
        {
            if ((protectionResistanceModCache ??= new()).TryGetValue(damageType, out var value))
                return value;

            value = base.GetProtectionResistanceMod(damageType);

            protectionResistanceModCache[damageType] = value;

            return value;
        }

        private Dictionary<DamageType, float> vulnerabilityResistanceModCache;

        /// <summary>
        /// Gets the resistance modifier for a damage type
        /// </summary>
        public override float GetVulnerabilityResistanceMod(DamageType damageType)
        {
            if ((vulnerabilityResistanceModCache ??= new()).TryGetValue(damageType, out var value))
                return value;

            value = base.GetVulnerabilityResistanceMod(damageType);

            vulnerabilityResistanceModCache[damageType] = value;

            return value;
        }

        private Dictionary<CreatureVital, float> regenerationModCache;

        /// <summary>
        /// Gets the regeneration modifier for a vital type
        /// (regeneration / rejuvenation / mana renewal)
        /// </summary>
        public override float GetRegenerationMod(CreatureVital vital)
        {
            if ((regenerationModCache ??= new()).TryGetValue(vital, out var value))
                return value;

            value = base.GetRegenerationMod(vital);

            regenerationModCache[vital] = value;

            return value;
        }


        private int? damageBonusCache;

        /// <summary>
        /// Returns the weapon damage modifier, ie. Blood Drinker
        /// </summary>
        public override int GetDamageBonus()
        {
            if (damageBonusCache.HasValue)
                return damageBonusCache.Value;

            damageBonusCache = base.GetDamageBonus();

            return damageBonusCache.Value;
        }

        private float? damageModCache;

        /// <summary>
        /// Returns the DamageMod for bow / crossbow
        /// </summary>
        public override float GetDamageMod()
        {
            if (damageModCache.HasValue)
                return damageModCache.Value;

            damageModCache = base.GetDamageMod();

            return damageModCache.Value;
        }

        private float? attackModCache;

        /// <summary>
        /// Returns the attack skill modifier, ie. Heart Seeker
        /// </summary>
        public override float GetAttackMod()
        {
            if (attackModCache.HasValue)
                return attackModCache.Value;

            attackModCache = base.GetAttackMod();

            return attackModCache.Value;
        }

        private int? weaponSpeedModCache;

        /// <summary>
        /// Returns the weapon speed modifier, ie. Swift Killer
        /// </summary>
        public override int GetWeaponSpeedMod()
        {
            if (weaponSpeedModCache.HasValue)
                return weaponSpeedModCache.Value;

            weaponSpeedModCache = base.GetWeaponSpeedMod();

            return weaponSpeedModCache.Value;
        }

        private float? defenseModCache;

        /// <summary>
        /// Returns the defense skill modifier, ie. Defender
        /// </summary>
        public override float GetDefenseMod()
        {
            if (defenseModCache.HasValue)
                return defenseModCache.Value;

            defenseModCache = base.GetDefenseMod();

            return defenseModCache.Value;
        }

        private float? manaConvModCache;

        /// <summary>
        /// Returns the mana conversion bonus modifier, ie. Hermetic Link / Void
        /// </summary>
        public override float GetManaConvMod()
        {
            if (manaConvModCache.HasValue)
                return manaConvModCache.Value;

            manaConvModCache = base.GetManaConvMod();

            return manaConvModCache.Value;
        }

        private float? elementalDamageModCache;

        /// <summary>
        /// Returns the elemental damage bonus modifier, ie. Spirit Drinker / Loather
        /// </summary>
        public override float GetElementalDamageMod()
        {
            if (elementalDamageModCache.HasValue)
                return elementalDamageModCache.Value;

            elementalDamageModCache = base.GetElementalDamageMod();

            return elementalDamageModCache.Value;
        }

        private float? varianceModCache;

        /// <summary>
        /// Returns the weapon damage variance modifier
        /// </summary>
        /// 
        public override float GetVarianceMod()
        {
            if (varianceModCache.HasValue)
                return varianceModCache.Value;

            varianceModCache = base.GetVarianceMod();

            return varianceModCache.Value;
        }

        private int? armorModCache;

        /// <summary>
        /// Returns the additive armor level modifier, ie. Impenetrability
        /// </summary>
        public override int GetArmorMod()
        {
            if (armorModCache.HasValue)
                return armorModCache.Value;

            armorModCache = base.GetArmorMod();

            return armorModCache.Value;
        }

        private Dictionary<DamageType, float> armorModVsTypeModCache;

        /// <summary>
        /// Gets the additive armor level vs type modifier, ie. banes
        /// </summary>
        public override float GetArmorModVsType(DamageType damageType)
        {
            if ((armorModVsTypeModCache ??= new()).TryGetValue(damageType, out var value))
                return value;

            value = base.GetArmorModVsType(damageType);

            armorModVsTypeModCache[damageType] = value;

            return value;
        }

        private int? resistLockpickCache;

        public override int GetResistLockpick()
        {
            if (resistLockpickCache.HasValue)
                return resistLockpickCache.Value;

            resistLockpickCache = base.GetResistLockpick();

            return resistLockpickCache.Value;
        }

        private Dictionary<PropertyInt, int> ratingCache;

        public override int GetRating(PropertyInt property)
        {
            if ((ratingCache ??= new()).TryGetValue(property, out var value))
                return value;

            value = base.GetRating(property);

            ratingCache[property] = value;

            return value;
        }

        private int? netherDotDamageRatingCache;

        public override int GetNetherDotDamageRating()
        {
            if (netherDotDamageRatingCache == null)
                netherDotDamageRatingCache = base.GetNetherDotDamageRating();

            return netherDotDamageRatingCache.Value;
        }

        private float? xpBonusCache;

        public override float GetXPBonus()
        {
            if (xpBonusCache == null)
                xpBonusCache = base.GetXPBonus();

            return xpBonusCache.Value;
        }

        public override bool StartCooldown(WorldObject item)
        {
            var result = base.StartCooldown(item);

            if (result)
                ClearCache();

            return result;
        }
    }
}
