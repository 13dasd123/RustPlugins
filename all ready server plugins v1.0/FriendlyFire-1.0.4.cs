using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("FriendlyFire", "Nimant", "1.0.4")]    
    public class FriendlyFire : RustPlugin
    {        					
						
		#region Variables				
						
		[PluginReference] 
		private Plugin Clans;				
		
		[PluginReference] 
		private Plugin Friends;
								
		private static HashSet<ulong> PlayersWithEnabledDamage = new HashSet<ulong>();
		private static Dictionary<ulong, List<ulong>> PlayerFriends = new Dictionary<ulong, List<ulong>>();
		
		#endregion
		
		#region Hooks
		
		private void Init()
		{
			LoadVariables();
			LoadDefaultMessages();
			LoadData();
		}
		
		private void OnServerInitialized() 
		{
			if (configData.UseNoFriendDamage || configData.UseNoClanDamage)
				Subscribe("OnEntityTakeDamage");		
		}	
		
		private void Unload()
		{
			if (configData.UseNoFriendDamage || configData.UseNoClanDamage)
				Unsubscribe("OnEntityTakeDamage");
			
			SaveData();
		}
		
		private void OnServerSave() => SaveData();
		
		private bool? OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
			if (player == null || !player.userID.IsSteamId() || info?.Initiator == null) return null;
			var aggressor = info?.Initiator.ToPlayer();
			if (aggressor == null || !aggressor.userID.IsSteamId()) return null;						
			
			if (PlayersWithEnabledDamage.Contains(aggressor.userID)) return null;
			
			if (!PlayerFriends.ContainsKey(aggressor.userID))
				InitFriends(aggressor.userID);
			
			if (PlayerFriends[aggressor.userID].Contains(player.userID))
			{
				DamageType type = info.damageTypes.GetMajorityDamageType();								
				
				if (type != null && type == DamageType.Explosion)
				{
					if (configData.IgnoreExplosion)
					{	
						DisableDamage(info);
						return false;
					}
					else
						return null;
				}
				
				if (type != null && type == DamageType.Heat)
				{
					if (configData.IgnoreFire)
					{	
						DisableDamage(info);
						return false;
					}
					else
						return null;
				}								
								
				DisableDamage(info);
				return false;					
			}	
			
			return null;
        }
		
		private void OnPlayerConnected(BasePlayer player) => InitFriends(player.userID);
        
		// поддержка Friends от Moscow.ovh
		
		private void OnActiveFriendsUpdate(BasePlayer player) => InitFriends(player.userID);
		
		private void OnActiveFriendsUpdateUserId(ulong userID) => InitFriends(userID);		
		
		// поддержка Friends c Oxide
		
		private void OnFriendAdded(string userID) => InitFriends((ulong)Convert.ToInt64(userID));		
		
		private void OnFriendRemoved(string userID) => InitFriends((ulong)Convert.ToInt64(userID));		
		
		// без поддержки Clans от Moscow.ovh и Oxide (из-за неподходящих api)
		
		// поддержка собственного Clans (еще не разработан)
		
		private void OnClanMemberAdded(string tag, ulong userID) => InitFriends(userID);		
		
		private void OnClanMemberRemoved(string tag, ulong userID) => InitFriends(userID);
		
		// private List<ulong> GetClanMembers(string tag);
		
		// поддержка Clans Reborn (#1)
		
		private void OnClanMemberJoined(string playerId, object members_)
		{
			if (string.IsNullOrEmpty(playerId) || !playerId.IsSteamId() || members_ == null) return;
			
			var members = new List<string>();
			var err = false;
			
			try
			{
				var dic = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(members_), new KeyValuesConverter());
				if (dic is Dictionary<string, object>)
					members = dic.Keys.ToList();
			}
			catch { err = true; }
			
			if (err || members == null || members.Count() == 0)
			{
				try
				{
					err = false;
					members = new List<string>();
					
					var list = JsonConvert.DeserializeObject<List<string>>(JsonConvert.SerializeObject(members_), new KeyValuesConverter());
					if (list is List<string>)					
						members = list;
				}
				catch { err = true; }
			}
			
			if (err || members == null || members.Count() == 0) return;
			
			members.Add(playerId);
			CommunityEntity.ServerInstance.StartCoroutine(InitFriendsProcess(members));
		}
		
		private void OnClanMemberGone(string playerId, object members_)
		{
			if (string.IsNullOrEmpty(playerId) || !playerId.IsSteamId() || members_ == null) return;
			
			var members = new List<string>();
			var err = false;
			
			try
			{
				var dic = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(members_), new KeyValuesConverter());
				if (dic is Dictionary<string, object>)
					members = dic.Keys.ToList();
			}
			catch { err = true; }
			
			if (err || members == null || members.Count() == 0)
			{
				try
				{
					err = false;
					members = new List<string>();
					
					var list = JsonConvert.DeserializeObject<List<KeyValuePair<string, object>>>(JsonConvert.SerializeObject(members_), new KeyValuesConverter());
					if (list is List<KeyValuePair<string, object>>)					
						foreach (var pair in list)
							members.Add(pair.Key);
				}
				catch { err = true; }
			}
			
			if (err || members == null || members.Count() == 0) return;
			
			members.Add(playerId);
			CommunityEntity.ServerInstance.StartCoroutine(InitFriendsProcess(members));
		}
		
		private void OnClanDisbanded(object members_)
		{
			if (members_ == null) return;
			
			var members = new List<string>();
			var err = false;
			
			try
			{
				var dic = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(members_), new KeyValuesConverter());
				if (dic is Dictionary<string, object>)
					members = dic.Keys.ToList();
			}
			catch { err = true; }
						
			if (err || members == null || members.Count() == 0) return;
			
			CommunityEntity.ServerInstance.StartCoroutine(InitFriendsProcess(members));
		}
		
		#endregion
		
		#region Main
		
		private IEnumerator InitFriendsProcess(List<string> members)
		{
			foreach (var memberId_ in members)
			{
				if (string.IsNullOrEmpty(memberId_) || !memberId_.IsSteamId()) continue;
				var memberId = (ulong)Convert.ToInt64(memberId_);
				
				InitFriends(memberId);
				
				yield return new WaitForEndOfFrame();
			}		
		}
				
		private void InitFriends(ulong userID)
		{		
			List<ulong> friends_ = new List<ulong>();
			
			if (configData.UseNoFriendDamage && Friends != null)
			{					
				var friends = Friends.Call("GetFriends", userID);				
				
				if (friends != null && friends is ulong[])								
					friends_.AddRange(friends as ulong[]);								
			}
			
			if (configData.UseNoClanDamage && Clans != null)
			{						
				var friends = Clans.Call("GetClanMembers", userID);
				
				if (friends != null && friends is List<ulong>)
					friends_.AddRange(friends as List<ulong>);				
			}
			
			friends_.Remove(userID);
			
			if (!PlayerFriends.ContainsKey(userID))
				PlayerFriends.Add(userID, friends_);
			else
				PlayerFriends[userID] = friends_;
		}
		
		#endregion
		
		#region Common

		private void DisableDamage(HitInfo info)
		{
			info.damageTypes = new DamageTypeList();
            info.DidHit = false;
            info.HitEntity = null;
            info.Initiator = null;
            info.DoHitEffects = false;
            info.HitMaterial = 0;
		}
		
		#endregion		

		#region Commands
		
		[ChatCommand("ff")]
        private void CmdChatFF(BasePlayer player, string command, string[] args)
        {
            if (!configData.UseNoFriendDamage && !configData.UseNoClanDamage) return;
            
			if (args.Length == 0)
			{
				SendReply(player, string.Format(GetLangMessage("CMD.FF.HELP"), PlayersWithEnabledDamage.Contains(player.userID) ? "включён" : "выключен"));
                return;
			}	
			
			var param = args[0].ToLower();
			
            if (param != "on" && param != "off")
            {
                SendReply(player, string.Format(GetLangMessage("CMD.FF.UNKNOWN.ARG"), args[0]));
                return;
            }
			            
            switch (param)
            {                
                case "on":             
                    if (PlayersWithEnabledDamage.Contains(player.userID))
                        SendReply(player, GetLangMessage("FRIENDLY.FIRE.ALREADY.ON"));
                    else
                    {
                        PlayersWithEnabledDamage.Add(player.userID);
                        SendReply(player, GetLangMessage("FRIENDLY.FIRE.ON"));						
                    }
                    break;                
                case "off":                
                    if (!PlayersWithEnabledDamage.Contains(player.userID))
                        SendReply(player, GetLangMessage("FRIENDLY.FIRE.ALREADY.OFF"));
                    else
                    {
                        PlayersWithEnabledDamage.Remove(player.userID);
                        SendReply(player, GetLangMessage("FRIENDLY.FIRE.OFF"));
                    }
                    break;                
            }
        }
		
		#endregion

		#region Lang
		
        private void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {                
				{"CMD.FF.HELP", "ДОСТУПНЫЕ КОМАНДЫ:\n<color=#aae9f2>/ff on</color> - включить огонь по друзьям/соклановцам.\n<color=#aae9f2>/ff off</color> - выключить огонь по друзьям/соклановцам.\nТекущее состояние <color=#aae9f2>{0}</color>."},
				{"FRIENDLY.FIRE.ON", "Вы включили огонь по друзьям/соклановцам."},
				{"FRIENDLY.FIRE.OFF", "Вы выключили огонь по друзьям/соклановцам."},
				{"FRIENDLY.FIRE.ALREADY.ON", "У вас уже включен огонь по друзьям/соклановцам."},
				{"FRIENDLY.FIRE.ALREADY.OFF", "У вас уже выключен огонь по друзьям/соклановцам."},
				{"CMD.FF.UNKNOWN.ARG", "Неизвестный параметр <color=#aae9f2>\"{0}\"</color>.\nИспользуйте <color=#aae9f2>/ff</color> чтобы посмотреть список доступных команд."}
            }, this);
        }

        private string GetLangMessage(string key, string steamID = null) => lang.GetMessage(key, this, steamID);
		
		#endregion		
		
		#region Config        				
		
        private static ConfigData configData;
		
        private class ConfigData
        {            						
			[JsonProperty(PropertyName = "Разрешить отключение огня по друзьям")]
			public bool UseNoFriendDamage;
			[JsonProperty(PropertyName = "Разрешить отключение огня по соклановцам")]
			public bool UseNoClanDamage;
			[JsonProperty(PropertyName = "Игнорировать урон от взрывов")]
			public bool IgnoreExplosion;
			[JsonProperty(PropertyName = "Игнорировать урон от огня")]
			public bool IgnoreFire;
        }
		
        private void LoadVariables() => configData = Config.ReadObject<ConfigData>();        
		
        protected override void LoadDefaultConfig()
        {
            var config = new ConfigData
            {
                UseNoFriendDamage = false,
				UseNoClanDamage = false,
				IgnoreExplosion = false,
				IgnoreFire = false
            };
            SaveConfig(config);
			timer.Once(0.1f, ()=>SaveConfig(config));
        }        
		
        private void SaveConfig(ConfigData config) => Config.WriteObject(config, true);
		
        #endregion
		
		#region Data
		
		private void LoadData() => PlayersWithEnabledDamage = Interface.GetMod().DataFileSystem.ReadObject<HashSet<ulong>>("FriendlyFireData");					
		
		private void SaveData() => Interface.GetMod().DataFileSystem.WriteObject("FriendlyFireData", PlayersWithEnabledDamage);		
		
		#endregion
		
    }
}