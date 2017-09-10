using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker.Controls.Error;
using Hearthstone_Deck_Tracker.HsReplay.Data;
using Hearthstone_Deck_Tracker.Live.Data;
using Hearthstone_Deck_Tracker.Utility;
using Hearthstone_Deck_Tracker.Utility.Logging;
using HSReplay.OAuth;
using HSReplay.OAuth.Data;

namespace Hearthstone_Deck_Tracker.HsReplay
{
	// ReSharper disable InconsistentNaming
	internal sealed class HSReplayNetOAuth
	{
		private static readonly JsonSerializer<OAuthData> Serializer;
		private static readonly Lazy<OAuthClient> Client;
		private static readonly Lazy<OAuthData> Data;

		private static readonly int[] Ports = { 17781, 17782, 17783, 17784, 17785, 17786, 17787, 17788, 17789 };
		private const string HSReplayNetClientId = "jIpNwuUWLFI6S3oeQkO3xlW6UCnfogw1IpAbFXqq";
		private const string TwitchExtensionId = "apwln3g3ia45kk690tzabfp525h9e1";
		private const string SuccessUrl = "https://hsdecktracker.net/hsreplaynet/oauth_success/";
		private const string ErrorUrl = "https://hsdecktracker.net/hsreplaynet/oauth_error/";


		static HSReplayNetOAuth()
		{
			Serializer = new JsonSerializer<OAuthData>("hsreplay_oauth", true);
			Data = new Lazy<OAuthData>(Serializer.Load);
			Client = new Lazy<OAuthClient>(LoadClient);
		}

		private static OAuthClient LoadClient()
		{
			return new OAuthClient(HSReplayNetClientId, Helper.GetUserAgent(), Data.Value.TokenData);
		}

		public static void Save() => Serializer.Save(Data.Value);

		public static async Task<bool> Authenticate()
		{
			var url = Client.Value.GetAuthenticationUrl(new[] { Scope.Webhooks }, null, Ports);
			var callbackTask = Client.Value.ReceiveAuthenticationCallback(SuccessUrl, ErrorUrl);
			if(!Helper.TryOpenUrl(url))
				ErrorManager.AddError("Could not open browser to complete authentication.", $"Please go to '{url}' to continue authentication.", true);
			var data = await callbackTask;
			Data.Value.Code = data.Code;
			Data.Value.RedirectUrl = data.RedirectUrl;
			await UpdateToken();
			Save();
			return true;
		}

		public static async Task<bool> UpdateToken()
		{
			var data = Data.Value;
			if(data.TokenData != null && (DateTime.Now - data.TokenDataCreatedAt).TotalSeconds < data.TokenData.ExpiresIn)
				return true;
			if(string.IsNullOrEmpty(data.Code) || string.IsNullOrEmpty(data.RedirectUrl))
			{
				Log.Error("Could not update token, we don't have a code or redirect url.");
				return false;
			}
			if(!string.IsNullOrEmpty(data.TokenData?.RefreshToken))
			{
				try
				{
					var tokenData = await Client.Value.RefreshToken();
					if(tokenData != null)
					{
						UpdateTokenData(tokenData);
						return true;
					}
				}
				catch(Exception e)
				{
					Log.Error(e);
				}
			}
			try
			{
				var tokenData = await Client.Value.GetToken(data.Code, data.RedirectUrl);
				if(tokenData == null)
				{
					Log.Error("We did not receive any token data.");
					return false;
				}
				UpdateTokenData(tokenData);
				return true;
			}
			catch(Exception e)
			{
				Log.Error(e);
				return false;
			}
		}

		public static async Task<bool> UpdateTwitchUsers()
		{
			try
			{
				if(!await UpdateToken())
				{
					Log.Error("Could not update token data");
					return false;
				}
				var twitchAccounts = await Client.Value.GetTwitchAccounts();
				Data.Value.TwitchUsers = twitchAccounts;
				Save();
				return twitchAccounts.Count != 0;
			}
			catch(Exception e)
			{
				Log.Error(e);
				return false;
			}
		}

		public static bool IsAuthenticated => !string.IsNullOrEmpty(Data.Value.Code);

		public static List<TwitchAccount> TwitchUsers => Data.Value.TwitchUsers;

		public static void UpdateTokenData(TokenData data)
		{
			Data.Value.TokenData = data;
			Data.Value.TokenDataCreatedAt = DateTime.Now;
			Save();
		}

		public static async Task SendTwitchPayload(Payload payload)
		{
			await Client.Value.SendTwitchUpdate(Config.Instance.SelectedTwitchUser, TwitchExtensionId, payload);
		}
	}
}
