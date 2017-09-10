﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Live.Data;

namespace Hearthstone_Deck_Tracker.Live
{
	internal class BoardStateWatcher
	{
		private const int UpdateDelay = 1000;
		private const int RepeatDelay = 10000;
		private bool _update;
		private bool _running;
		private BoardState _currentBoardState;
		private DateTime _currentBoardStateTime = DateTime.MinValue;
		public event Action<BoardState> OnNewBoardState;

		public void Stop()
		{
			_update = false;
			_currentBoardState = null;
		}

		public async void Start()
		{
			if(_running)
				return;
			_running = true;
			_update = true;
			while(_update)
			{
				var boardState = GetBoardState();
				var delta = (DateTime.Now - _currentBoardStateTime).TotalMilliseconds;
				var forceInvoke = delta > RepeatDelay && boardState != null && _currentBoardState != null;
				if(forceInvoke || (!boardState?.Equals(_currentBoardState) ?? false))
				{
					OnNewBoardState?.Invoke(boardState);
					_currentBoardState = boardState;
					_currentBoardStateTime = DateTime.Now;
				}
				await Task.Delay(UpdateDelay);
			}
			_running = false;
		}

		private BoardState GetBoardState()
		{
			if(Core.Game.PlayerEntity == null || Core.Game.OpponentEntity == null)
				return null;

			int ZonePosition(Entity e) => e.GetTag(GameTag.ZONE_POSITION);
			int DbfId(Entity e) => e?.Card.DbfIf ?? 0;
			int[] SortedDbfIds(IEnumerable<Entity> entities) => entities.OrderBy(ZonePosition).Select(DbfId).ToArray();
			int HeroId(Entity playerEntity) => playerEntity.GetTag(GameTag.HERO_ENTITY);
			int WeaponId(Entity playerEntity) => playerEntity.GetTag(GameTag.WEAPON);
			Entity Find(Player p, int entityId) => p.PlayerEntities.FirstOrDefault(x => x.Id == entityId);
			Entity FindHeroPower(Player p) => p.PlayerEntities.FirstOrDefault(x => x.IsHeroPower && x.IsInPlay);

			var player = Core.Game.Player;
			var opponent = Core.Game.Opponent;

			var deck = DeckList.Instance.ActiveDeck;
			var games = deck?.GetRelevantGames();
			var fullDeckList = DeckList.Instance.ActiveDeckVersion?.Cards.ToDictionary(x => x.DbfIf, x => x.Count);
			int FullCount(int dbfId) => fullDeckList == null ? 0 : fullDeckList.TryGetValue(dbfId, out var count) ? count : 0;

			BoardStateQuest quest = null;
			var questEntity = player.Quests.FirstOrDefault();
			if(questEntity != null)
			{
				quest = new BoardStateQuest
				{
					DbfId = questEntity.Card.DbfIf,
					Progress = questEntity.GetTag(GameTag.QUEST_PROGRESS),
					Total = questEntity.GetTag(GameTag.QUEST_PROGRESS_TOTAL)
				};
			}

			return new BoardState
			{
				Player = new BoardStatePlayer
				{
					Board = SortedDbfIds(player.Board.Where(x => x.IsMinion)),
					Deck = new BoardStateDeck
					{
						Cards =  player.PlayerCardList.ToDictionary(x => x.DbfIf, x => new []{x.Count, FullCount(x.DbfIf)}),
						Name = deck?.Name,
						Format = (deck?.IsWildDeck ?? false) ? FormatType.FT_WILD : FormatType.FT_STANDARD,
						Hero = Database.GetHeroCardFromClass(deck?.Class)?.DbfIf ?? 0,
						Wins = games?.Count(g => g.Result == GameResult.Win) ?? 0,
						Losses = games?.Count(g => g.Result == GameResult.Loss) ?? 0,
						Size = player.DeckCount
					},
					Secrets = SortedDbfIds(player.Secrets),
					Hero = DbfId(Find(player, HeroId(Core.Game.PlayerEntity))),
					Hand = new BoardStateHand
					{
						Cards = SortedDbfIds(player.Hand),
						Size = player.HandCount
					},
					HeroPower = DbfId(FindHeroPower(player)),
					Weapon = DbfId(Find(player, WeaponId(Core.Game.PlayerEntity))),
					Quest = quest,
					Fatigue = Core.Game.PlayerEntity.GetTag(GameTag.FATIGUE)
				},
				Opponent = new BoardStatePlayer
				{
					Board = SortedDbfIds(opponent.Board.Where(x => x.IsMinion)),
					Deck = new BoardStateDeck
					{
						Size = opponent.DeckCount
					},
					Hand = new BoardStateHand
					{
						Size = opponent.HandCount
					},
					Hero = DbfId(Find(opponent, HeroId(Core.Game.OpponentEntity))),
					HeroPower = DbfId(FindHeroPower(opponent)),
					Weapon = DbfId(Find(opponent, WeaponId(Core.Game.OpponentEntity))),
				},
			};
		}
	}
}